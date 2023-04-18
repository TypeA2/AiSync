using Newtonsoft.Json;

using System.Diagnostics;
using System.Net;
using System.Collections.Generic;

using WebSocketSharp;
using WebSocketSharp.Server;
using System;
using System.Threading.Tasks;
using System.Threading;

namespace AiSync {
    public class AiWsPlayer : WebSocketBehavior, IDisposable {
        public AiServer? Server { get; set; }

        public event EventHandler<CloseEventArgs>? Closed;

        private readonly Mutex wait_mutex = new(false);
        private readonly ManualResetEvent wait_event = new(false);
        private AiMessageType? wait_for;

        private void PrepareWait(AiMessageType msg) {
            wait_mutex.WaitOne();
            wait_event.Reset();
            wait_for = msg;
        }

        private void WaitForCurrent() {
            wait_event.WaitOne();
            wait_event.Reset();
            wait_for = null;
        }

        private void FinishWait(AiMessageType msg) {
            if (wait_for == msg) {
                wait_event.Set();
            }
        }

        public Task SendMessage<T>(T msg) where T : AiProtocolMessage {
            return Task.Run(() => Send(JsonConvert.SerializeObject(msg)));
        }

        /* Send the NewFileSelected message and wait for a response */
        public async Task SetNewFile(AiNewFileSelected msg) {
            PrepareWait(AiMessageType.NewFileAccepted);
            await SendMessage(msg);
            WaitForCurrent();
        }

        protected override void OnOpen() {
            
        }

        protected override void OnMessage(MessageEventArgs e) {
            /* Only accept text connections */
            if (!e.IsText) {
                return;
            }

            AiMessageType type;

            try {
                type = AiWsBase.MessageType(e.Data);
            } catch (InvalidMessageException exception) {
                Trace.WriteLine(exception);
                return;
            }

            /* Dispatch semi-dynamically */
            (Delegate handler, Type message_type) = type switch {
                AiMessageType.NewFileAccepted    => (HandleNewFileAccepted, typeof(AiNewFileAccepted)),
                _ => ((Delegate)HandleDefault, typeof(AiProtocolMessage))
            };

            handler.DynamicInvoke(AiWsBase.Deserialize(e.Data, message_type));
        }

        protected override void OnClose(CloseEventArgs e) {
            /* Stop any waits when the connection is closed */
            if (wait_for != null) {
                WaitForCurrent();
            }

            Closed?.Invoke(this, e);
        }

        private void HandleNewFileAccepted(AiNewFileAccepted msg) {
            /* Client accepts or rejects the new file.
             * In case of rejection, client handles disconnect.
             */
            FinishWait(AiMessageType.NewFileAccepted);
        }

        private void HandleDefault(AiProtocolMessage msg) {
            throw new InvalidMessageException(msg);
        }

        public void Dispose() {
            wait_mutex.Dispose();
            wait_event.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public sealed class AiServer : AiWsBase {
        public event EventHandler? SessionOpened;
        public event EventHandler? SessionClosed;

        private readonly WebSocketServer server;

        private readonly List<AiWsPlayer> clients = new();

        private readonly Player player;

        private void NewSession(AiWsPlayer player) {
            player.Server = this;

            clients.Add(player);

            SessionOpened?.Invoke(this, EventArgs.Empty);

            player.Closed += (sender, _) => {
                if (sender != null) {
                    clients.Remove((AiWsPlayer)sender);
                }

                SessionClosed?.Invoke(this, EventArgs.Empty);
            };
        }

        public int ClientCount { get => clients.Count; }

        public async Task SetFile(string filename, string hash, Action progress) {
            /* Send all clients the same NewFileSelected message */
            AiNewFileSelected msg = new() {
                Name = filename,
                Hash = hash
            };

            Task[] tasks = new Task[clients.Count];

            for (int i = 0; i < clients.Count; ++i) {
                AiWsPlayer client = clients[i];
                tasks[i] = Task.Run(async () => {
                    await client.SetNewFile(msg);
                });
            }

            /* Wait for all clients to accepts */
            foreach (Task task in tasks) {
                await task;
                progress();
            }
        }

        public async void AllClientsReady() {

            AiAllClientsReady msg = new();

            foreach (AiWsPlayer client in clients) {
                await client.SendMessage(msg);
            }
        }

        public AiServer(IPAddress addr, ushort ws_port, string vlc_path, ushort vlc_port) {
            player = new VlcPlayer(vlc_path, vlc_port);

            server = new WebSocketServer(addr, ws_port);
            server.AddWebSocketService<AiWsPlayer>("/player", NewSession);
            server.Start();
        }

        public override void Dispose() {
            player.Dispose();

            foreach (AiWsPlayer client in clients) {
                client.Dispose();
            }
        }

        public void Stop() {
            server.Stop();
        }
    }
}
