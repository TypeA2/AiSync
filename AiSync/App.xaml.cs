using Microsoft.Win32;

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ai_sync {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private static bool GetVlcPath() {
            OpenFileDialog dialog = new OpenFileDialog() {
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

            AiSync ai = new(Settings.Default.VlcPath);

            MainWindow main = new(ai);

            main.Closed += (_, _) => {
                ai.Dispose();
            };

            main.Show();

            /* Retrieve whether we're client or server */
            StartupWindow startup = new() {
                Owner = main,
            };

            startup.ShowDialog();

            Trace.WriteLine($"Running as {startup.RunAs}");

            switch (startup.RunAs) {
                case StartupWindow.RunType.None:
                    main.Close();
                    break;

                case StartupWindow.RunType.Client:
                    ai.StartClient(startup.IP, startup.Port);
                    main.SetAsClient();
                    break;

                case StartupWindow.RunType.Server:
                    ai.StartServer(startup.IP, startup.Port);
                    main.SetAsServer();
                    break;
            }
        }
    }
}
