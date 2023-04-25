using AiSync;
using HeyRed.Mime;

using LibVLCSharp.Shared;

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AiSyncServer {
    public partial class ServerWindow {
        #region Setup
        private void ServerWindow_Loaded(object sender, RoutedEventArgs e) {
            Core.Initialize();

            SetPreStart();
            ValidateServerParams();
        }

        private void ServerWindow_Closed(object? sender, EventArgs e) {
            Media?.Dispose();
            _vlc?.Dispose();
            CommServer?.Dispose();
            DataServer?.Dispose();
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

        private void StartServer_Click(object sender, RoutedEventArgs e) {
            LockUI(true);
            CommServer = new AiServer(_logger_factory, IPAddress.Any, CommPort.ParseText<ushort>());

            CommServer.ClientConnected += (_, _) => Dispatcher.Invoke(ClientConnected);

            CommServer.ServerStarted += (_, _) => Dispatcher.Invoke(() => {
                SetStatus("Idle");
                SetStarted();
            });

            CommServer.PlayingChanged += (_, e) => Dispatcher.Invoke(UpdateImages);

            CommServer.PositionChanged += (_, e) =>
                Dispatcher.Invoke(() => SetCurrentPos(e.Position));

            CommServer.Start();
        }

        private async void UploadFile_Click(object sender, RoutedEventArgs e) {
            if (!CommRunning) {
                return;
            }

            if (Utils.GetFile(out string file)) {
                FileInfo info = new(file);
                string mime = MimeTypesMap.GetMimeType(info.Extension);

                if (!mime.StartsWith("video")) {
                    /* Not a video, ignore */
                    MessageBox.Show($"Invalid MIME type:\n\n{mime}", "AiSync Server");
                    return;
                }

                LockUI(true);

                Media = new Media(VLC, new Uri(info.FullName));
                await Media.Parse();

                FileSelected.Text = info.Name;
                FileMime.Text = mime;

                Duration.Text = AiSync.Utils.FormatTime(Media.Duration);
                SetCurrentPos(0);

                DataServer = new AiFileServer(IPAddress.Any, DataPort.ParseText<ushort>(), info.FullName, mime);
                CommServer.SetHasFile();
                SetHasFile();
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e) {
            /* Close any media, remove current file, notify clients */
            if (!CommRunning) {
                return;
            }

            LockUI(true);

            FileSelected.Text = "(none)";
            FileMime.Text = "(none)";
            Duration.Text = "--:--";
            CurrentPos.Text = "--:--";

            /* TODO notify clients */
            CommServer.StopPlayback();

            Media?.Dispose();
            Media = null;

            DataServer?.Dispose();
            DataServer = null;

            SetStarted();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) {
            if (!CommRunning) {
                return;
            }

            if (CommServer.Playing) {
                CommServer.Pause(CommServer.Position);
            } else {
                CommServer.Play(CommServer.Position);
            }

            UpdateImages();
        }

        private void StopServer_Click(object sender, RoutedEventArgs e) {
            LockUI(true);
            if (DataRunning) {
                DataServer.Stop();
                DataServer.Dispose();
            }

            if (CommRunning) {
                CommServer.Stop();
                CommServer.Dispose();
            }
            SetPreStart();
        }
        #endregion
    }
}
