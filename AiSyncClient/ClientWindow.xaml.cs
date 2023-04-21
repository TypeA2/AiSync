using AiSync;

using LibVLCSharp.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AiSyncClient {
    public partial class ClientWindow : Window {

        private static ImageSource ReadImage(string url) => new BitmapImage(new Uri($"/AiSyncClient;component/{url}", UriKind.Relative));

        private static readonly ImageSource play_image = ReadImage("icons8-play.png");
        private static readonly ImageSource play_disabled_image = ReadImage("icons8-play-disabled.png");

        private static readonly ImageSource pause_image = ReadImage("icons8-pause.png");
        private static readonly ImageSource pause_disabled_image = ReadImage("icons8-pause-disabled.png");

        private LibVLC? _vlc;
        private LibVLC VLC { get => _vlc ??= new(); }

        private LibVLCSharp.Shared.MediaPlayer? _player;
        private LibVLCSharp.Shared.MediaPlayer Player {
            get => _player ?? throw new InvalidOperationException("No player is opened");
            set => _player = value;
        }

        private AiClient? _comm_client;
        private AiClient CommClient {
            get => _comm_client ?? throw new InvalidOperationException("No client exists");
            set => _comm_client = value;
        }

        private bool playing = false;
        private bool scrubbing = false;

        private readonly ILoggerFactory _logger_factory = LoggerFactory.Create(builder => {
            builder.AddAiLogger()
                   .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        public ClientWindow() {
            InitializeComponent();

            Loaded += Window_Loaded;
            Closed += ClientWindow_Closed;
            PreviewKeyDown += ClientWindow_PreviewKeyDown;
        }

        private string AutoToolTipFormatter(double pos) {
            if (_player is null || Player.Media is null) {
                return "(unknown)";
            }

            return GetTimeString(pos);
        }

        private void UpdatePlayPause() {
            if (playing) {
                PlayPauseImage.Source = PlayPause.IsEnabled ? pause_image : pause_disabled_image;
            } else {
                PlayPauseImage.Source = PlayPause.IsEnabled ? play_image : play_disabled_image;
            }
        }

        private void UpdateCurrentPosition() {
            if (_player is null || Player.Media is null) {
                return;
            }

            CurrentPosition.Text = GetTimeString();

            if (!scrubbing) {
                Scrubber.Value = Player.Position;
            }
        }

        private void PlaybackEnded() {
            playing = false;

            CurrentPosition.Text = "--:--";
            Duration.Text = "--:--";
            Scrubber.Value = 0;

            PlayPause.IsEnabled = false;
        }

        private string GetTimeString() => GetTimeString(Player.PositionMs());

        private string GetTimeString(float pos) => GetTimeString((long)Math.Round(pos * (Player.Media?.Duration ?? 0)));

        private string GetTimeString(double pos) => GetTimeString((long)Math.Round(pos * (Player.Media?.Duration ?? 0)));

        private string GetTimeString(long pos)
            => AiSync.Utils.FormatTime(pos, false, (Player.Media?.Duration ?? 0) >= (3600 * 1000));

        private void ClientWindow_Closed(object? sender, EventArgs e) {
            _player?.Dispose();
            _vlc?.Dispose();
            _comm_client?.Dispose();
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            int volume = (int)Math.Round(e.NewValue, 0);

            VolumeDisplay.Text = $"{volume}%";

            if (_player is not null) {
                Player.Volume = volume;
            }
        }

        private void Volume_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            //Volume.Value += Volume.SmallChange * (e.Delta < 0 ? -1 : 1);
        }

        private void Video_Loaded(object sender, RoutedEventArgs e) {
            Core.Initialize();

            Player = new LibVLCSharp.Shared.MediaPlayer(VLC);

            Video.MediaPlayer = Player;
        }

        private bool fullscreen = false;
        private WindowStyle style;
        private WindowState state;
        private ResizeMode resize;

        private (double height, double width) GetVirtualWindowSize() {
            Window virtualWindow = new Window();
            virtualWindow.Show();
            virtualWindow.Opacity = 0;
            virtualWindow.WindowState = WindowState.Maximized;
            double returnHeight = virtualWindow.Height;
            double returnWidth = virtualWindow.Width;
            virtualWindow.Close();
            return (returnHeight, returnWidth);
        }

        private void ToggleFullscreen() {
            Trace.WriteLine($"Setting fullscreen to: {!fullscreen}");
            if (fullscreen) {
                WindowState = state;
                WindowStyle = style;
                ResizeMode = resize;

                ScrubberGrid.Visibility = Visibility.Visible;
                ParameterGrid.Visibility = Visibility.Visible;
                ButtonGrid.Visibility = Visibility.Visible;

                InputParameters.Visibility = Visibility.Visible;

                Grid.SetRowSpan(Video, 1);

                //ControlGrid.Background = Brushes.Transparent;
                ControlGrid.Visibility = Visibility.Visible;

                fullscreen = false;
            } else {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;

                ButtonGrid.Visibility = Visibility.Collapsed;

                InputParameters.Visibility = Visibility.Collapsed;

                Grid.SetRowSpan(Video, 2);

                //ControlGrid.Background = Brushes.White;
                ControlGrid.Visibility = Visibility.Collapsed;

                fullscreen = true;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            PlayPause.IsEnabledChanged += (_, _) => UpdatePlayPause();

            style = WindowStyle;
            state = WindowState;
            resize = ResizeMode;

            Video.Loaded += Video_Loaded;

            Scrubber.Formatter = AutoToolTipFormatter;
            
            LockUI(true);
            Address.IsEnabled = true;
            VolumeDisplay.Width = VolumeDisplay.ActualWidth;

            ValidateConnectionParams();
            UpdatePlayPause();
        }

        private void LockUI(bool locked) {
            bool new_state = !locked;

            Scrubber.IsEnabled = new_state;
            Connect.IsEnabled = new_state;
            Disconnect.IsEnabled = new_state;
            PlayPause.IsEnabled = new_state;
            Volume.IsEnabled = new_state;
            Address.IsEnabled = new_state;
            CommPort.IsEnabled = new_state;
            DataPort.IsEnabled = new_state;
        }

        private void ValidateConnectionParams() {
            if (!IsLoaded) {
                return;
            }

            bool comm_good = CommPort.TryParseText(out uint comm_port) && comm_port > 1023 && comm_port <= 65353;
            bool data_good = DataPort.TryParseText(out uint data_port) && data_port > 1023 && data_port <= 63353;
            bool addr_good = IPAddress.TryParse(Address.Text, out IPAddress? _);

            if (comm_good && data_good && comm_port == data_port) {
                comm_good = false;
                data_good = false;
            }

            CommPortText.Foreground = comm_good ? Brushes.Black : Brushes.Red;
            DataPortText.Foreground = data_good ? Brushes.Black : Brushes.Red;
            AddressText.Foreground  = addr_good ? Brushes.Black : Brushes.Red;

            Connect.IsEnabled = (comm_good && data_good && addr_good);
        }

        private void TogglePlayback() {
            if (_player is null || Player.Media is null) {
                return;
            }

            /* Manually desync when connected to local ip (but not loopback) */
            if (LocalControls.IsChecked ?? false) {
                if (playing) {
                    LastAction.Text = "Local pause";
                    Player.Pause();
                } else {
                    LastAction.Text = "Local play";
                    Player.Play();
                }
                playing = !playing;
                UpdatePlayPause();
                return;
            }

            PlayPause.IsEnabled = false;

            if (playing) {
                LastAction.Text = $"Request pause at {GetTimeString()}";
                CommClient.RequestPause(Player.PositionMs());
            } else {
                LastAction.Text = $"Request play at {GetTimeString()}";
                CommClient.RequestPlay(Player.PositionMs());
            }
        }

        private void Scrub(double target) {
            if (_player is not null && Player.Media is not null) {
                long target_ms = (long)Math.Round(Player.Media.Duration * target);

                if (LocalControls.IsChecked ?? false) {
                    LastAction.Text = $"Local seek to {GetTimeString(target)}";

                    Player.SeekTo(TimeSpan.FromMilliseconds(target_ms));
                } else {
                    LastAction.Text = $"Request seek to {GetTimeString(target)}";

                    CommClient.RequestSeek(target_ms);

                    Scrubber.IsEnabled = false;
                }
            }
        }

        private void ClientWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
            Trace.WriteLine($"Key: {e.Key}");
            switch (e.Key) {
                case Key.Space:
                    if (PlayPause.IsEnabled) {
                        TogglePlayback();
                    }
                    e.Handled = true;
                    break;

                case Key.Left:
                    if (_player is not null && Player.Media is not null && Scrubber.IsEnabled) {
                        /* 5 second steps */
                        Scrub(Scrubber.Value - (5000.0 / Player.Media.Duration));
                    }
                    e.Handled = true;
                    break;

                case Key.Right:
                    if (_player is not null && Player.Media is not null && Scrubber.IsEnabled) {
                        /* 5 second steps */
                        Scrub(Scrubber.Value + (5000.0 / Player.Media.Duration));
                    }
                    e.Handled = true;
                    break;

                case Key.F:
                    ToggleFullscreen();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    if (fullscreen) {
                        ToggleFullscreen();
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void Address_TextChanged(object sender, TextChangedEventArgs e) {
            ValidateConnectionParams();
        }

        private void Port_TextChanged(object sender, TextChangedEventArgs e) {
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
            CommClient.EnableControls += (_, _) => Dispatcher.Invoke(() => {
                PlayPause.IsEnabled = true;
                Scrubber.IsEnabled = true;
                Volume.IsEnabled = true;
            });

            CommClient.PausePlay += (_, e) => Dispatcher.Invoke(() => CommClient_PausePlay(e));

            CommClient.Seek += (_, e) => Dispatcher.Invoke(() => {
                if (Player.Media is not null && Player.Media.IsParsed) {
                    LastAction.Text = $"Seek to {GetTimeString(e.Target)}";

                    Player.SeekTo(TimeSpan.FromMilliseconds(e.Target));
                    Scrubber.IsEnabled = true;
                }
            });

            CommClient.UpdateStatus += (_, _) => Dispatcher.Invoke(() =>
                CommClient.SetStatus(Player.IsPlaying, (Player.Media is null) ? 0 : Player.PositionMs()));

            bool connected = await CommClient.Connect();

            if (connected) {
                LastAction.Text = "Connected to server";

                Disconnect.IsEnabled = true;
            } else {
                LastAction.Text = "Failed to connect to server";
                CommPort.IsEnabled = true;
                DataPort.IsEnabled = true;
                Address.IsEnabled = true;
                Connect.IsEnabled = true;
            }
        }

        private void CommClient_PausePlay(PausePlayEventArgs e) {
            /* Adjust position to sync with other clients if we're too far out of sync */
            long diff = Player.PositionMs().Difference(e.Position);
            TimeSpan? adjust = (diff > CommClient.CloseEnoughValue)
                ? TimeSpan.FromMilliseconds(e.Position) : null;
            if (e.IsPlaying && !playing && Player.Media is not null && Player.Media.IsParsed) {
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
            UpdatePlayPause();
        }

        private async void LoadMedia() {
            Player.Media = new Media(VLC, $"http://{Address.Text}:{DataPort.Text}/", FromType.FromLocation);

            Player.Media.DurationChanged +=
                    (_, e) => Dispatcher.Invoke(() => {
                        Duration.Text = AiSync.Utils.FormatTime(e.Duration, false);
                        UpdateCurrentPosition();
                    });

            Player.PositionChanged +=
                (_, _) => Dispatcher.Invoke(UpdateCurrentPosition);

            Player.EndReached +=
                (_, _) => Dispatcher.Invoke(PlaybackEnded);

            await Player.Media.Parse(MediaParseOptions.ParseNetwork);

            Player.Volume = (int)Math.Round(Volume.Value, 0);

            LastAction.Text = "Media loaded";
            ServerStatus.Text = "New media";

            CommClient.FileParsed();
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

        private void PlayPause_Click(object sender, RoutedEventArgs e) {
            TogglePlayback();
        }

        private void Scrubber_DragStarted(object sender, DragStartedEventArgs e) {
            scrubbing = true;
        }

        private void Scrubber_DragDelta(object sender, DragDeltaEventArgs e) {

        }

        private void Scrubber_DragCompleted(object sender, DragCompletedEventArgs e) {
            Scrub(Scrubber.Value);

            scrubbing = false;
        }

        private void Video_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            ToggleFullscreen();
        }

        private void Video_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            Volume.Value += Volume.SmallChange * (e.Delta < 0 ? -1 : 1);
        }
    }
}
