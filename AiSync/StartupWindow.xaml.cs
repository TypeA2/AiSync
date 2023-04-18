using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AiSync {
    /// <summary>
    /// Interaction logic for StartupWindow.xaml
    /// </summary>
    public partial class StartupWindow : Window {
        private Brush? ip_brush;
        private Brush? port_brush;
        private Brush? vlc_port_brush;

        public StartupWindow() {
            InitializeComponent();

            Loaded += (_, _) => {
                this.SetSysMenu(false);

                if (Settings.Default.LastMode == "server") {
                    ServerButton.Focus();
                    HostIP.Text = Settings.Default.ServerHost;
                    HostPort.Text = Settings.Default.ServerPort;
                } else {
                    ClientButton.Focus();
                    HostIP.Text = Settings.Default.ClientHost;
                    HostPort.Text = Settings.Default.ClientPort;
                }

                VlcIntfPort.Text = Settings.Default.VlcPort;

                ip_brush = HostIP.BorderBrush;
                port_brush = HostIP.BorderBrush;
                vlc_port_brush = VlcIntfPort.BorderBrush;
            };
        }

        public enum RunType {
            None,
            Client,
            Server
        }

        public RunType RunAs { get; private set; } = RunType.None;
        public IPAddress IP { get; private set; } = IPAddress.Loopback;
        public ushort Port { get; private set; } = 0;

        public ushort VlcPort { get; private set; } = 0;

        private bool ValidateConnection(out bool ip_valid, out bool port_valid, out bool vlc_port_valid) {
            ip_valid = false;
            port_valid = false;
            vlc_port_valid = false;

            if (IPAddress.TryParse(HostIP.Text, out IPAddress? ip)) {
                IP = ip;
                ip_valid = true;
            } else if (String.Compare(HostIP.Text, "localhost", ignoreCase: true) == 0) {
                IP = IPAddress.Loopback;
                ip_valid = true;
            }

            if (UInt16.TryParse(HostPort.Text, out ushort port) && port > 1024) {
                Port = port;
                port_valid = true;
            }

            if (UInt16.TryParse(VlcIntfPort.Text, out ushort vlc_port) && vlc_port > 1024) {
                VlcPort = vlc_port;
                vlc_port_valid = true;
            }

            return ip_valid && port_valid && vlc_port_valid;
        }

        private void ButtonClicked(object sender, RoutedEventArgs e) {
            Button btn = (Button)sender;

            if (!ValidateConnection(out bool ip_valid, out bool port_valid, out bool vlc_port_valid)) {
                HostIP.BorderBrush      = ip_valid       ? ip_brush       : Brushes.Red;
                HostPort.BorderBrush    = port_valid     ? port_brush     : Brushes.Red;
                VlcIntfPort.BorderBrush = vlc_port_valid ? vlc_port_brush : Brushes.Red;

                return;
            }

            if (btn.Name == "ClientButton") {
                /* Open as client */
                RunAs = RunType.Client;

                Settings.Default.ClientHost = HostIP.Text;
                Settings.Default.ClientPort = HostPort.Text;

                Settings.Default.LastMode = "client";
            } else {
                /* Open as server */
                RunAs = RunType.Server;

                Settings.Default.ServerHost = HostIP.Text;
                Settings.Default.ServerPort = HostPort.Text;

                Settings.Default.LastMode = "server";
            }

            Settings.Default.VlcPort = VlcIntfPort.Text;

            Settings.Default.Save();
            
            Close();
        }
    }
}
