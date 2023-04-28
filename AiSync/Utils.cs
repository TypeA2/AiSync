using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AiSync {
    public static class Utils {
        public static string FormatTime(long ms, bool show_ms = true, bool always_hours = false, int ms_prec = 3) {
            ms_prec = Int32.Clamp(ms_prec, 1, 3);

            StringBuilder sb = new();

            if (ms <= 0) {
                if (always_hours) {
                    sb.Append("00:");
                }

                sb.Append("00:00");

                if (show_ms) {
                    sb.Append(".000");
                }

                return sb.ToString();
            }

            long seconds = ms / 1000;
            long minutes = seconds / 60;
            long hours = minutes / 60;

            if (always_hours || hours > 0) {
                sb.AppendFormat("{0:d2}:", hours);
            }

            sb.AppendFormat("{0:d2}:{1:d2}", minutes % 60, seconds % 60);

            if (show_ms) {
                switch (ms_prec) {
                    case 3:
                        sb.AppendFormat(".{0:d3}", ms % 1000);
                        break;

                    case 2:
                        sb.AppendFormat(".{0:d2}", (int)Math.Round((ms % 1000) / 10.0));
                        break;

                    case 1:
                        sb.AppendFormat(".{0:d1}", (int)Math.Round((ms % 1000) / 100.0));
                        break;
                }
                
            }

            return sb.ToString();
        }

        public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> src) where T : class {
            foreach (T? val in src) {
                if (val is not null) {
                    yield return val;
                }
            }
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

        public static T Difference<T>(this T val, T comp) where T : INumber<T> {
            T diff = val - comp;

            return T.IsNegative(diff) ? -diff : diff;
        }

        public static IList<Task> GetPrivateTasks(this object src) {
            FieldInfo[] fields = src.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic);

            return fields
                    .Where(f => f.FieldType == typeof(Task))
                    .Select(f => f.GetValue(src) as Task)
                    .WhereNotNull()
                    .ToList();
        }
    }
}
