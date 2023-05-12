using AiSync;

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;
using AiSyncServer.Properties;
using WatsonTcp;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Threading.Tasks;

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
                .SetMinimumLevel(LogLevel.Debug);
        });

        private readonly ILogger _logger;

        private readonly Dictionary<Guid, ClientListItem> _clients = new();

        public ObservableCollection<ClientListItem> ClientListSource { get; } = new();

        private DateTime _last_client_update = DateTime.UtcNow;

        public sealed class ClientListItem : INotifyPropertyChanged {
            public ClientMetadata Client { get; }

            public string Address { get; }

            private double? _ping = null;
            public string Ping => (_ping is null) ? "unknown" : ((int)Math.Round(_ping.Value)).ToString();

            private PlayingState _state = PlayingState.Stopped;
            public PlayingState State {
                get {
                    return _state;
                }

                set {
                    _state = value;
                    OnPropertyChanged(nameof(State));
                }
            }

            private long _position = 0;

            public string Position => AiSync.Utils.FormatTime(_position);

            public long Delta { get; private set; }

            public DateTime LastUpdate { get; set; } = DateTime.UtcNow;

            public event PropertyChangedEventHandler? PropertyChanged;

            public ClientListItem(ClientMetadata client) {
                Client = client;
                Address = Client.IpPort;
            }

            public void SetPing(double? ping) {
                _ping = ping;
                OnPropertyChanged(nameof(Ping));
            }

            public void SetPosition(long position) {
                _position = position;
                OnPropertyChanged(nameof(Position));
            }

            public void SetDelta(long delta) {
                Delta = delta;
                OnPropertyChanged(nameof(Delta));
            }

            private void OnPropertyChanged(string property_name) {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property_name));
            }
        }

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

        private void UpdateClients() {
            if (!CommRunning) {
                return;
            }

            //TimeSpan elapsed = DateTime.UtcNow - _last_client_update;
            
            const int update_delay = 500;

            foreach (ClientListItem item in ClientListSource) {
                if ((DateTime.UtcNow - item.LastUpdate).TotalMilliseconds > update_delay) {
                    Task.Run(() => {
                        CommServer.UpdateClient(item.Client.Guid);
                    });
                }
            }
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

        private void ClientConnected(ClientMetadata client) {
            if (!CommRunning) {
                return;
            }

            ClientsConnected.Text = CommServer.ClientCount.ToString();

            ClientListItem item = new(client);

            _clients.Add(client.Guid, item);
            ClientListSource.Add(item);
        }

        private void ClientDisconnected(ClientMetadata client) {
            if (!CommRunning) {
                return;
            }

            ClientsConnected.Text = CommServer.ClientCount.ToString();

            ClientListSource.Remove(_clients[client.Guid]);
            _clients.Remove(client.Guid);
        }

        private void ClientUpdated(ClientUpdateEvent e) {
            if (!CommRunning) {
                return;
            }


            ClientListItem item = _clients[e.Guid];

            if (e.Position is not null) {
                TimeSpan elapsed = DateTime.UtcNow - e.Timestamp;

                long extra_ms = (long)Math.Round(elapsed.TotalMilliseconds);

                _logger.LogDebug("Updated {}: {}", e.Guid, e.Position.Value + extra_ms);

                item.SetPosition(e.Position.Value + extra_ms);
                item.SetDelta((e.Position.Value + extra_ms) - CommServer.Position);
            }

            if (e.Ping is not null) {
                item.SetPing((e.Ping.Value < 0) ? null : e.Ping.Value);
            }

            if (e.State is not null) {
                item.State = e.State.Value;
            }

            item.LastUpdate = DateTime.UtcNow;
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
