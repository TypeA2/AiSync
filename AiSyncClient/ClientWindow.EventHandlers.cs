using System.Windows;
using System;
using LibVLCSharp.Shared;
using System.Windows.Input;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using System.Net;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading;
using System.Reflection;
using LibVLCSharp.WPF;
using AiSync;

namespace AiSyncClient {
    public partial class ClientWindow {
        #region Disposal
        private void ClientWindow_Closed(object? sender, EventArgs e) {
            _player?.Dispose();
            _vlc?.Dispose();
            _comm_client?.Dispose();
        }
        #endregion

        #region Initialization
        private void Window_Loaded(object sender, RoutedEventArgs e) {
            style = WindowStyle;
            state = WindowState;
            resize = ResizeMode;

            /* Make these fixed-size so elements don't jump around at below 100% volume */
            VolumeDisplay.Width = VolumeDisplay.ActualWidth;
            FullscreenVolumeDisplay.Width = VolumeDisplay.ActualWidth;

            SetPreConnect();
            ValidateConnectionParams();
            UpdateImages();
        }

        private void Video_Loaded(object sender, RoutedEventArgs e) {
            Core.Initialize();

            Player = new MediaPlayer(VLC);

            Video.MediaPlayer = Player;

            /* Hack to get keyboard input for the ForegroundWindow as well */
            PropertyInfo fgwin_prop = typeof(VideoView).GetProperty("ForegroundWindow",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new NullReferenceException("ForegroundWindow property is null");

            MethodInfo getter = fgwin_prop.GetGetMethod(nonPublic: true)
                ?? throw new NullReferenceException("ForegroundWindow getter is null");

            Window fgwin = (Window)(getter.Invoke(Video, null) 
                ?? throw new NullReferenceException("ForegroundWindow getter returned null"));

            fgwin.PreviewKeyDown += General_PreviewKeyDown;
        }


        private void ImageButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) {
            UpdateImages();
        }

        private async void Disconnect_Click(object sender, RoutedEventArgs e) {
            Player.Position = 0.975f;
            LockUI(true);

            if (playing) {
                await Task.Run(Player.Stop);
            }

            PlaybackEnded();

            CommClient.Disconnect();
            CommClient.Dispose();

            LastAction.Text = "Disconnect from server";

            CommPort.IsEnabled = true;
            DataPort.IsEnabled = true;
            Address.IsEnabled = true;
            Connect.IsEnabled = true;
        }

        #endregion

        #region Input
        private void TextInput_TextChanged(object sender, TextChangedEventArgs e) {
            ValidateConnectionParams();
        }

        [GeneratedRegex("[^0-9]+")]
        private static partial Regex NumbersRegex();

        private void Port_PreviewTextInput(object sender, TextCompositionEventArgs e) {
            e.Handled = NumbersRegex().IsMatch(e.Text);
        }

        private async void Connect_Click(object sender, RoutedEventArgs e) {
            LockUI(true);

            /* Do connection stuff */
            CommClient = new AiClient(_logger_factory, IPAddress.Parse(Address.Text), CommPort.ParseText<ushort>());

            CommClient.GotFile += (_, _) => Dispatcher.Invoke(LoadMedia);
            CommClient.EnableControls += (_, _) => Dispatcher.Invoke(SetDefaulPlayback);

            CommClient.PausePlay += (_, e) => Dispatcher.Invoke(() => CommClient_PausePlay(e));

            CommClient.Seek += (_, e) => Dispatcher.Invoke(() => {
                if (HasMedia) {
                    LastAction.Text = $"Seek to {GetTimeString(e.Target)}";

                    Player.SeekTo(TimeSpan.FromMilliseconds(e.Target));
                    Scrubber.IsEnabled = true;
                    FullscreenScrubber.IsEnabled = true;
                }
            });

            CommClient.UpdateStatus += (_, _) => Dispatcher.Invoke(() =>
                CommClient.SetStatus(Player.IsPlaying,Player.PositionMs()));

            bool connected = await CommClient.Connect();

            if (connected) {
                LastAction.Text = "Connected to server";
                SetDefaulPlayback();
            } else {
                LastAction.Text = "Failed to connect to server";
                SetPreConnect();
            }
        }
        #endregion

        #region Playback
        private void General_PreviewKeyDown(object sender, KeyEventArgs e) {
            e.Handled = HandleKey(e.Key);
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            int volume = (int)Math.Round(e.NewValue);

            SetVolume(volume, (Slider)sender);
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) {
            TogglePlayback();
        }

        private void Scrubber_DragStarted(object sender, DragStartedEventArgs e) {
            scrubbing = true;
        }

        private void Scrubber_DragCompleted(object sender, DragCompletedEventArgs e) {
            if (!HasMedia) {
                scrubbing = false;
                return;
            }

            long new_pos = (long)Math.Round(((AiSlider)sender).Value * Media.Duration);
            Scrub(new_pos);

            scrubbing = false;
        }

        bool yes = false;

        private void VideoInteraction_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) {
                SetFullscreen(!fullscreen);
            }
        }

        private void Volume_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (HasMedia && (Volume.IsEnabled || FullscreenVolume.IsEnabled)) {
                Volume.Value += Volume.SmallChange * (e.Delta < 0 ? -1 : 1);
            }
        }

        private void CommClient_PausePlay(PausePlayEventArgs e) {
            /* Adjust position to sync with other clients if we're too far out of sync */
            long diff = Player.PositionMs().Difference(e.Position);
            TimeSpan? adjust = (diff > CommClient.CloseEnoughValue)
                ? TimeSpan.FromMilliseconds(e.Position) : null;
            if (e.IsPlaying && !playing && HasMedia) {
                if (adjust is not null) {
                    if (e.Position > Player.PositionMs()) {
                        LastAction.Text = $"Server seek forward by {diff} ms and play";
                    } else {
                        LastAction.Text = $"Server seek backward by {diff} ms and play";
                    }

                    Player.SeekTo(adjust.Value);
                } else {
                    LastAction.Text = $"Server play (Δ={diff} ms)";
                }

                Player.Play();
                playing = true;

            } else if (playing) {
                Player.Pause();
                playing = false;

                if (adjust is not null) {
                    if (e.Position > Player.PositionMs()) {
                        LastAction.Text = $"Server pause and seek forward by {diff} ms";
                    } else {
                        LastAction.Text = $"Server pause and seek backward by {diff} ms";
                    }

                    Player.SeekTo(adjust.Value);
                } else {
                    LastAction.Text = $"Server pause (Δ={diff} ms)";
                }
            }

            PlayPause.IsEnabled = true;
            FullscreenPlayPause.IsEnabled = true;
            UpdateImages();
        }

        private void VideoInteraction_MouseEnter(object sender, MouseEventArgs e) {
            if (fullscreen) {
                CancelFullscreenTimeout();
            }
        }

        private void VideoInteraction_MouseLeave(object sender, MouseEventArgs e) {
            if (fullscreen) {
                StartFullscreenTimeout();
            }
        }

        private void VideoInteraction_MouseMove(object sender, MouseEventArgs e) {
            if (fullscreen) {
                if (FullScreenControls.Visibility == Visibility.Collapsed) {
                    CancelFullscreenTimeout();
                } else {
                    StartFullscreenTimeout();
                }
            }
        }

        private void ExitFullscreen_Click(object sender, RoutedEventArgs e) {
            SetFullscreen(false);
        }

        private void EnterFullscreen_Click(object sender, RoutedEventArgs e) {
            SetFullscreen(true);
        }
        #endregion
    }
}
