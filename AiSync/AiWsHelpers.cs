using Newtonsoft.Json;

using System.Threading.Tasks;

using WebSocketSharp;

namespace AiSync {
    public static class AiWsHelpers {
        public static Task SendMessage<T>(this WebSocket ws, T msg) where T : AiProtocolMessage {
            return Task.Run(() => ws.Send(JsonConvert.SerializeObject(msg)));
        }
    }
}
