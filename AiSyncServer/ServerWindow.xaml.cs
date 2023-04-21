using AiSync;

using LibVLCSharp.Shared;

using HeyRed.Mime;

using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

using Microsoft.Extensions.Logging;

namespace AiSyncServer {
    public partial class ServerWindow : Window {
        private static ImageSource ReadImage(string url) => new BitmapImage(new Uri($"/AiSyncServer;component/{url}", UriKind.Relative));

        private static readonly ImageSource upload_image = ReadImage("icons8-upload.png");
        private static readonly ImageSource upload_disabled_image = ReadImage("icons8-upload-disabled.png");

        private static readonly ImageSource stop_image = ReadImage("icons8-stop.png");
        private static readonly ImageSource stop_disabled_image = ReadImage("icons8-stop-disabled.png");

        private Media? _media;
        private Media Media {
            get => _media ?? throw new InvalidOperationException("No media is opened");
            set => _media = value;
        }

        private LibVLC? _vlc;
        private LibVLC VLC { get => _vlc ??= new(); }

        private AiServer? _comm_server;
        private AiServer CommServer { 
            get => _comm_server ?? throw new InvalidOperationException("No comm server instance exists");
            set => _comm_server = value;
        }

        private AiFileServer? _data_server;
        private AiFileServer DataServer {
            get => _data_server ?? throw new InvalidOperationException("No data server instance exists");
            set => _data_server = value;
        }

        private readonly ILoggerFactory _logger_factory = LoggerFactory.Create(builder => {
            builder
                .AddAiLogger()
                .SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        public ServerWindow() {
            InitializeComponent();

            Loaded += Window_Loaded;

            Closed += ServerWindow_Closed;

            UploadFile.IsEnabledChanged += (_, _) =>
                UploadFileImage.Source = UploadFile.IsEnabled ? upload_image : upload_disabled_image;

            Stop.IsEnabledChanged += (_, _) =>
                StopImage.Source = Stop.IsEnabled ? stop_image : stop_disabled_image;
        }

        private void ServerWindow_Closed(object? sender, EventArgs e) {
            _media?.Dispose();
            _vlc?.Dispose();
            _comm_server?.Dispose();
            _data_server?.Dispose();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            Core.Initialize();

            _vlc ??= new();

            LockUI(true);
            Start.IsEnabled = true;
            CommPort.IsEnabled = true;
            DataPort.IsEnabled = true;
        }

        private void LockUI(bool locked) {
            bool new_state = !locked;

            UploadFile.IsEnabled = new_state;
            Stop.IsEnabled = new_state;
            Start.IsEnabled = new_state;
            DisconnectAll.IsEnabled = new_state;
            CommPort.IsEnabled = new_state;
            DataPort.IsEnabled = new_state;
        }

        private void ResetProgressRangePercent(double max) {
            Progress.Maximum = max;
            Progress.Value = 0;
            ProgressText.Text = "0.0 %";
        }

        private void ResetProgressRangeInteger(long max) {
            Progress.Maximum = max;
            Progress.Value = 0;
            ProgressText.Text = $"0 / {max}";
        }

        private void ClearProgressRange() {
            Progress.Value = 0;
            ProgressText.Text = String.Empty;
        }

        private void UpdateProgressPercent(double new_val) {
            double percent = new_val / Progress.Maximum * 100;

            Progress.Value = new_val;
            ProgressText.Text = String.Format("{0:0.0}%", percent);
        }

        private void UpdateProgressInteger(long new_val, long max_val) {
            Progress.Value = new_val;
            ProgressText.Text = $"{new_val} / {max_val}";
        }

        private void SetStatus(string msg) {
            Status.Text = msg;
        }

        private void SetCurrentPos(long ms) {
            CurrentPos.Text = AiSync.Utils.FormatTime(ms, always_hours: Media.Duration >= (3600 * 1000), ms_prec: 3);
        }

        private void Port_TextChanged(object sender, TextChangedEventArgs e) {
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

            Start.IsEnabled = (comm_good && data_good);
        }

        [GeneratedRegex("[^0-9]+")]
        private static partial Regex NumbersRegex();

        private void Port_PreviewTextInput(object sender, TextCompositionEventArgs e) {
            e.Handled = NumbersRegex().IsMatch(e.Text);
        }

        private async void UploadFile_Click(object sender, RoutedEventArgs e) {
            if (Utils.GetFile(out string file)) {
                FileInfo info = new(file);
                string mime = MimeTypesMap.GetMimeType(info.Extension);

                if (!mime.StartsWith("video")) {
                    /* Not a video, ignore */
                    MessageBox.Show($"Invalid MIME type:\n\n{mime}", "AiSync Server");
                    return;
                }

                UploadFile.IsEnabled = false;

                Media = new Media(VLC, new Uri(info.FullName));

                await Media.Parse();

                FileSelected.Text = info.Name;
                FileMime.Text = mime;

                Duration.Text = AiSync.Utils.FormatTime(Media.Duration);
                SetCurrentPos(0);

                DataServer = new AiFileServer(IPAddress.Any, DataPort.ParseText<ushort>(), info.FullName, mime);
                CommServer.SetHasFile();

                Stop.IsEnabled = true;
            }
        }

        private void Stop_Click(object sender, RoutedEventArgs e) {
            /* Close any media, remove current file, notify clients */
            Stop.IsEnabled = false;

            _media?.Dispose();

            _media = null;

            FileSelected.Text = "(none)";
            FileMime.Text = "(none)";
            Duration.Text = "--:--";
            CurrentPos.Text = "--:--";

            /* TODO notify clients */
            CommServer.StopPlayback();

            DataServer.Dispose();

            UploadFile.IsEnabled = true;
        }

        private void Start_Click(object sender, RoutedEventArgs e) {
            LockUI(true);
            CommServer = new AiServer(_logger_factory, IPAddress.Any, CommPort.ParseText<ushort>());
            CommServer.ServerStarted += ServerStarted;
            CommServer.ClientConnected += (_, _) => Dispatcher.Invoke(ClientConnected);

            CommServer.PlayingChanged += (_, e) => Dispatcher.Invoke(
                () => Playing.Text = e.IsPlaying ? "true" : "false");

            CommServer.PositionChanged += (_, e) =>
                Dispatcher.Invoke(() => SetCurrentPos(e.Position));

            CommServer.Start();

            ServerStarted(null, EventArgs.Empty);
        }

        private void ServerStarted(object? sender, EventArgs e) {
            Title = $"AiSync Server - Active";

            SetStatus("Idle");

            UploadFile.IsEnabled = true;
        }

        private void ClientConnected() {
            ClientsConnected.Text = CommServer.ClientCount.ToString();
        }
    }
}
