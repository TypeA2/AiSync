using AiSync;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using WatsonTcp;
using LibVLCSharp.Shared;
using System.Diagnostics.CodeAnalysis;

namespace AiSyncServer {
    internal enum AiConnectionState {
        Closed,
        Connected,
        SentHasFile,
    }

    internal class AiClientConnection {
        public AiConnectionState State { get; set; } = AiConnectionState.Closed;
    }

    internal sealed class AiServer : IDisposable {
        
        public event EventHandler? ServerStarted;
        public event EventHandler? ClientConnected;
        public event EventHandler? ClientDisconnected;

        public event EventHandler<PlayingChangedEventArgs>? PlayingChanged;
        public event EventHandler<PositionChangedEvent>? PositionChanged;

        private readonly ILoggerFactory _logger_factory;
        private readonly ILogger _logger;

        private readonly WatsonTcpServer server;

        private class ServerMedia : IDisposable {
            private readonly AiMediaSource.ParsedMedia _media;
            public AiMediaSource.ParsedMedia Media {
                get {
                    DisposeCheck(_disposed);
                    return _media;
                }
            }

            private readonly AiPlaybackTimer _timer;
            public AiPlaybackTimer Timer {
                get {
                    DisposeCheck(_disposed);
                    return _timer;
                }
            }

            /* Stopped is only possible without any media present */
            public PlayingState State => Timer.State;
            public long Position => Timer.Position;

            public long Duration => Media.Duration;

            private readonly AiMediaSource _source;
            private bool _disposed = false;

            public ServerMedia(AiMediaSource source, AiMediaSource.ParsedMedia media, AiPlaybackTimer timer) {
                _source = source;
                _media = media;
                _timer = timer;
            }

            public void Dispose() {
                if (_disposed) {
                    return;
                }

                Timer?.Dispose();
                _source.Unload(Media);

                _disposed = true;
            }
            
            private static void DisposeCheck([DoesNotReturnIf(true)] bool disposed) {
                if (disposed) {
                    throw new ObjectDisposedException(nameof(ServerMedia));
                }
            }

            public void Play(long pos) => Timer.Play(pos);
            public void Pause(long pos) => Timer.Pause(pos);
            public void Seek(long pos) => Timer.Seek(pos);
        }

        private readonly AiMediaSource media_source = new();
        private ServerMedia? media;

        public long Position => media?.Position ?? 0;
        public long Duration => media?.Duration ?? 0;
        public PlayingState State => media?.State ?? PlayingState.Stopped;

        public int Delay { get; set; }

        private bool during_resync = false;

        private bool _disposed = false;

        private readonly List<Guid> _clients = new();

        public IList<Guid> Clients { get => _clients; }

        public int ClientCount { get => _clients.Count; }

        public static long CloseEnoughValue { get => 1500; }

        
        private readonly ManualResetEventSlim close_event = new();

        public AiServer(ILoggerFactory fact, IPAddress addr, ushort port) : this(fact, new IPEndPoint(addr, port)) { }

        public AiServer(ILoggerFactory fact, IPEndPoint endpoint) {
            _logger_factory = fact;
            _logger = _logger_factory.CreateLogger("AiServer");

            server = new WatsonTcpServer(endpoint.Address.ToString(), endpoint.Port) ;
            server.Settings.Logger = (level, msg) => {
                _logger.Log(level switch {
                    Severity.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
                    Severity.Info => Microsoft.Extensions.Logging.LogLevel.Information,
                    Severity.Warn => Microsoft.Extensions.Logging.LogLevel.Warning,
                    Severity.Error => Microsoft.Extensions.Logging.LogLevel.Error,
                    Severity.Alert => Microsoft.Extensions.Logging.LogLevel.Warning,
                    Severity.Critical => Microsoft.Extensions.Logging.LogLevel.Critical,
                    Severity.Emergency => Microsoft.Extensions.Logging.LogLevel.Critical,
                    _ => Microsoft.Extensions.Logging.LogLevel.Warning
                }, "{}", msg);
            };

            server.Events.MessageReceived += OnMessageReceived;
            server.Events.ClientConnected += OnClientConnected;
            server.Events.ClientDisconnected += OnClientDisconnected;
            server.Events.ExceptionEncountered += (_, e) => {
                _logger.LogWarning("Exception {}: {}", e.Exception.GetType(), e.Exception.Message);
            };

            server.Events.ServerStopped += (_, _) => {
                _logger.LogInformation("Server stopped");
                close_event.Set();
            };

            server.Callbacks.SyncRequestReceived = OnSyncRequestReceived;

            server.Events.ServerStarted += (_, _) =>
                ServerStarted?.Invoke(this, EventArgs.Empty);
        }

