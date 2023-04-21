using AiSync;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

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

        private readonly WatsonTcpServer server;

        public AiServer(IPAddress addr, ushort port) : this(new IPEndPoint(addr, port)) { }

        private readonly ConcurrentDictionary<Guid, AiClientConnection> clients = new();

        private bool has_file = false;

        private readonly object control_lock = new();

        public bool Playing { get; private set; } = false;
        public long Position { get; private set; } = 0;

        public IEnumerable<Guid> Clients { get => clients.Keys; }
        public int ClientCount { get => clients.Count; }

        public AiServer(IPEndPoint endpoint) {
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
        }

        public void Start() {
            server.Start();
        }

        public async void SetHasFile() {
            if (has_file) {
                throw new InvalidOperationException("SetHasFile has already been called");
            }

            has_file = true;

            Task<(Guid, bool)>[] tasks = Clients
                .Select(SendAndWaitNewFileAsync)
                .ToArray();

            await Task.WhenAll(tasks);
                    
            /* Remove all failed clients */
            foreach ((Guid guid, bool _) in tasks.Select(t => t.Result).Where(result => !result.Item2)) {
                server.DisconnectClient(guid);
            }

            Trace.WriteLine("All clients parsed");

            /* Tell all clients the server is ready */
            await Task.WhenAll(
                Clients
                    .Select(guid => SendMessage<AiServerReady>(guid))
                    .ToArray()
            );
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
                SyncResponse resp = server.SendAndWait(60 * 1000, guid, new AiFileReady().Serialize());

                SendAndExpectMsg<AiFileReady, AiFileParsed>(60 * 1000, guid, AiFileReady.FromCloseEnough(750));

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
            clients.TryAdd(e.Client.Guid, new AiClientConnection() { State = AiConnectionState.Connected });

            ClientConnected?.Invoke(this, EventArgs.Empty);

            if (has_file) {
                (Guid _, bool success) = await SendAndWaitNewFileAsync(e.Client.Guid);
                if (success) {
                    await SendMessage<AiServerReady>(e.Client.Guid);
                } else if (success) {
                    server.DisconnectClient(e.Client.Guid);
                    return;

                }
            }

            Trace.WriteLine($"Connected: {e.Client.Guid}");
        }
        private void OnClientDisconnected(object? sender, DisconnectionEventArgs e) {
            clients.TryRemove(e.Client.Guid, out AiClientConnection _);

            Trace.WriteLine($"Disconnected: {e.Client.Guid}");
        }

        private SyncResponse OnSyncRequestReceived(SyncRequest req) {
            Trace.WriteLine($"Got sync request: {Encoding.UTF8.GetString(req.Data)}");
            return new SyncResponse(req, "hi from server");
        }

        private async void HandleClientRequestsPause(AiClientRequestsPause msg) {
            lock (control_lock) {
                if (!Playing) {
                    /* Ignore if not playing */
                    return;
                }

                Playing = false;

                PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(Playing));
            }

            AiServerRequestsPause new_msg = new() { Position = msg.Position };

            await Task.WhenAll(Clients.Select(guid => SendMessage(guid, new_msg)).ToArray());
        }

        private async void HandleClientRequestsPlay(AiClientRequestsPlay msg) {
            lock (control_lock) {
                if (Playing) {
                    /* Ignore if already playing */
                    return;
                }

                Playing = true;

                PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(Playing));
            }

            AiServerRequestsPlay new_msg = new() { Position = msg.Position };

            await Task.WhenAll(Clients.Select(guid => SendMessage(guid, new_msg)).ToArray());
        }

        private async void HandleClientRequestsSeek(AiClientRequestSeek msg) {
            lock (control_lock) {
                if (msg.Target == Position) {
                    /* You never know? */
                    return;
                }

                Position = msg.Target;

                PositionChanged?.Invoke(this, new PositionChangedEvent(Position));
            }

            await Task.WhenAll(Clients.Select(guid => SendMessage(guid, new AiServerRequestSeek() { Target = Position })).ToArray());
        }

        private void HandleDefault(Guid client, AiProtocolMessage msg) {
            throw new InvalidMessageException(msg);
        }
    }
}
