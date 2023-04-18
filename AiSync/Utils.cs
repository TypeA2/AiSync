using System;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Windows.Documents.Serialization;

namespace AiSync {
    internal static partial class Utils {
        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        public static ImageSource ExtractIcon(string source, int idx) {
            IntPtr icon = ExtractIconW(Process.GetCurrentProcess().Handle, source, idx);

            return Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(128, 128));
        }

        public static string SHA512File(string path, Action<long> progress) {
            using SHA512 sha512 = SHA512.Create();
            using FileStream stream = File.OpenRead(path);

            byte[] buffer = new byte[4096];

            long total_read = 0;
            
            while (total_read < stream.Length) {
                int read = stream.Read(buffer);

                total_read += read;

                sha512.TransformBlock(buffer, 0, read, buffer, 0);

                progress(total_read);
            }

            sha512.TransformFinalBlock(buffer, 0, 0);
            progress(stream.Length);

            return BitConverter.ToString(sha512.Hash ?? new byte[sha512.HashSize / 8]).Replace("-", "").ToLowerInvariant();
        }

        public static async Task<string> SHA512FileAsync(string path, Action<long> progress) {
            return await Task.Run(() => SHA512File(path, progress));
        }

        /* Returns whether a file was selected */
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
    }
}
