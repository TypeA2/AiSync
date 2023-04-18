using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AiSync {
    public static partial class Extensions {
        /* Because WPF doesn't natively support this */
        private const int GWL_STYLE = -16;
        private const int WS_SYSMENU = 0x80000;

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static partial IntPtr GetWindowLongPtr(IntPtr hwnd, int nindex);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static partial IntPtr SetWindowLongPtr(IntPtr hwnd, int nindex, IntPtr newlong);

        public static void SetSysMenu(this Window w, bool new_val) {
            nint hwnd = new WindowInteropHelper(w).Handle;

            IntPtr window_long = GetWindowLongPtr(hwnd, GWL_STYLE);

            if (new_val) {
                window_long |= WS_SYSMENU;
            } else {
                window_long &= ~WS_SYSMENU;
            }

            _ = SetWindowLongPtr(hwnd, GWL_STYLE, window_long);
        }
    }
}
