using AiSync;

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;
using AiSyncServer.Properties;

namespace AiSyncServer {
    public partial class ServerWindow : Window {
        private static ImageSource ReadImage(string url) => new BitmapImage(new Uri($"/AiSyncServer;component/{url}", UriKind.Relative));

        private static readonly ImageSource upload_image = ReadImage("icons8-upload.png");
        private static readonly ImageSource upload_disabled_image = ReadImage("icons8-upload-disabled.png");

        private static readonly ImageSource play_image = ReadImage("icons8-play.png");
        private static readonly ImageSource play_disabled_image = ReadImage("icons8-play-disabled.png");

        private static readonly ImageSource pause_image = ReadImage("icons8-pause.png");
        private static readonly ImageSource pause_disabled_image = ReadImage("icons8-pause-disabled.png");

        private static readonly ImageSource stop_image = ReadImage("icons8-stop.png");
        private static readonly ImageSource stop_disabled_image = ReadImage("icons8-stop-disabled.png");

        private AiServer? CommServer { get; set; }

        [MemberNotNullWhen(returnValue: true, nameof(CommServer))]
        private bool CommRunning => (CommServer is not null);

        private readonly ILoggerFactory _logger_factory = LoggerFactory.Create(builder => {
            builder
                .AddAiLogger()
                .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        private readonly ILogger _logger;

        public ServerWindow() {
            _logger = _logger_factory.CreateLogger("ServerWindow");

            InitializeComponent();
        }

        private void UpdateImages() {
            UploadFileImage.Source = UploadFile.IsEnabled ? upload_image : upload_disabled_image;
            StopImage.Source = Stop.IsEnabled ? stop_image : stop_disabled_image;

            if (CommRunning && CommServer.State == PlayingState.Playing) {
                PlayPauseImage.Source = PlayPause.IsEnabled ? pause_image : pause_disabled_image;
            } else {
                PlayPauseImage.Source = PlayPause.IsEnabled ? play_image : play_disabled_image;
            }
        }

        private void LockUI(bool locked) {
            bool new_state = !locked;

            UploadFile.IsEnabled = new_state;
            PlayPause.IsEnabled = new_state;
            Stop.IsEnabled = new_state;
            StartServer.IsEnabled = new_state;
            StopServer.IsEnabled = new_state;
            CommPort.IsEnabled = new_state;
            DataPort.IsEnabled = new_state;
        }

        private void SetPreStart() {
            LockUI(true);
            CommPort.IsEnabled = true;
            DataPort.IsEnabled = true;
        }

        private void SetStarted() {
            LockUI(true);
            UploadFile.IsEnabled = true;
            StopServer.IsEnabled = true;

        }

        private void SetHasFile() {
            LockUI(true);
            PlayPause.IsEnabled = true;
            Stop.IsEnabled = true;
            StopServer.IsEnabled = true;
        }

        private void SetStatus(string msg) {
            Status.Text = msg;
        }

        private void SetCurrentPos(long ms) {
            if (CommServer is null) {
                return;
            }

            CurrentPos.Text = AiSync.Utils.FormatTime(
                ms,
                always_hours: (CommServer.Duration) >= (3600 * 1000),
                ms_prec: 3);
        }

        private void ValidateServerParams() {
            if (!IsLoaded) {
                return;
            }

            bool comm_good = CommPort.TryParseText(out uint comm_port) && comm_port > 1023 && comm_port <= 65353;
            bool data_good = DataPort.TryParseText(out uint data_port) && data_port > 1023 && data_port <= 63353;

            if (comm_good && data_good && comm_port == data_port) {
                comm_good = false;
                data_good = false;
            }

            CommPortText.Foreground = comm_good ? Brushes.Black : Brushes.Red;
            DataPortText.Foreground = data_good ? Brushes.Black : Brushes.Red;

            StartServer.IsEnabled = (comm_good && data_good);

            if (comm_good) {
                Settings.Default.CommPort = (ushort)comm_port;
            }

            if (data_good) {
                Settings.Default.DataPort = (ushort)data_port;
            }

            if (comm_good || data_good) {
                Settings.Default.Save();
            }
        }

        private void UpdateClientCount() {
            if (CommRunning) {
                ClientsConnected.Text = CommServer.ClientCount.ToString();
            }
        }
        
        private void ResetFile() {
            /* Close any media, remove current file, notify clients */
            if (!CommRunning) {
                return;
            }

            ResetUiFields();
            SetStarted();
            UpdateImages();
        }

        private void ResetUiFields() {
            LockUI(true);

            FileSelected.Text = "(none)";
            FileMime.Text = "(none)";
            Duration.Text = "--:--";
            CurrentPos.Text = "--:--";
        }
    }
}
