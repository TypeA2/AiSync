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

namespace AiSyncServer {
    internal enum AiConnectionState {
        Closed,
        Connected,
        SentHasFile,
    }

    internal class AiClientConnection {
        public AiConnectionState State { get; set; } = AiConnectionState.Closed;
    }

    internal class PlayingChangedEventArgs : EventArgs {
        public bool IsPlaying { get; }

        public PlayingChangedEventArgs(bool new_status) : base() {
            IsPlaying = new_status;
        }
    }

    internal class PositionChangedEvent : EventArgs {
        public long Position { get; }

        public PositionChangedEvent(long position) :base() {
            Position = position;
        }
    }

    internal class AiServer : IDisposable {
        
        public event EventHandler? ServerStarted;
        public event EventHandler? ClientConnected;

        public event EventHandler<PlayingChangedEventArgs>? PlayingChanged;
        public event EventHandler<PositionChangedEvent>? PositionChanged;

        private readonly ILogger _logger;

        private readonly WatsonTcpServer server;

        public AiServer(ILoggerFactory fact, IPAddress addr, ushort port) : this(fact, new IPEndPoint(addr, port)) { }

        private readonly ConcurrentDictionary<Guid, AiClientConnection> clients = new();

        private bool has_file = false;

        private readonly object control_lock = new();

        public bool Playing { get; private set; } = false;

        private DateTime? latest_start;
        private long latest_start_pos = 0;

        public long Position {
            get {
                if (latest_start is not null) {
                    /* If playing, calculate, else latest_start_pos already contains right value */
                    if (Playing) {
                        TimeSpan? elapsed = DateTime.UtcNow - latest_start;

                        return (long)Math.Round(elapsed.Value.TotalMilliseconds) + latest_start_pos;
                    } else {
                        return latest_start_pos;
                    }
                }

                /* Playback hasn't started yet */
                return 0;
            }

            private set {
                latest_start = DateTime.UtcNow;
                latest_start_pos = value;
            }
        }

        public IEnumerable<Guid> Clients { get => clients.Keys; }
        public int ClientCount { get => clients.Count; }

        public static long CloseEnoughValue { get => 1500; }

        private Thread? poll_thread;

        private readonly CancellationTokenSource cancel_poll = new();

        private bool seeking = false;