        public async void Dispose() {
            if (_disposed) {
                return;
            }

            if (server.IsListening) {
                await Task.Run(StopMedia);
            }
            
            List<Task> tasks = new();

            FieldInfo[] fields = server
                .GetType()
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

            PropertyInfo? data_receiver_field = typeof(ClientMetadata).GetProperty("DataReceiver", BindingFlags.NonPublic | BindingFlags.Instance);
            
            /* Just accept the crash if this fails */
            if (data_receiver_field is not null) {
                foreach (FieldInfo field in fields) {
                    if (field.FieldType == typeof(ConcurrentDictionary<Guid, ClientMetadata>)) {
                        /* Gather all tasks belonging to clients */
                        var clients = field.GetValue(server) as ConcurrentDictionary<Guid, ClientMetadata>;

                        /* Add all non-null DataReceiver fields */
                        if (clients is not null) {
                            tasks.AddRange(
                                clients.Values
                                .Select(c => data_receiver_field.GetValue(c) as Task)
                                .WhereNotNull());
                        }
                    }
                }
            }

            _logger.LogDebug("Got receiver tasks for {} clients", tasks.Count);
            
            server.DisconnectClients();
            server.Stop();

            /* WatsonTcpServer does not await it's tasks when disposing, so do it manually */
            foreach (Task task in server.GetPrivateTasks()) {
                await task;
            }

            await Task.WhenAll(tasks);

            server.Dispose();

            /* Disposes all loaded media */
            media?.Dispose();
            media = null;

            media_source.Dispose();

            _disposed = true;
        }

        private static void DisposeCheck([DoesNotReturnIf(true)] bool disposed) {
            if (disposed) {
                throw new ObjectDisposedException(nameof(AiPlaybackTimer));
            }
        }

        public void Start() {
            DisposeCheck(_disposed);

            _logger.LogInformation("Starting server");
            server.Start();
        }

        public void Stop() {
            DisposeCheck(_disposed);

            _logger.LogInformation("Stopping server");
            StopMedia();

            server.DisconnectClients();
            server.Stop();
        }

        public void PlayMedia(long pos) {
            DisposeCheck(_disposed);

            if (media is null) {
                return;
            }

            _logger.LogInformation("Play at {}", AiSync.Utils.FormatTime(pos));

            media.Play(pos);
        }

        public void PauseMedia(long pos) {
            DisposeCheck(_disposed);

            if (media is null) {
                return;
            }

            _logger.LogInformation("Pause at {}", AiSync.Utils.FormatTime(pos));

            media.Pause(pos);
        }

        public void SeekMedia(long pos) {
            DisposeCheck(_disposed);

            if (media is null) {
                return;
            }

            _logger.LogInformation("Seek to {}", AiSync.Utils.FormatTime(pos));

            media.Seek(pos);
        }

        public void StopMedia() {
            DisposeCheck(_disposed);
            
            _logger.LogInformation("Stopping playback");

            media?.Dispose();
            media = null;
        }

        public async Task LoadMedia(string path) {
            DisposeCheck(_disposed);

            if (media is not null) {
                _logger.LogCritical("Tried to open a file while one is already open");
                throw new InvalidOperationException("SetFile has already been called");
            }

            _logger.LogInformation("New file received: {}", path);

            AiMediaSource.ParsedMedia parsed;

            try {
                parsed = await media_source.Load(path);
            } catch (VLCException e) {
                _logger.LogError("Error parsing media: {}", e.Message);
                media = null;

                /* Stop everyone */
                PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(PlayingState.Stopped));
                return;
            }

            /* Notify all clients and wait */
            (Guid guid, bool success)[] results = await Task.WhenAll(Clients.Select(SendAndWaitNewFileAsync).ToArray());
                    
            /* Remove all failed clients */
            foreach (Guid guid in results.Where(result => !result.success).Select(result => result.guid)) {
                _logger.LogInformation("Removing {} for failing to reply", guid);
                server.DisconnectClient(guid);
            }

            _logger.LogInformation("All clients ready to play");

            /* Tell all clients the server is ready */
            await SendToAll<AiServerReady>();

