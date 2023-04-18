using Newtonsoft.Json;

using System;
using System.Diagnostics;

namespace AiSync {
    public class InvalidMessageException : Exception {
        public InvalidMessageException(Type type, string data)
            : base($"{type.Name}: {data}") { }

        public InvalidMessageException(AiProtocolMessage msg)
            : base($"Invalid message of type {msg.Type}: {msg.SourceString}") { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class AiProtocolMessageAttribute : Attribute {
        public AiMessageType Type { get; set; }

        public AiProtocolMessageAttribute(AiMessageType type) {
            Type = type;
        }
    }

    public enum AiMessageType {
        None,
        NewFileSelected,
        NewFileAccepted,

        AllClientsReady,
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

    /* Cannot be directly used, must be derived */
    public abstract class AiActionAccepted : AiProtocolMessage {
        [JsonProperty("accepted", Required = Required.Always)]
        public bool Accepted { get; set; }
    }

    [AiProtocolMessage(AiMessageType.NewFileSelected)]
    public class AiNewFileSelected : AiProtocolMessage {
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; } = String.Empty;

        [JsonProperty("hash", Required = Required.Always)]
        public string Hash { get; set; } = String.Empty;
    }

    [AiProtocolMessage(AiMessageType.NewFileAccepted)]
    public class AiNewFileAccepted : AiActionAccepted { }

    [AiProtocolMessage(AiMessageType.AllClientsReady)]
    public class AiAllClientsReady : AiProtocolMessage { }
}
