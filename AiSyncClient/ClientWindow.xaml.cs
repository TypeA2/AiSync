using LibVLCSharp.Shared;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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

        public ClientWindow() {
            InitializeComponent();

            Video.Loaded += Video_Loaded;

            Loaded += Window_Loaded;

            Closed += ClientWindow_Closed;

            PlayPause.IsEnabledChanged += (_, _) => UpdatePlayPause();

            Scrubber.Formatter = AutoToolTipFormatter;
        }

        private string AutoToolTipFormatter(double pos) {
            if (_player is null || Player.Media is null) {
                return "(unknown)";
            }

            double actual_pos = pos * Player.Media.Duration;

            return AiSync.Utils.FormatTime(
                (long)Math.Round(actual_pos),
                false,
                Player.Media.Duration >= (3600 * 1000));
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

            float actual_pos = Player.Position * Player.Media.Duration;

            CurrentPosition.Text = AiSync.Utils.FormatTime(
                (long)Math.Round(actual_pos),
                false,
                Player.Media.Duration >= (3600 * 1000));

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
            Volume.Value += Volume.SmallChange * (e.Delta < 0 ? -1 : 1);
        }

        private void Video_Loaded(object sender, RoutedEventArgs e) {
            Core.Initialize();

            Player = new LibVLCSharp.Shared.MediaPlayer(VLC);

            Video.MediaPlayer = Player;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
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
            CommClient = new AiClient(IPAddress.Parse(Address.Text), CommPort.ParseText<ushort>());

            CommClient.GotFile += (_, _) => Dispatcher.Invoke(LoadMedia);
            CommClient.EnableControls += (_, _) => Dispatcher.Invoke(() => {
                PlayPause.IsEnabled = true;
                Scrubber.IsEnabled = true;
                Volume.IsEnabled = true;
            });

            CommClient.PausePlay += (_, e) => Dispatcher.Invoke(() => CommClient_PausePlay(e));

            CommClient.Seek += (_, e) => Dispatcher.Invoke(() => {
                if (Player.Media is not null && Player.Media.IsParsed) {
                    Player.SeekTo(TimeSpan.FromMilliseconds(e.Target));
                    Scrubber.IsEnabled = true;
                }
            });

            bool connected = await CommClient.Connect();

            if (connected) {
                Disconnect.IsEnabled = true;
            } else {
                CommPort.IsEnabled = true;
                DataPort.IsEnabled = true;
                Address.IsEnabled = true;
                Connect.IsEnabled = true;
            }
        }

        private void CommClient_PausePlay(PausePlayEventArgs e) {
            if (e.IsPlaying && !playing && Player.Media is not null && Player.Media.IsParsed) {
                Player.Play();
                playing = true;
            } else if (playing) {
                Player.Pause();
                playing = false;
            }

            PlayPause.IsEnabled = true;
            UpdatePlayPause();

            /* Adjust position to sync with other clients if we're too far out of sync */
            if (Math.Abs(e.Position - Player.PositionMs()) > CommClient.CloseEnoughValue) {
                Player.SeekTo(TimeSpan.FromMilliseconds(e.Position));
            }
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

            CommPort.IsEnabled = true;
            DataPort.IsEnabled = true;
            Address.IsEnabled = true;
            Connect.IsEnabled = true;
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) {
            if (_player is null || Player.Media is null) {
                return;
            }

            /* Manually desync when connected to local ip (but not loopback) */
            if (Address.Text.StartsWith("192")) {
                if (playing) {
                    Player.Pause();
                } else {
                    Player.Play();
                }
                playing = !playing;
                UpdatePlayPause();
                return;
            }

            PlayPause.IsEnabled = false;

            if (playing) {
                CommClient.RequestPause(Player.PositionMs());
            } else {
                CommClient.RequestPlay(Player.PositionMs());
            }
        }

        private void Scrubber_DragStarted(object sender, DragStartedEventArgs e) {
            scrubbing = true;
        }

        private void Scrubber_DragDelta(object sender, DragDeltaEventArgs e) {

        }

        private void Scrubber_DragCompleted(object sender, DragCompletedEventArgs e) {
            if (_player is not null && Player.Media is not null) {
                CommClient.RequestSeek((long)Math.Round(Player.Media.Duration * Scrubber.Value));

                Scrubber.IsEnabled = false;
            }

            scrubbing = false;
        }
    }
}
