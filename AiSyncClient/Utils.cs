using AiSync;

using LibVLCSharp.Shared;

using System;
using System.Numerics;
using System.Windows.Controls;

using WatsonTcp;

namespace AiSyncClient {
    internal static class Utils {
        public static T ParseText<T>(this TextBox element) where T : INumber<T> {
            return T.Parse(element.Text, null);
        }

        public static bool TryParseText<T>(this TextBox element, out T val) where T : notnull, INumber<T> {
#pragma warning disable CS8601 // Possible null reference assignment.
            return T.TryParse(element.Text, null, out val);
#pragma warning restore CS8601 // Possible null reference assignment.
        }

        public static SyncResponse ReplyWith<T>(this SyncRequest req) where T : AiProtocolMessage, new() {
            return new SyncResponse(req, new T().AiSerialize());
        }

        public static SyncResponse ReplyWith<T>(this SyncRequest req, T msg) where T : AiProtocolMessage {
            return new SyncResponse(req, msg.AiSerialize());
        }

        public static long PositionMs(this MediaPlayer player) {
            if (player.Media is null) {
                throw new NullReferenceException("Player Media is null");
            }

            return (long)Math.Round(player.Position * player.Media.Duration);
        }
    }
}
