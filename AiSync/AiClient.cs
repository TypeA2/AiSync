using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using WebSocketSharp;

namespace AiSync {
    public sealed class AiClient : AiWsBase {
        public event EventHandler? ConnectionClosed;

        public event EventHandler<AiNewFileSelected>? NewFileSelected;

        private readonly WebSocket conn;

        private readonly Player player;

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

        public AiClient(IPAddress addr, ushort ws_port, string vlc_path, ushort vlc_port) {
            player = new VlcPlayer(vlc_path, vlc_port);

            conn = new WebSocket($"ws://{addr}:{ws_port}/player");

            conn.OnMessage += OnMessage;
            conn.OnClose += OnClosed;

            conn.OnMessage += (_, _) => Trace.WriteLine("Message");
            conn.OnOpen += (_, _) => Trace.WriteLine("Open");
            conn.OnClose += (_, _) => Trace.WriteLine("Close");
            conn.OnError += (_, _) => Trace.WriteLine("Error");

            Task.Run(conn.Connect);
        }

        public override void Dispose() {
            player.Dispose();
            conn.Close(CloseStatusCode.Away);

            wait_mutex.Dispose();
            wait_event.Dispose();
        }

        public void Close() {
            conn.Close(CloseStatusCode.Normal);
        }

        public async Task NewFileAccepted() {
            PrepareWait(AiMessageType.AllClientsReady);
            await conn.SendMessage(new AiNewFileAccepted() {
                Accepted = true,
            });
            WaitForCurrent();
        }

        public Task NewFileRejected() {
            return conn.SendMessage(new AiNewFileAccepted() {
                Accepted = false,
            });
        }

        private void OnClosed(object? sender, CloseEventArgs e) {
            ConnectionClosed?.Invoke(this, e);
        }

        private void OnMessage(object? sender, MessageEventArgs e) {
            /* Ignore text */
            if (!e.IsText) {
                return;
            }

            AiMessageType type;

            try {
                type = MessageType(e.Data);
            } catch (InvalidMessageException exception) {
                Trace.WriteLine(exception);
                return;
            }

            (Delegate handler, Type message_type) = type switch {
                AiMessageType.NewFileSelected => (HandleNewFileSelected, typeof(AiNewFileSelected)),
                AiMessageType.AllClientsReady => (HandleAllClientsReady, typeof(AiAllClientsReady)),
                _ => ((Delegate)HandleDefault, typeof(AiProtocolMessage)),
            };

            handler.DynamicInvoke(Deserialize(e.Data, message_type));
        }

        private void HandleNewFileSelected(AiNewFileSelected msg) {
            NewFileSelected?.Invoke(this, msg);
        }

        private void HandleAllClientsReady(AiAllClientsReady msg) {
            FinishWait(AiMessageType.AllClientsReady);
        }

        private void HandleDefault(AiProtocolMessage msg) {
            throw new InvalidMessageException(msg);
        }

    }
}
