using Microsoft.Win32;

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AiSync {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class ServerWindow : Window {
        private readonly ImageSource _play_icon  = Utils.ExtractIcon(@"C:\Windows\System32\mmcndmgr.dll", -30529);
        private readonly ImageSource _pause_icon = Utils.ExtractIcon(@"C:\Windows\System32\mmcndmgr.dll", -30529);
        private readonly ImageSource _add_icon = Utils.ExtractIcon(@"C:\Windows\System32\mmcndmgr.dll", -30503);

        private readonly AiServer ai;

        public ServerWindow(AiServer ai) {
            this.ai = ai;

            InitializeComponent();

            PlayPauseImage.Source = _play_icon;
            SelectFileImage.Source = _add_icon;

            this.ai.SessionOpened += (_, _) =>
                Dispatcher.Invoke(() => ClientsConnected.Text = ai.ClientCount.ToString());

            this.ai.SessionClosed += (_, _) =>
                Dispatcher.Invoke(() => ClientsConnected.Text = ai.ClientCount.ToString());

            LockUI(true);
            SelectFile.IsEnabled = true;
        }

        private void LockUI(bool locked) {
            PlayPause.IsEnabled = !locked;
            Volume.IsEnabled = !locked;
            Scrubber.IsEnabled = !locked;
            SelectFile.IsEnabled = !locked;
        }

        private void ResetProgressRangePercent(double max) {
            CurrentProgress.Maximum = max;
            CurrentProgress.Value = 0;
            CurrentProgressText.Text = "0.0 %";
        }

        private void ResetProgressRangeInteger(long max) {
            CurrentProgress.Maximum = max;
            CurrentProgress.Value = 0;
            CurrentProgressText.Text = $"0 / {max}";
        }

        private void ClearProgressRange() {
            CurrentProgress.Value = 0;
            CurrentProgressText.Text = String.Empty;
        }

        private void UpdateProgressPercent(double new_val) {
            double percent = new_val / CurrentProgress.Maximum * 100;

            CurrentProgress.Value = new_val;
            CurrentProgressText.Text = String.Format("{0:0.0}%", percent);
        }

        private void UpdateProgressInteger(long new_val, long max_val) {
            CurrentProgress.Value = new_val;
            CurrentProgressText.Text = $"{new_val} / {max_val}";
        }

        /*
        public void OnConnectionOpened(object? sender, EventArgs e) {
            if (is_server) {
                Title = $"Ai Sync - {ai.Server.ClientCount} Clients";
            } else {
                Title = "Ai Sync - Connected";
            }
        }

        public void OnConnectionClosed(object? sender, EventArgs e) {
            StatusText.Text = "Connection closed";

            LockUI(true);
        }

        public void OnNewFileSelected(object? sender, AiNewFileSelected msg) {
            MessageBox.Show($"Please select the following file:\n\n{msg.Name}");

            bool hash_matches = false;

            while (!hash_matches) {
                OpenFileDialog dialog = new() {
                    Filter = "All files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true) {
                    MessageBox.Show($"Hash mismatch: {msg.Hash}");
                }
            }
        }

        private void PlaybackStopped(object? sender, EventArgs e) {
            PlayPauseImage.Source = _play_icon;
        }

        private void PlaybackStarted(object? sender, EventArgs e) {
            PlayPauseImage.Source = _pause_icon;
        }
        */

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            Slider volume = (Slider)sender;

            volume.Value = Math.Round(e.NewValue, 0);

            if (VolumeText != null) {
                VolumeText.Text = $"{volume.Value}%";
            }
        }

        private void Volume_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            Volume.Value += Volume.SmallChange * (e.Delta < 0 ? -1 : 1);
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) {
            
        }

        private async void SelectFile_Click(object sender, RoutedEventArgs e) {
            if (Utils.GetFile(out string file)) {
                FileInfo info = new(file);

                StatusText.Text = "Hashing input file";
                CurrentFile.Text = $"{info.Name} (hashing)";

                ResetProgressRangePercent(info.Length);

                int previous_permille = 0;
                string hash = await Utils.SHA512FileAsync(file, bytes_read => {
                    int permille = (int)((double)bytes_read / info.Length * 1000.0);

                    /* Update every 0.1% at most */
                    if (permille != previous_permille) {
                        Dispatcher.Invoke(() => UpdateProgressPercent(bytes_read));

                        previous_permille = permille;
                    }
                });

                CurrentFile.Text = $"{info.Name} ({hash[..8]}...)";
                StatusText.Text = "Waiting for clients";

                ResetProgressRangeInteger(ai.ClientCount);

                /* Wait for all clients to select and hash file */
                int clients_ready = 0;
                await ai.SetFile(info.Name, hash, () =>
                    Dispatcher.Invoke(() => {
                        ++clients_ready;
                        if (clients_ready == ai.ClientCount) {
                            StatusText.Text = "Idle";
                        } else {
                            UpdateProgressInteger(clients_ready, ai.ClientCount);
                        }
                    })
                );

                ai.AllClientsReady();
                ClearProgressRange();

                LockUI(false);
            }

        }
    }
}