            /* Start status polling thread */
            AiPlaybackTimer timer = new(_logger_factory, parsed.Duration, 75, 100);
            timer.PositionChanged += async (o, e) => {
                if (e.IsSeek) {
                    _logger.LogDebug("New position: {} (seek: {})", e.Position, e.IsSeek);
                    await SendToAll(new AiServerRequestSeek() { Target = e.Position });
                }

                PositionChanged?.Invoke(o, e);
            };

            timer.PlayingChanged += async (o, e) => {
                _logger.LogInformation("New playing state: {}", e.State);
                switch (e.State) {
                    case PlayingState.Stopped: {
                        /* EOF or manual stop -> close file */
                        await SendToAll<AiFileClosed>();
                        break;
                    }

                    case PlayingState.Playing: {
                        await SendToAll(new AiServerRequestsPlay() { Position = timer.Position });
                        break;
                    }

                    case PlayingState.Paused: {
                        await SendToAll(new AiServerRequestsPause() { Position = timer.Position });
                        break;
                    }
                }

                PlayingChanged?.Invoke(o, e);
            };

            media = new ServerMedia(media_source, parsed, timer);
        }

        private Resp SendAndExpectMsg<Msg, Resp>(int ms, Guid guid, Msg src)
            where Msg : AiProtocolMessage
            where Resp : AiProtocolMessage {
            SyncResponse response = server.SendAndWait(ms, guid, src.Serialize());

            return response.Data.ToUtf8().AiDeserialize<Resp>();
        }

        /* Return all failed clients */
        private async Task<IEnumerable<Guid>> SendToAll<T>(T? msg = null) where T : AiProtocolMessage, new() {
            IEnumerable<(Guid guid, Task<bool> task)> tasks = Clients.Select((guid) => (guid, SendMessage(guid, msg)));

            await Task.WhenAll(tasks.Select(p => p.task));

            return tasks.Where(p => !p.task.Result).Select(p => p.guid);
        }

        private async Task<bool> SendMessage<T>(Guid guid, T? msg = null) where T : AiProtocolMessage, new() {
            if (Delay > 0) {
                // await Task.Delay(Delay);
            }

            return await server.SendAsync(guid, (msg ?? new T()).Serialize());
        }

        private (Guid, bool) SendAndWaitNewFile(Guid guid) {
            /* Notify client of new file, wait at most 1 minute for a response */
            try {
                SendAndExpectMsg<AiFileReady, AiFileParsed>(60 * 1000, guid, AiFileReady.FromCloseEnough(CloseEnoughValue));

                /* No exception means this client is ready */
                return (guid, true);

            /* Consume exceptions that indicate failure */
            } catch (TimeoutException) {
                
            } catch (InvalidMessageException) {

            }

            return (guid, false);
        }

        private Task<(Guid, bool)> SendAndWaitNewFileAsync(Guid guid) {
            return Task.Run(() => SendAndWaitNewFile(guid));
        }

