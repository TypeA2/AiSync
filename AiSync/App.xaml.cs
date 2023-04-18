using Microsoft.Win32;

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AiSync {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private static bool GetVlcPath() {
            OpenFileDialog dialog = new() {
                Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            };

            if (!String.IsNullOrEmpty(Settings.Default.VlcPath)) {
                dialog.InitialDirectory = Path.GetDirectoryName(Settings.Default.VlcPath);
            }

            if (dialog.ShowDialog() == true) {
                Settings.Default.VlcPath = dialog.FileName;
                Settings.Default.Save();
                return true;
            }

            return false;
        }

        private void StartApp(object sender, StartupEventArgs e) {
            /* Get VLC path if not already set or moved */
            if (String.IsNullOrEmpty(Settings.Default.VlcPath) || !File.Exists(Settings.Default.VlcPath)) {
                if (!GetVlcPath()) {
                    return;
                }
            }

            /* Retrieve whether we're client or server */
            StartupWindow startup = new(); ;

            startup.Closing += (_, _) => {
                Trace.WriteLine($"Running as {startup.RunAs}");

                switch (startup.RunAs) {
                    case StartupWindow.RunType.None:
                        break;

                    case StartupWindow.RunType.Client: {
                        AiClient ai = new(startup.IP, startup.Port, Settings.Default.VlcPath, startup.VlcPort);
                        ClientWindow window = new(ai);

                        window.Closed += (_, _) => {
                            ai.Dispose();
                        };

                        window.Show();
                        break;
                    }

                    case StartupWindow.RunType.Server: {
                        AiServer ai = new(startup.IP, startup.Port, Settings.Default.VlcPath, startup.VlcPort);
                        ServerWindow window = new(ai);

                        window.Closed += (_, _) => {
                            ai.Dispose();
                        };

                        window.Show();
                        break;
                    }
                }
            };

            startup.Show();
        }
    }
}
