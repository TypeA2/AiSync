using Newtonsoft.Json;

using System;

namespace AiSync {
    public abstract class AiWsBase : IDisposable {
        public abstract void Dispose();

        public static string Serialize<T>(T obj) where T : AiProtocolMessage {
            return JsonConvert.SerializeObject(obj);
        }

        public static T Deserialize<T>(string data) where T : AiProtocolMessage {
            T result = JsonConvert.DeserializeObject<T>(data) ?? throw new InvalidMessageException(typeof(T), data);
            result.SourceString = data;

            return result;
        }

        public static object Deserialize(string data, Type type) {
            if (!type.IsAssignableTo(typeof(AiProtocolMessage))) {
                throw new InvalidOperationException($"{type.Name} does not derive from {typeof(AiProtocolMessage).Name}");
            }

            object result = JsonConvert.DeserializeObject(data, type) ?? throw new InvalidMessageException(type, data);
            ((AiProtocolMessage)result).SourceString = data;

            return result;
        }

        public static AiMessageType MessageType(string data) => Deserialize<AiProtocolMessage>(data).Type;
    }
}
