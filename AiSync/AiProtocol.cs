using Newtonsoft.Json;

using System.Text;

namespace AiSync {
    public static class AiProtocol {
        public static string Serialize<T>(this T obj) where T : AiProtocolMessage {
            return JsonConvert.SerializeObject(obj);
        }

        public static T AiDeserialize<T>(this string data) where T : AiProtocolMessage {
            T result = JsonConvert.DeserializeObject<T>(data) ?? throw new InvalidMessageException(typeof(T), data);
            result.SourceString = data;

            return result;
        }

        public static object AiDeserialize(this string data, Type type) {
            if (!type.IsAssignableTo(typeof(AiProtocolMessage))) {
                throw new InvalidOperationException($"{type.Name} does not derive from {typeof(AiProtocolMessage).Name}");
            }

            object result = JsonConvert.DeserializeObject(data, type) ?? throw new InvalidMessageException(type, data);
            ((AiProtocolMessage)result).SourceString = data;

            return result;
        }

        public static AiMessageType AiJsonMessageType(this string data) => AiDeserialize<AiProtocolMessage>(data).Type;

        public static string ToUtf8(this byte[] data) => Encoding.UTF8.GetString(data);
    }

    public class InvalidMessageException : Exception {
        public InvalidMessageException(Type type, string data)
            : base($"{type.Name}: {data}") { }

        public InvalidMessageException(AiProtocolMessage msg)
            : base($"Invalid message of type {msg.Type}: {msg.SourceString}") { }
    }

    /* Base message to derive from */
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class AiProtocolMessageAttribute : Attribute {
        public AiMessageType Type { get; set; }

        public AiProtocolMessageAttribute(AiMessageType type) {
            Type = type;
        }
    }

    public enum AiMessageType {
        None,

        FileReady,
        FileParsed,

        ServerReady,

        ClientRequestsPause,
        ServerRequestsPause,

        ClientRequestsPlay,
        ServerRequestsPlay,

        ClientRequestsSeek,
        ServerRequestsSeek,

        ServerRequestsStatus,

        ClientStatus,
        ServerStatus,
    }

    [AiProtocolMessage(AiMessageType.None)]
    public class AiProtocolMessage {
        [JsonIgnore]
        public string SourceString { get; set; } = "";

        public override string ToString() => SourceString;

        private AiProtocolMessageAttribute ProtocolMessageAttribute {
            get {
                object[] attrs = GetType().GetCustomAttributes(typeof(AiProtocolMessageAttribute), true);

                if (attrs == null || attrs.Length != 1) {
                    throw new InvalidMessageException(GetType(), SourceString);
                }

                return (AiProtocolMessageAttribute)attrs[0];
            }
        }

        private AiMessageType actual_type;

        public AiProtocolMessage() {
            object[] attrs = GetType().GetCustomAttributes(typeof(AiProtocolMessageAttribute), true);
            if (attrs == null || attrs.Length != 1) {
                throw new InvalidMessageException(GetType(), SourceString);
            }

            AiProtocolMessageAttribute attr = (AiProtocolMessageAttribute)attrs[0];

            actual_type = attr.Type;
        }

        [JsonProperty("type", Required = Required.Always)]
        public AiMessageType Type {
            get => actual_type;
            set {
                if (GetType() == typeof(AiProtocolMessage)) {
                    /* Actually set the type for AiProtocolMessage instances */
                    actual_type = value;
                } else if (actual_type != value) {
                    /* Check the type for anything else */
                    throw new InvalidMessageException(GetType(), SourceString);
                }
            }
        }
    }

    /* The playback file is ready on the data server */
    [AiProtocolMessage(AiMessageType.FileReady)]
    public class AiFileReady : AiProtocolMessage {
        public static AiFileReady FromCloseEnough(long val) {
            return new AiFileReady() { CloseEnoughValue = val };
        }

        [JsonProperty("close_enough_value", Required = Required.Always)]
        public long CloseEnoughValue { get; set; }
    }

    /* Client has parsed the file and is awaiting the start of playback */
    [AiProtocolMessage(AiMessageType.FileParsed)]
    public class AiFileParsed : AiProtocolMessage { }

    /* Server is ready, clients can begin control */
    [AiProtocolMessage(AiMessageType.ServerReady)]
    public class AiServerReady : AiProtocolMessage { }

    /* Base class for messages with a position attachment */
    public class AiPositionMessage : AiProtocolMessage {
        public static T FromPosition<T>(long pos) where T : AiPositionMessage, new() {
            return new T() { Position = pos };
        }

        [JsonProperty("position", Required = Required.Always)]
        public long Position { get; set; }
    }

    /* A client requests the shared playback to stop */
    [AiProtocolMessage(AiMessageType.ClientRequestsPause)]
    public class AiClientRequestsPause : AiPositionMessage { }

    /* Server requests the receiving client to pause playback */
    [AiProtocolMessage(AiMessageType.ServerRequestsPause)]
    public class AiServerRequestsPause : AiPositionMessage { }

    /* Ditto but for resuming playback */
    [AiProtocolMessage(AiMessageType.ClientRequestsPlay)]
    public class AiClientRequestsPlay : AiPositionMessage { }

    [AiProtocolMessage(AiMessageType.ServerRequestsPlay)]
    public class AiServerRequestsPlay : AiPositionMessage { }

    /* Base class used for seek messages */
    public class AiSeekRequest : AiProtocolMessage {
        [JsonProperty("target", Required = Required.Always)]
        public long Target { get; set; }
    }

    [AiProtocolMessage(AiMessageType.ClientRequestsSeek)]
    public class AiClientRequestSeek : AiSeekRequest { }

    [AiProtocolMessage(AiMessageType.ServerRequestsSeek)]
    public class AiServerRequestSeek : AiSeekRequest { }

    [AiProtocolMessage(AiMessageType.ServerRequestsStatus)]
    public class AiServerRequestsStatus : AiProtocolMessage { }

    /* Base class for status messages */
    public class AiStatusMessage : AiProtocolMessage {
        [JsonProperty("playing", Required = Required.Always)]
        public bool IsPlaying { get; set; }

        [JsonProperty("position", Required = Required.Always)]
        public long Position { get; set; }
    }

    [AiProtocolMessage(AiMessageType.ClientStatus)]
    public class AiClientStatus : AiStatusMessage { }

    [AiProtocolMessage(AiMessageType.ServerStatus)]
    public class AiServerStatus : AiStatusMessage { }
}
