using AiSync;

using Microsoft.Extensions.Logging;

using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using WatsonTcp;

namespace AiSyncClient {
    internal class SeekEventArgs : EventArgs {
        public SeekEventArgs(long target) : base() {
            Target = target;
        }

        public long Target { get; }
    }

    internal class PausePlayEventArgs : EventArgs {
        
        public PausePlayEventArgs(long pos, bool playing) : base() {
            Position = pos;
            IsPlaying = playing;
        }

        public long Position { get; }

        public bool IsPlaying { get; }
    }

    internal class AiClient : IDisposable {
        public event EventHandler? GotFile;
        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler? CloseFile;

        public event EventHandler<PausePlayEventArgs>? PausePlay;

        public event EventHandler<SeekEventArgs>? Seek;

        public event EventHandler? UpdateStatus;

        public long CloseEnoughValue { get; private set; }

        public bool IsConnected => client.Connected;

        private readonly ILogger _logger;

        private readonly WatsonTcpClient client;

        private bool playing = false;
        private long pos_ms = 0;
        private DateTime ts;

        public AiClient(ILoggerFactory fact, IPAddress addr, ushort port) : this(fact, new IPEndPoint(addr, port)) { }

        public AiClient(ILoggerFactory fact, IPEndPoint endpoint) {
            _logger = fact.CreateLogger("AiClient");

            client = new WatsonTcpClient(endpoint.Address.ToString(), endpoint.Port);
            client.Settings.Logger = (level, msg) => {
                _logger.Log(level switch {
                    Severity.Debug => LogLevel.Debug,
                    Severity.Info => LogLevel.Information,
                    Severity.Warn => LogLevel.Warning,
                    Severity.Error => LogLevel.Error,
                    Severity.Alert => LogLevel.Warning,
                    Severity.Critical => LogLevel.Critical,
                    Severity.Emergency => LogLevel.Critical,
                    _ => LogLevel.Warning
                }, "{}", msg);
            };

            client.Events.ServerConnected += ServerConnected;
            client.Events.ServerDisconnected += ServerDisconnected;
            client.Events.MessageReceived += MessageReceived;
            client.Events.ExceptionEncountered += ExceptionEncountered;
            client.Callbacks.SyncRequestReceived = SyncRequestReceived;
        }

        public async void Dispose() {
            if (client.Connected) {
                client.Disconnect();
            } else {
                /* Not cleaned up in this case */
                foreach (Task task in client.GetPrivateTasks()) {
                    try {
                        await task;
                    } catch (TaskCanceledException) { }
                }
            }

            client.Dispose();
            GC.SuppressFinalize(this);
        }

        public Task<bool> Connect() {
            _logger.LogInformation("Connecting to server");

            return Task.Run(() => {
                try {
                    client.Connect();
                } catch (SocketException) {
                    /* pass */
                    _logger.LogWarning("Timed out when connecting to server");
                }
                return client.Connected;
            });
        }

        public void FileParsed() {
            _logger.LogInformation("Input media done parsing");
        }

        public void SetStatus(bool playing, long pos_ms) {
            _logger.LogDebug("Status request, playing={}, pos={}", playing, pos_ms);
            this.playing = playing;
            this.pos_ms = pos_ms;
            ts = DateTime.Now;
        }

        public async void RequestPause(long pos) {
            _logger.LogInformation("Sending pause request at {}", AiSync.Utils.FormatTime(pos));
            await SendMessage(new AiClientRequestsPause() { Position = pos });
        }

        public async void RequestPlay(long pos) {
            _logger.LogInformation("Sending play request at {}", AiSync.Utils.FormatTime(pos));
            await SendMessage(new AiClientRequestsPlay() { Position = pos });
        }

        public async void RequestSeek(long target) {
            _logger.LogInformation("Sending seek request to {}", AiSync.Utils.FormatTime(target));
            await SendMessage(new AiClientRequestSeek() { Target = target });
        }

        public async void PauseResync() {
            _logger.LogInformation("Resync-pausing");
            await SendMessage<AiPauseResync>();
        }

        private Task SendMessage<T>(T? msg = null) where T : AiProtocolMessage, new() {
            return client.SendAsync((msg ?? new T()).AiSerialize());
        }

