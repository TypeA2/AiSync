using AiSync;

using LibVLCSharp.Shared;

using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        private static readonly ImageSource fullscreen_image = ReadImage("icons8-fullscreen.png");
        private static readonly ImageSource fullscreen_disabled_image = ReadImage("icons8-fullscreen-disabled.png");

        private static readonly ImageSource exit_fullscreen_image = ReadImage("icons8-exit-fullscreen.png");
        private static readonly ImageSource exit_fullscreen_disabled_image = ReadImage("icons8-exit-fullscreen-disabled.png");

        private LibVLC? _vlc;
        private LibVLC VLC { get => _vlc ??= new(); }

        private LibVLCSharp.Shared.MediaPlayer? _player;
        private LibVLCSharp.Shared.MediaPlayer Player {
            get =>_player ?? throw new InvalidOperationException("No player set");
            set => _player = value;
         }

        private Media? Media {
            get => Player?.Media;
        }

        [MemberNotNullWhen(returnValue: true, nameof(Media))]
        private bool HasMedia => _player != null && Media != null;

        private AiClient? _comm_client;
        private AiClient CommClient {
            get => _comm_client ?? throw new InvalidOperationException("No client exists");
            set => _comm_client = value;
        }

        private bool scrubbing = false;
        private bool remote_scrubbing = false;

        private readonly object hide_ui_lock = new();
        private bool hide_ui_running = false;
        private bool hide_ui_cancelled = false;
        private int hide_ui_sleep = 0;
        private DateTime hide_ui_last_start;

        private const int hide_ui_delay = 1250;

        private readonly ILoggerFactory _logger_factory = LoggerFactory.Create(builder => {
            builder.AddAiLogger()
                   .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        public ClientWindow() {
            InitializeComponent();
        }

        private string AutoToolTipFormatter(double pos) {
            if (!HasMedia) {
                return "(unknown)";
            }

            return GetTimeString(pos);
        }

        private void SetVolume(int new_val, Slider? source) {
            if (!Video.IsLoaded) {
                return;
            }

            Player.Volume = new_val;
            FullscreenVolumeDisplay.Text = $"{new_val}%";
            VolumeDisplay.Text = $"{new_val}%";

            /* Update the other scrubber's value */
            if (source != Volume) {
                Volume.Value = new_val;
            }
                
            if (source != FullscreenVolume) {
                FullscreenVolume.Value = new_val;
            }
        }

        private void UpdateImages() {
            if (!HasMedia) {
                return;
            }

            if (Player.IsPlaying) {
                ImageSource src = PlayPause.IsEnabled ? pause_image : pause_disabled_image;
                PlayPauseImage.Source = src;
                FullscreenPlayPauseImage.Source = src;
            } else {
                ImageSource src = PlayPause.IsEnabled ? play_image : play_disabled_image; ;
                PlayPauseImage.Source = src;
                FullscreenPlayPauseImage.Source = src;
            }

            EnterFullscreenImage.Source = EnterFullscreen.IsEnabled ? fullscreen_image : fullscreen_disabled_image;

            ExitFullscreenImage.Source = ExitFullscreen.IsEnabled ? exit_fullscreen_image : exit_fullscreen_disabled_image;
        }

        private void UpdateCurrentPosition() {
            if (!HasMedia) {
                return;
            }

            CurrentPosition.Text = GetTimeString();
            FullscreenCurrentPosition.Text = GetTimeString();

            if (!scrubbing) {
                Scrubber.Value = Player.Position;
                FullscreenScrubber.Value = Player.Position;
            }
        }

        private async void CloseMedia() {
            LockUI(true);

            if (HasMedia) {
                if (Player.IsPlaying) {
                    await Task.Run(Player.Stop);
                }

                /* Dispose media but keep player alive */
                Media.Dispose();
                Player.Media = null;
            }

            SetNoMedia();
        }

        private void CancelFullscreenTimeout() {
            lock (hide_ui_lock) {
                if (hide_ui_running) {
                    hide_ui_cancelled = true;
                    /* Don't return here in case we were just slightly too late I guess */
                }
            }

            if (FullScreenControls.Visibility == Visibility.Collapsed) {
                FullScreenControls.Visibility = Visibility.Visible;
            }

            VideoInteraction.Cursor = Cursors.Arrow;
        }

        private void StartFullscreenTimeout() {
            lock (hide_ui_lock) {

                if (hide_ui_running) {
                    hide_ui_cancelled = false;

                    TimeSpan elapsed = DateTime.Now - hide_ui_last_start;

                    /* We've already slept for elapsed ms, so (1000 - elapsed) remaining
                     * We want to sleep until 1000ms from now, so (1000 - elapsed) + elapsed = 1000
                     */
                    hide_ui_sleep += (int)Math.Round(elapsed.TotalMilliseconds);
                    hide_ui_last_start = DateTime.Now;

                    return;
                }

                hide_ui_last_start = DateTime.Now;

                hide_ui_running = true;
                hide_ui_cancelled = false;
                hide_ui_sleep = hide_ui_delay;
            }

            /* Wait 1 second, if not cancelled, hide fullscreen controls */
            Task.Run(() => {
                while (true) {
                    int sleep_for = 0;
                    lock (hide_ui_lock) {
                        sleep_for = Int32.Min(hide_ui_delay, hide_ui_sleep);
                    }

                    Thread.Sleep(sleep_for);

                    lock (hide_ui_lock) {
                        hide_ui_sleep -= sleep_for;
                        if (hide_ui_sleep <= 0) {
                            if (!hide_ui_cancelled) {
                                Dispatcher.Invoke(() => {
                                    if (!FullScreenControls.IsMouseOver && fullscreen) {
                                        FullScreenControls.Visibility = Visibility.Collapsed;
                                        VideoInteraction.Cursor = Cursors.None;
                                    }
                                });
                            }

                            hide_ui_running = false;
                            return;
                        }
                    }
                }
            });
        }

        private string GetTimeString() {
            return HasMedia ? GetTimeString(Player.PositionMs()) : "--:--";
        }

        private string GetTimeString(float pos) {
            return HasMedia ? GetTimeString((long)Math.Round(pos * Media.Duration)) : "--:--";
        }

        private string GetTimeString(double pos) {
            return HasMedia ? GetTimeString((long)Math.Round(pos * Media.Duration)) : "--:--";
        }

        private string GetTimeString(long pos) {
            return AiSync.Utils.FormatTime(pos, false, (HasMedia ? Media.Duration : 0) >= (3600 * 1000));
        }


        private bool fullscreen = false;
        /* Saved window styles */
        private WindowStyle style;
        private WindowState state;
        private ResizeMode resize;

        private void SetPreConnect() {
            LockUI(true);
            Address.IsEnabled = true;
            CommPort.IsEnabled = true;
            DataPort.IsEnabled = true;
        }

        private void SetNoMedia() {
            LockUI(true);
            Disconnect.IsEnabled = true;

            FullscreenCurrentPosition.Text = "--:--";
            FullscreenDuration.Text = "--:--";
            FullscreenScrubber.Value = 0;

            CurrentPosition.Text = "--:--";
            Duration.Text = "--:--";
            Scrubber.Value = 0;
        }

        private void SetDefaulPlayback() {
            LockUI(true);

            PlayPause.IsEnabled = true;
            // Scrubber.IsEnabled = true;
            Volume.IsEnabled = true;

            FullscreenPlayPause.IsEnabled = true;
            // FullscreenScrubber.IsEnabled = true;
            FullscreenVolume.IsEnabled = true;

            EnterFullscreen.IsEnabled = true;
            ExitFullscreen.IsEnabled = true;

            Disconnect.IsEnabled = true;
        }

        private void SetFullscreen(bool new_state) {
            if (new_state) {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;

                ControlGrid.Visibility = Visibility.Collapsed;
                FullScreenControls.Visibility = Visibility.Visible;

                fullscreen = true;
            } else {
                WindowState = state;
                WindowStyle = style;
                ResizeMode = resize;

                ControlGrid.Visibility = Visibility.Visible;
                FullScreenControls.Visibility = Visibility.Collapsed;

                fullscreen = false;
            }
        }

        private void LockUI(bool locked) {
            bool new_state = !locked;

            /* Disabling these is kind of pointless */
            // ExitFullscreen.IsEnabled = new_state;
            // EnterFullscreen.IsEnabled = new_state;

            /* Same for these, these are client-side only */
            // FullscreenVolume.IsEnabled = new_state;
            // Volume.IsEnabled = new_state;

            FullscreenScrubber.IsEnabled = new_state;
            FullscreenPlayPause.IsEnabled = new_state;

            Scrubber.IsEnabled = new_state;
            Address.IsEnabled = new_state;
            CommPort.IsEnabled = new_state;
            DataPort.IsEnabled = new_state;
            PlayPause.IsEnabled = new_state;

            Connect.IsEnabled = new_state;
            Disconnect.IsEnabled = new_state;
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

        private void SetPlaying(bool new_playing) {
            if (!HasMedia) {
                return;
            }

            if (LocalControls.IsChecked ?? false) {
                if (new_playing) {
                    LastAction.Text = "Local play";
                    Player.Play();
                } else {
                    LastAction.Text = "Local pause";
                    Player.Pause();
                }
                
                return;
            }

            PlayPause.IsEnabled = false;
            FullscreenPlayPause.IsEnabled = false;

            if (new_playing) {
                LastAction.Text = $"Request play at {GetTimeString()}";
                CommClient.RequestPlay(Player.PositionMs());
            } else {
                LastAction.Text = $"Request pause at {GetTimeString()}";
                CommClient.RequestPause(Player.PositionMs());
            }
        }

        private void Scrub(long target_ms) {
            if (HasMedia) {
                if (LocalControls.IsChecked ?? false) {
                    LastAction.Text = $"Local seek to {GetTimeString(target_ms)}";

                    Player.SeekTo(TimeSpan.FromMilliseconds(target_ms));
                } else {
                    LastAction.Text = $"Request seek to {GetTimeString(target_ms)}";

                    CommClient.RequestSeek(target_ms);

                    Scrubber.IsEnabled = false;
                    FullscreenScrubber.IsEnabled = false;
                }
            }
        }

        private bool HandleKey(Key key) {
            switch (key) {
                case Key.Space:
                    if (PlayPause.IsEnabled) {
                        SetPlaying(!Player.IsPlaying);
                        return true;
                    }
                    break;

                case Key.Left:
                    if (HasMedia && Scrubber.IsEnabled) {
                        /* 5 second steps */
                        Scrub(Player.PositionMs() - 5000);
                        return true;
                    }
                    break;

                case Key.Right:
                    if (HasMedia && Scrubber.IsEnabled) {
                        /* 5 second steps */
                        Scrub(Player.PositionMs() + 5000);
                        return true;
                    }
                    break;

                case Key.F:
                    SetFullscreen(!fullscreen);
                    return true;

                case Key.Escape:
                    SetFullscreen(false);
                    return true;
            }

            return false;
        }

        private async void LoadMedia() {
            if (Player is null) {
                throw new InvalidOperationException("Attempted to load media for null player");
            }

            Player.Media = new Media(VLC, $"http://{Address.Text}:{DataPort.Text}/", FromType.FromLocation);

            Player.Media.DurationChanged +=
                    (_, e) => Dispatcher.Invoke(() => {
                        string duration_str = AiSync.Utils.FormatTime(e.Duration, false);
                        Duration.Text = duration_str;
                        FullscreenDuration.Text = duration_str;
                        UpdateCurrentPosition();
                    });


            await Player.Media.Parse(MediaParseOptions.ParseNetwork);

            Player.Volume = (int)Math.Round(Volume.Value);

            LastAction.Text = "Media loaded";
            ServerStatus.Text = "New media";

            CommClient.FileParsed();
            SetDefaulPlayback();
        }
    }
}