        private AiClientStatus? GetClientStatus(Guid guid) {
            try {
                return SendAndExpectMsg<AiServerRequestsStatus, AiClientStatus>(5 * 1000, guid, new());
            } catch (TimeoutException) {
                
            } catch (InvalidMessageException) {

            }

            return null;
        }

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e) {
            string str = e.Data.ToUtf8();
            AiMessageType type = str.AiJsonMessageType();

            Delegate handler = str.AiJsonMessageType() switch {
                AiMessageType.ClientRequestsPause => HandleClientRequestsPause,
                AiMessageType.ClientRequestsPlay => HandleClientRequestsPlay,
                AiMessageType.ClientRequestsSeek => HandleClientRequestsSeek,
                AiMessageType.PauseResync => HandlePauseRsync,
                _ => HandleDefault,
            };

            ParameterInfo[] handler_params = handler.GetMethodInfo().GetParameters();

            /* Should never happen */
            if (handler_params.Length != 1 || !handler_params[0].ParameterType.IsAssignableTo(typeof(AiProtocolMessage))) {
                throw new ArgumentException("invalid delegate");
            }

            handler.DynamicInvoke(str.AiDeserialize(handler_params[0].ParameterType));
        }

        private async void OnClientConnected(object? sender, ConnectionEventArgs e) {
            _logger.LogInformation("Client connected: {}, {} total", e.Client.Guid, ClientCount);

            lock (_clients) {
                _clients.Add(e.Client.Guid);
            }

            ClientConnected?.Invoke(this, EventArgs.Empty);

            if (media is not null) {
                _logger.LogInformation("Sending status to {}", e.Client.Guid);

                (Guid _, bool success) = await SendAndWaitNewFileAsync(e.Client.Guid);
                if (success) {
                    await SendMessage<AiServerReady>(e.Client.Guid);

                    long pos = media.Position;
                    if (media.State == PlayingState.Playing) {
                        _logger.LogDebug("Playing new client at {}", pos);
                        await SendMessage(e.Client.Guid, new AiServerRequestsPlay() { Position = pos });
                    } else {
                        _logger.LogDebug("Pausing new client at {}", pos);
                        await SendMessage(e.Client.Guid, new AiServerRequestsPause() { Position = pos });
                    }

                } else if (success) {
                    _logger.LogInformation("Client rejected: {}", e.Client.Guid);
                    server.DisconnectClient(e.Client.Guid);
                    return;

                }
            }
        }

        private void OnClientDisconnected(object? sender, DisconnectionEventArgs e) {
            _logger.LogInformation("Client disconnected: {}, reason: {}", e.Client.Guid, e.Reason);
            
            lock (_clients) {
                _clients.Remove(e.Client.Guid);
            }
            
            ClientDisconnected?.Invoke(this, EventArgs.Empty);
        }

        private SyncResponse OnSyncRequestReceived(SyncRequest req) {
            string str = req.Data.ToUtf8();
            AiMessageType type = str.AiJsonMessageType();

            switch (type) {
                case AiMessageType.GetStatus: {

                    return req.ReplyWith(new AiServerStatus() {
                        State = media?.State ?? PlayingState.Stopped,
                        Position = media?.Position ?? 0,
                        Timestamp = DateTime.UtcNow,
                    });
                }

                default:
                    throw new InvalidMessageException(str.AiDeserialize<AiProtocolMessage>());
            }
        }

        private void HandleClientRequestsPause(AiClientRequestsPause msg) {
            _logger.LogDebug("ClientRequestsPause: {}", msg.Position);
            if (media is null) {
                return;
            }

            /* Use server position if <0 */
            long pos = (msg.Position < 0) ? media.Position : msg.Position;

            _logger.LogInformation("Pause requested at {}", AiSync.Utils.FormatTime(pos));

            PauseMedia(pos);
        }

        private void HandleClientRequestsPlay(AiClientRequestsPlay msg) {
            _logger.LogDebug("ClientRequestsPlay: {}", msg.Position);
            if (media is null) {
                return;
            }

            /* Use server position if <0 */
            long pos = (msg.Position < 0) ? media.Position : msg.Position;

            _logger.LogInformation("Play requested at {}", AiSync.Utils.FormatTime(pos));

            PlayMedia(pos);
        }

        private void HandleClientRequestsSeek(AiClientRequestSeek msg) {
            _logger.LogDebug("ClientRequestsSeek: {}", msg.Target);
            if (media is null) {
                return;
            }

            /* Seek to server pos if <0 */
            long target = (msg.Target < 0) ? media.Position : msg.Target;

            _logger.LogInformation("Seek requested to {}", AiSync.Utils.FormatTime(msg.Target));

            SeekMedia(target);
        }

        private async void HandlePauseRsync(AiPauseResync msg) {
            _logger.LogDebug("PauseResync");
            /* Only run 1 resync at a time */
            if (during_resync || media is null) {
                return;
            }

            during_resync = true;
            DateTime start = DateTime.Now;
            long target_pos = media.Position;

            Task<AiClientStatus?>[] tasks = Clients.Select(async guid => {
                return await Task.Run(() => GetClientStatus(guid));
            }).ToArray();

            /* Get earliest position of all clients */
            foreach (AiClientStatus status in (await Task.WhenAll(tasks)).WhereNotNull()) {
                /* TODO do stuff with `playing`, maybe? */

                /* Adjust position to same start time */
                double delta = (start - status.Timestamp).TotalMilliseconds;

                target_pos = Int64.Min(target_pos, (long)Math.Round(status.Position + delta));
            }

            _logger.LogInformation("Resync pausing at {}", AiSync.Utils.FormatTime(target_pos));

            PauseMedia(target_pos);

            during_resync = false;
        }

        private void HandleDefault(Guid client, AiProtocolMessage msg) {
            _logger.LogCritical("Unexpected message of type {} from {}", msg.Type, client);
            throw new InvalidMessageException(msg);
        }
    }
}
