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
        public event EventHandler? CloseFile;

        public event EventHandler<PausePlayEventArgs>? PausePlay;

        public event EventHandler<SeekEventArgs>? Seek;

        public event EventHandler? UpdateStatus;

        public long CloseEnoughValue { get; private set; }

        private readonly ILogger _logger;

        private readonly WatsonTcpClient client;

        private readonly ManualResetEventSlim sync_wait = new(false);

        private bool playing = false;
        private long pos_ms = 0;
        private DateTime ts;

        public AiClient(ILoggerFactory fact, IPAddress addr, ushort port) : this(fact, new IPEndPoint(addr, port)) { }

        public AiClient(ILoggerFactory fact, IPEndPoint endpoint) {
            _logger = fact.CreateLogger("AiClient");

            client = new WatsonTcpClient(endpoint.Address.ToString(), endpoint.Port);
            client.Events.ServerConnected += ServerConnected;
            client.Events.ServerDisconnected += ServerDisconnected;
            client.Events.MessageReceived += MessageReceived;
            client.Events.ExceptionEncountered += ExceptionEncountered;
            client.Callbacks.SyncRequestReceived = SyncRequestReceived;
        }

        public void Dispose() {
            client.Dispose();
            sync_wait.Dispose();
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

        public void Disconnect() {
            _logger.LogInformation("Disconnecting");
            client.Disconnect();
        }

        public void FileParsed() {
            _logger.LogInformation("Input media done parsing");
            sync_wait.Set();
        }

        public void SetStatus(bool playing, long pos_ms) {
            _logger.LogDebug("Status request, playing={}, pos={}", playing, pos_ms);
            this.playing = playing;
            this.pos_ms = pos_ms;
            ts = DateTime.Now;

            sync_wait.Set();
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
            return client.SendAsync((msg ?? new T()).Serialize());
        }

        private void ServerConnected(object? sender, ConnectionEventArgs e) {
            _logger.LogInformation("Connected");
        }
        private void ServerDisconnected(object? sender, DisconnectionEventArgs e) {
            _logger.LogInformation("Disconnected, reason: ", e.Reason);
        }

        private Resp SendAndExpectMsg<Msg, Resp>(int ms, Msg src)
            where Msg : AiProtocolMessage
            where Resp : AiProtocolMessage {
            SyncResponse response = client.SendAndWait(5 * 1000, src.Serialize());

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
            string str = e.Data.ToUtf8();
            AiMessageType type = str.AiJsonMessageType();

            Delegate handler = str.AiJsonMessageType() switch {
                AiMessageType.ServerReady => HandleServerReady,
                AiMessageType.FileClosed => HandleFileClosed,
                AiMessageType.ServerRequestsPause => HandleServerRequestsPause,
                AiMessageType.ServerRequestsPlay => HandleServerRequestsPlay,
                AiMessageType.ServerRequestsSeek => HandleServerRequestsSeek,
                _ => HandleDefault,
            };

            ParameterInfo[] handler_params = handler.GetMethodInfo().GetParameters();

            /* Should never happen */
            if (handler_params.Length != 1 || !handler_params[0].ParameterType.IsAssignableTo(typeof(AiProtocolMessage))) {
                throw new ArgumentException("invalid delegate");
            }

            handler.DynamicInvoke(str.AiDeserialize(handler_params[0].ParameterType));
        }

        private SyncResponse SyncRequestReceived(SyncRequest req) {
            string str = req.Data.ToUtf8();
            AiMessageType type = str.AiJsonMessageType();
            
            switch (type) {
                case AiMessageType.FileReady: {
                    /* Parse file and reply with AiFileParsed */
                    AiFileReady msg = str.AiDeserialize<AiFileReady>();

                    _logger.LogInformation("Server file ready, parsing");

                    CloseEnoughValue = msg.CloseEnoughValue;

                    GotFile?.Invoke(this, EventArgs.Empty);
                    sync_wait.WaitAndReset();

                    _logger.LogInformation("Parsing finished");

                    return req.ReplyWith<AiFileParsed>();
                }

                case AiMessageType.ServerRequestsStatus: {
                    AiClientStatus status = new();

                    UpdateStatus?.Invoke(this, EventArgs.Empty);
                    sync_wait.WaitAndReset();

                    status.IsPlaying = playing;
                    status.Position = pos_ms;
                    status.Timestamp = ts;

                    return req.ReplyWith(status);
                }

                default:
                    throw new InvalidMessageException(str.AiDeserialize<AiProtocolMessage>());
            }
        }

        private void ExceptionEncountered(object? sender, ExceptionEventArgs e) {
            _logger.LogCritical("Exception encountered: {}", e.Exception);
        }

        private void HandleServerReady(AiServerReady msg) {
            _logger.LogInformation("Server is ready");
            Connected?.Invoke(this, EventArgs.Empty);
        }

        private void HandleFileClosed(AiFileClosed msg) {
            _logger.LogInformation("Closing file");
            CloseFile?.Invoke(this, EventArgs.Empty);
        }

        private void HandleServerRequestsPause(AiServerRequestsPause msg) {
            _logger.LogInformation("Server requests pause at {}", AiSync.Utils.FormatTime(msg.Position));
            PausePlay?.Invoke(this, new PausePlayEventArgs(msg.Position, false));
        }

        private void HandleServerRequestsPlay(AiServerRequestsPlay msg) {
            _logger.LogInformation("Server requests play at {}", AiSync.Utils.FormatTime(msg.Position));
            PausePlay?.Invoke(this, new PausePlayEventArgs(msg.Position, true));
        }

        private void HandleServerRequestsSeek(AiServerRequestSeek msg) {
            _logger.LogInformation("Server requests seek to {}", AiSync.Utils.FormatTime(msg.Target));
            Seek?.Invoke(this, new SeekEventArgs(msg.Target));
        }

        private void HandleDefault(AiProtocolMessage msg) {
            _logger.LogCritical("Unexpected message of type {} from {}", msg.Type, client);
            throw new InvalidMessageException(msg);
        }
    }
}
