using System;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Runtime.InteropServices;

namespace ai_sync {
    internal static partial class Utils {
        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr ExtractIconW(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        public static ImageSource ExtractIcon(string source, int idx) {
            IntPtr icon = ExtractIconW(Process.GetCurrentProcess().Handle, source, idx);

            return Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(128, 128));
        }
    }
}
