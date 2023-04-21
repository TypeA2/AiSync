using Microsoft.Win32;

using System;
using System.IO;
using System.Threading.Tasks;
using System.IO.Hashing;
using System.Numerics;
using System.Windows.Controls;
using WatsonTcp;
using AiSync;

namespace AiSyncServer {
    internal static class Utils {
        public static bool GetFile(out string result, string filter = "All files (*.*)|*.*") {
            OpenFileDialog dialog = new() {
                Filter = filter,
            };

            if (dialog.ShowDialog() == true) {
                result = dialog.FileName;
                return true;
            } else {
                result = String.Empty;
                return false;
            }
        }

        public static string Crc32File(string path, Action<long> progress) {
            Crc32 crc = new();

            using FileStream stream = File.OpenRead(path);

            byte[] buffer = new byte[4096];

            long total_read = 0;

            while (total_read < stream.Length) {
                int read = stream.Read(buffer);

                total_read += read;

                crc.Append(buffer[..read]);

                progress(total_read);
            }

            progress(stream.Length);

            return BitConverter.ToString(crc.GetCurrentHash()).Replace("-", "").ToLowerInvariant();
        }

        public static async Task<string> Crc32FileAsync(string path, Action<long> progress) {
            return await Task.Run(() => Crc32File(path, progress));
        }

        public static T ParseText<T>(this TextBox element) where T : INumber<T> {
            return T.Parse(element.Text, null);
        }

        public static bool TryParseText<T>(this TextBox element, out T val) where T : notnull, INumber<T> {
#pragma warning disable CS8601 // Possible null reference assignment.
            return T.TryParse(element.Text, null, out val);
#pragma warning restore CS8601 // Possible null reference assignment.
        }
    }
}
