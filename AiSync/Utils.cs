using System.Text;

namespace AiSync {
    public static class Utils {
        public static string FormatTime(long ms, bool show_ms = true, bool always_hours = false) {
            long seconds = ms / 1000;
            long minutes = seconds / 60;
            long hours = minutes / 60;

            StringBuilder sb = new();

            if (always_hours || hours > 0) {
                sb.AppendFormat("{0:d2}:", hours);
            }

            sb.AppendFormat("{0:d2}:{1:d2}", minutes % 60, seconds % 60);

            if (show_ms) {
                sb.AppendFormat(".{0:d3}", ms % 1000);
            }

            return sb.ToString();
        }

        private static readonly char[] hex_map = {
            '0', '1', '2', '3', '4', '5', '6', '7',
            '8', '9', 'a', 'b', 'c', 'd', 'e', 'f'
        };

        public static string HttpHeaderEncode(this string str) {
            StringBuilder sb = new();

            foreach (char ch in str) {
                /* Check if it's within ASCII range */
                if (ch > 127) {
                    /* Write this character as UTF8 bytes */
                    byte[] bytes = Encoding.UTF8.GetBytes($"{ch}");

                    foreach (byte b in bytes) {
                        sb.Append('%');

                        /* Write in 2 nybbles */
                        sb.Append(hex_map[(b >> 4) & 0xF]);
                        sb.Append(hex_map[b & 0xF]);
                    }
                } else {
                    /* Write raw ASCII */
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        public static string ReplaceNonAscii(this string str, char replacement = '?') {
            StringBuilder sb = new();

            foreach (char ch in str) {
                if (ch > 127) {
                    sb.Append(replacement);
                } else {
                    sb.Append(ch);
                }
            }

            return sb.ToString();
        }

        public static void WaitAndReset(this ManualResetEventSlim e) {
            e.Wait();
            e.Reset();
        }
    }
}