        private void ServerConnected(object? sender, ConnectionEventArgs e) {
            _logger.LogInformation("Connected");
        }
        private void ServerDisconnected(object? sender, DisconnectionEventArgs e) {
            _logger.LogInformation("Disconnected, reason: {}", e.Reason);
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        private Resp SendAndExpectMsg<Msg, Resp>(int ms, Msg src)
            where Msg : AiProtocolMessage
            where Resp : AiProtocolMessage {
            SyncResponse response = client.SendAndWait(ms, src.AiSerialize());

            return response.Data.ToUtf8().AiDeserialize<Resp>();
        }

        public AiServerStatus? GetStatus() {
            try {
                return SendAndExpectMsg<AiGetStatus, AiServerStatus>(1000, new AiGetStatus());
            } catch (TimeoutException) {

            } catch (InvalidMessageException) {

            }

            return null;
        }

        private void MessageReceived(object? sender, MessageReceivedEventArgs e) {
            /* Ignore */
            _logger.LogInformation("Message received from {}: {}", e.Client, e.Data.ToUtf8());
        }

        private void ExceptionEncountered(object? sender, ExceptionEventArgs e) {
            _logger.LogCritical("Exception encountered: {}", e.Exception);
        }

        private SyncResponse SyncRequestReceived(SyncRequest req) {
            string str = req.Data.ToUtf8();

            Delegate handler = str.AiJsonMessageType() switch {
                AiMessageType.FileReady => HandleFileReady,
                AiMessageType.ServerReady => HandleServerReady,
                AiMessageType.ServerRequestsPlay => HandleServerRequestsPlay,
                AiMessageType.ServerRequestsPause => HandleServerRequestsPause,
                AiMessageType.ServerRequestsSeek => HandleServerRequestsSeek,
                AiMessageType.FileClosed => HandleFileClosed,
                _ => HandleDefault,
            };

            MethodInfo handler_method = handler.GetMethodInfo();
            ParameterInfo[] handler_params = handler_method.GetParameters();

            /* Should never happen */
            if (!handler_method.ReturnType.IsAssignableTo(typeof(AiProtocolMessage))
                || handler_params.Length != 1
                || !handler_params[0].ParameterType.IsAssignableTo(typeof(AiProtocolMessage))) {
                throw new ArgumentException("invalid delegate");
            }

            object? response = handler.DynamicInvoke(str.AiDeserialize(handler_params[0].ParameterType));

            if (response == null) {
                /* How? */
                return req.ReplyWith(new AiProtocolMessage());
            } else {
                _logger.LogDebug("Replying with {}", response.GetType().Name);
                return req.ReplyWith(response, handler_method.ReturnType);
            }
            
            /*
            string str = req.Data.ToUtf8();
            AiMessageType type = str.AiJsonMessageType();
            
            switch (type) {

                case AiMessageType.ServerRequestsStatus: {
                    AiClientStatus status = new();

                    UpdateStatus?.Invoke(this, EventArgs.Empty);
                    sync_wait.WaitAndReset();

                    status.State = playing ? PlayingState.Playing : PlayingState.Paused;
                    status.Position = pos_ms;
                    status.Timestamp = ts;

                    return req.ReplyWith(status);
                }

                default:
                    throw new InvalidMessageException(str.AiDeserialize<AiProtocolMessage>());
            }*/
        }

        private AiFileParsed HandleFileReady(AiFileReady msg) {
            _logger.LogInformation("File ready, CloseEnoughValue: {}", msg.CloseEnoughValue);

            CloseEnoughValue = msg.CloseEnoughValue;
            
            /* Parse synchronously */
            GotFile?.Invoke(this, EventArgs.Empty);

            _logger.LogInformation("Parsing finished");

            return new();
        }

        private AiClientReady HandleServerReady(AiServerReady msg) {
            _logger.LogInformation("Server ready");
            Connected?.Invoke(this, EventArgs.Empty);
            return new();
        }

        private AiClientReady HandleServerRequestsPause(AiServerRequestsPause msg) {
            _logger.LogInformation("Server requests pause at {}", AiSync.Utils.FormatTime(msg.Position));
            PausePlay?.Invoke(this, new PausePlayEventArgs(msg.Position, false));
            return new();
        }

        private AiClientReady HandleServerRequestsPlay(AiServerRequestsPlay msg) {
            _logger.LogInformation("Server requests play at {}", AiSync.Utils.FormatTime(msg.Position));
            PausePlay?.Invoke(this, new PausePlayEventArgs(msg.Position, true));
            return new();
        }

        private AiClientReady HandleServerRequestsSeek(AiServerRequestSeek msg) {
            _logger.LogInformation("Server requests seek to {}", AiSync.Utils.FormatTime(msg.Target));
            Seek?.Invoke(this, new SeekEventArgs(msg.Target));
            return new();
        }

        private AiClientReady HandleFileClosed(AiFileClosed msg) {
            _logger.LogInformation("Closing file");
            CloseFile?.Invoke(this, EventArgs.Empty);
            return new();
        }

        private AiProtocolMessage HandleDefault(AiProtocolMessage msg) {
            _logger.LogCritical("Unexpected message of type {} from {}", msg.Type, client);
            return new();
        }
    }
}
