using Microsoft.Win32;

using System;
using System.IO;
using System.Threading.Tasks;
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
