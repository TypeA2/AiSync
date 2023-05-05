using AiSync;

using AiSyncServer.Properties;

using HeyRed.Mime;

using LibVLCSharp.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AiSyncServer {
    public partial class ServerWindow {
        #region Setup
        private void ServerWindow_Loaded(object sender, RoutedEventArgs e) {
            Core.Initialize();

            CommPort.Text = Settings.Default.CommPort.ToString();
            DataPort.Text = Settings.Default.DataPort.ToString();

            ExtraLatency.Text = Settings.Default.ExtraLatency.ToString();

            SetPreStart();
            ValidateServerParams();
        }

        private void ServerWindow_Closed(object? sender, EventArgs e) {
            CommServer?.Dispose();
        }
        #endregion

        #region UI
        private void Button_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) {
            UpdateImages();
        }
        #endregion

        #region Interaction
        private void Port_TextChanged(object sender, TextChangedEventArgs e) {
            ValidateServerParams();
        }

        [GeneratedRegex("[^0-9]+")]
        private static partial Regex NumbersRegex();

        private void Port_PreviewTextInput(object sender, TextCompositionEventArgs e) {
            e.Handled = NumbersRegex().IsMatch(e.Text);
        }

        private void ExtraLatency_TextChanged(object sender, TextChangedEventArgs e) {
            if (!IsLoaded) {
                return;
            }

            /* We already filtered on digits only, so we know it's valid, but limit to 5s */
            bool latency_good = ExtraLatency.TryParseText(out int val) && val >= 0 && val <= 5000;

            ExtraLatencyText.Foreground = latency_good ? Brushes.Black : Brushes.Red;

            if (latency_good) {
                /* TODO actually apply it */

                if (CommRunning) {
                    CommServer.Delay = val;
                }

                Settings.Default.ExtraLatency = val;
                Settings.Default.Save();
            }
        }

        private void StartServer_Click(object sender, RoutedEventArgs e) {
            LockUI(true);
            IPEndPoint comm = new(IPAddress.Any, CommPort.ParseText<ushort>());
            IPEndPoint data = new(IPAddress.Any, DataPort.ParseText<ushort>());
            CommServer = new AiServer(_logger_factory, comm, data);

            CommServer.ClientConnected += (_, _) => Dispatcher.Invoke(UpdateClientCount);
            CommServer.ClientDisconnected += (_, _) => Dispatcher.Invoke(UpdateClientCount);

            CommServer.ServerStarted += (_, _) => Dispatcher.Invoke(() => {
                SetStatus("Idle");
                SetStarted();
            });

            CommServer.PlayingChanged += (_, e) => Dispatcher.Invoke(() => {
                UpdateImages();

                if (e.State == PlayingState.Stopped) {
                    ResetFile();
                }
            });

            CommServer.PositionChanged += (_, e) =>
                Dispatcher.Invoke(() => SetCurrentPos(e.Position));

            CommServer.Start();
        }

        private async void UploadFile_Click(object sender, RoutedEventArgs e) {
            if (!CommRunning) {
                return;
            }

            if (Utils.GetFile(out string file)) {

                _logger.LogInformation("Got file: {}", file);

                FileInfo info = new(file);
                string mime = MimeTypesMap.GetMimeType(info.Extension);

                if (!mime.StartsWith("video")) {
                    /* Not a video, ignore */
                    MessageBox.Show($"Invalid MIME type:\n\n{mime}", "AiSync Server");
                    return;
                }

                LockUI(true);

                await CommServer.LoadMedia(info.FullName, mime);

                FileSelected.Text = info.Name;
                FileMime.Text = mime;

                Duration.Text = AiSync.Utils.FormatTime(CommServer.Duration);
                SetCurrentPos(0);

                SetHasFile();
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e) {
            if (!CommRunning) {
                return;
            }

            _logger.LogInformation("Stopping at {}", AiSync.Utils.FormatTime(CommServer.Position));

            CommServer.StopMedia();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) {
            if (!CommRunning) {
                return;
            }

            _logger.LogInformation("Play/Pause, current state: {} at {}", CommServer.State, AiSync.Utils.FormatTime(CommServer.Position));

            if (CommServer.State == PlayingState.Playing) {
                CommServer.PauseMedia(CommServer.Position);
            } else {
                CommServer.PlayMedia(CommServer.Position);
            }
        }

        private void StopServer_Click(object sender, RoutedEventArgs e) {
            LockUI(true);

            if (CommRunning) {
                CommServer.Dispose();
                CommServer = null;
            }

            ResetUiFields();

            SetPreStart();
            ValidateServerParams();
        }
        #endregion
    }
}