        public AiServer(ILoggerFactory fact, IPEndPoint endpoint) {
            _logger = fact.CreateLogger("AiServer");

            server = new WatsonTcpServer(endpoint.Address.ToString(), endpoint.Port) ;

            server.Events.MessageReceived += OnMessageReceived;
            server.Events.ClientConnected += OnClientConnected;
            server.Events.ClientDisconnected += OnClientDisconnected;

            server.Callbacks.SyncRequestReceived = OnSyncRequestReceived;

            server.Events.ServerStarted += (_, _) =>
                ServerStarted?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose() {
            if (server.IsListening) {
                server.Stop();
            }

            server.Dispose();
            cancel_poll.Dispose();
        }

        public void Start() {
            _logger.LogInformation("Starting server");
            server.Start();
        }

        public void StopPlayback() {
            _logger.LogInformation("Stopping playback");
            has_file = false;
            cancel_poll.Cancel();
            poll_thread?.Join();
        }

        public async void SetHasFile() {
            if (has_file) {
                _logger.LogCritical("Tried to open a file while one is already open");
                throw new InvalidOperationException("SetHasFile has already been called");
            }

            _logger.LogInformation("New file received");
            has_file = true;

            Task<(Guid, bool)>[] tasks = Clients
                .Select(SendAndWaitNewFileAsync)
                .ToArray();

            await Task.WhenAll(tasks);
                    
            /* Remove all failed clients */
            foreach ((Guid guid, bool _) in tasks.Select(t => t.Result).Where(result => !result.Item2)) {
                _logger.LogInformation("Removing {} for failing to reply", guid);
                server.DisconnectClient(guid);
            }

            _logger.LogInformation("All clients ready to play");

            /* Tell all clients the server is ready */
            await Task.WhenAll(
                Clients
                    .Select(guid => SendMessage<AiServerReady>(guid))
                    .ToArray()
            );

            /* Start status polling thread */
            poll_thread = new Thread(PlaybackSyncPoll);
            poll_thread.Start();
        }

        private struct PollResult {
            public Guid guid;
            public bool error;
            public bool playing;
            public long position;
            public long delta;
        }

        private async void PlaybackSyncPoll() {
            const int poll_interval = 125;
            const int sync_interval = 750;

            _logger.LogInformation("Starting polling thread");

            long sleep_extra = 0;

            while (!cancel_poll.Token.IsCancellationRequested) {
                while (latest_start is null) {
                    Thread.Sleep(poll_interval);
                }

                DateTime start;
                TimeSpan elapsed;
                long server_pos;

                /* Only sync every sync_interval ms */
                do {
                    start = DateTime.UtcNow;
                    elapsed = start - latest_start.Value;

                    /* Do update position more often */
                    server_pos = Position;

                    PositionChanged?.Invoke(this, new PositionChangedEvent(Position));

                    Thread.Sleep(poll_interval);
                } while (elapsed.TotalMilliseconds < (sync_interval + sleep_extra));

                sleep_extra = 0;

                /* Don't do anything while paused */
                if (!Playing) {
                    continue;
                }

                /* Check all clients */
                Task<PollResult>[] tasks = Clients.Select(guid =>
                        Task.Run(() => {
                            PollResult result = default;
                            result.guid = guid;
                            result.error = !GetClientStatus(guid, out bool is_playing, out long position);

                            if (result.error) {
                                return result;
                            }

                            result.playing = is_playing;
                            result.delta = position - server_pos;
                            result.position = position;

                            /* TODO check pause/play mismatch */
                            _logger.LogDebug("{}: Δ={}, {} at {}", result.guid, result.delta,
                                is_playing ? "playing" : "paused", AiSync.Utils.FormatTime(position));

                            return result;
                        })
                    ).ToArray();

                await Task.WhenAll(tasks);

                if (seeking) {
                    seeking = false;
                    continue;
                }

                bool perform_seek = false;
                long seek_target = server_pos;

                /* Negative delta means client is behind, positive means client is ahead */
                foreach (PollResult result in tasks.Select(t => t.Result)) {
                    /* If a client is too far out of sync */
                    if (result.position >= 0 && Math.Abs(result.delta) > CloseEnoughValue) {
                        perform_seek = true;

                        if (result.delta < 0) {
                            /* Client is behind, set the global target further back */
                            seek_target = Int64.Min(seek_target, result.position);
                        } else {
                            /* If the client is ahead, just seek to server_pos */
                        }
                    }
                }

                /* Play it safe:
                 *  - If someone falls behind, pause everyone at that client's position.
                 *  - If someone is ahead, pause everyone at the server's position
                 */
                if (perform_seek) {
                    _logger.LogInformation("Correcting synchronization to {}", AiSync.Utils.FormatTime(seek_target));

                    latest_start = null;

                    HandleClientRequestsPause(new AiClientRequestsPause() {
                        Position = seek_target,
                    });

                    /* Give clients some time to actually adjust */
                    sleep_extra = sync_interval;
                }
            }
        }

        private Resp SendAndExpectMsg<Msg, Resp>(int ms, Guid guid, Msg src)
            where Msg : AiProtocolMessage
            where Resp : AiProtocolMessage {
            SyncResponse response = server.SendAndWait(ms, guid, src.Serialize());

            return response.Data.ToUtf8().AiDeserialize<Resp>();
        }

        private Task SendMessage<T>(Guid guid, T? msg = null) where T : AiProtocolMessage, new() {
            return server.SendAsync(guid, (msg ?? new T()).Serialize());
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

        private bool GetClientStatus(Guid guid, out bool is_playing, out long position) {
            is_playing = false;
            position = 0;
            try {
                AiClientStatus status = SendAndExpectMsg<AiServerRequestsStatus, AiClientStatus>(60 * 1000, guid, new());

                is_playing = status.IsPlaying;
                position = status.Position;

                return true;
            } catch (TimeoutException) {
                
            } catch (InvalidMessageException) {

            }

            return false;
        }

        private void OnMessageReceived(object? sender, MessageReceivedEventArgs e) {
            string str = e.Data.ToUtf8();
            AiMessageType type = str.AiJsonMessageType();

            Delegate handler = str.AiJsonMessageType() switch {
                AiMessageType.ClientRequestsPause => HandleClientRequestsPause,
                AiMessageType.ClientRequestsPlay => HandleClientRequestsPlay,
                AiMessageType.ClientRequestsSeek => HandleClientRequestsSeek,
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
            _logger.LogInformation("Client connected: {}", e.Client.Guid);

            clients.TryAdd(e.Client.Guid, new AiClientConnection() { State = AiConnectionState.Connected });

            ClientConnected?.Invoke(this, EventArgs.Empty);

            if (has_file) {
                _logger.LogInformation("Sending status to {}", e.Client.Guid);

                (Guid _, bool success) = await SendAndWaitNewFileAsync(e.Client.Guid);
                if (success) {
                    await SendMessage<AiServerReady>(e.Client.Guid);
                } else if (success) {
                    _logger.LogInformation("Client rejected: {}", e.Client.Guid);
                    server.DisconnectClient(e.Client.Guid);
                    return;

                }
            }
        }

        private void OnClientDisconnected(object? sender, DisconnectionEventArgs e) {
            _logger.LogInformation("Client disconnected: {}, reason: {}", e.Client.Guid, e.Reason);
            clients.TryRemove(e.Client.Guid, out AiClientConnection _);
        }

        private SyncResponse OnSyncRequestReceived(SyncRequest req) {
            _logger.LogWarning("Unexpected sync request from {}", req.Client.Guid);
            return new SyncResponse(req, "hi from server");
        }

        private async void HandleClientRequestsPause(AiClientRequestsPause msg) {
            _logger.LogDebug("ClientRequestsPause: {}", msg.Position);

            long pos = (msg.Position < 0) ? Position : msg.Position; // Int64.Clamp(msg.Position, 0, Int64.MaxValue);

            _logger.LogInformation("Pause requested at {}", AiSync.Utils.FormatTime(pos));

            lock (control_lock) {
                if (!Playing) {
                    /* Ignore if not playing */
                    return;
                }

                Playing = false;

                PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(Playing));
            }

            AiServerRequestsPause new_msg = new() { Position = pos };

            await Task.WhenAll(Clients.Select(guid => SendMessage(guid, new_msg)).ToArray());

            Position = pos;
        }

        private async void HandleClientRequestsPlay(AiClientRequestsPlay msg) {
            _logger.LogDebug("ClientRequestsPlay: {}", msg.Position);

            long pos = (msg.Position < 0) ? Position : msg.Position; // Int64.Clamp(msg.Position, 0, Int64.MaxValue);

            _logger.LogInformation("Play requested at {}", AiSync.Utils.FormatTime(pos));

            lock (control_lock) {
                if (Playing) {
                    /* Ignore if already playing */
                    return;
                }

                Playing = true;

                PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(Playing));
            }

            AiServerRequestsPlay new_msg = new() { Position = pos };

            await Task.WhenAll(Clients.Select(guid => SendMessage(guid, new_msg)).ToArray());

            Position = pos;
        }

        private async void HandleClientRequestsSeek(AiClientRequestSeek msg) {
            _logger.LogDebug("ClientRequestsSeek: {}", msg.Target);
            long target = (msg.Target < 0) ? Position : msg.Target; // Int64.Clamp(msg.Target, 0, Int64.MaxValue);

            _logger.LogInformation("Seek requested to {}", AiSync.Utils.FormatTime(msg.Target));

            lock (control_lock) {
                Position = target;
                seeking = true;
                //PositionChanged?.Invoke(this, new PositionChangedEvent(Position));
            }

            await Task.WhenAll(Clients.Select(guid => SendMessage(guid, new AiServerRequestSeek() { Target = Position })).ToArray());
        }

        private void HandleDefault(Guid client, AiProtocolMessage msg) {
            _logger.LogCritical("Unexpected message of type {} from {}", msg.Type, client);
            throw new InvalidMessageException(msg);
        }
    }
}
