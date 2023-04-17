using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ai_sync {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>

    public partial class MainWindow : Window {
        private readonly ImageSource _play_icon = Utils.ExtractIcon("C:\\Windows\\System32\\mmcndmgr.dll", -30529);
        private readonly ImageSource _pause_icon = Utils.ExtractIcon("C:\\Windows\\System32\\mmcndmgr.dll", -30529);

        private readonly AiSync ai;

        public MainWindow(AiSync ai) {
            this.ai = ai;

            this.ai.PlaybackStarted += PlaybackStarted;
            this.ai.PlaybackStopped += PlaybackStopped;

            InitializeComponent();

            PlayPauseImage.Source = _play_icon;
        }

        public void SetAsClient() {

        }

        public void SetAsServer() {

        }

        private void PlaybackStopped(object? sender, EventArgs e) {
            PlayPauseImage.Source = _play_icon;
        }

        private void PlaybackStarted(object? sender, EventArgs e) {
            PlayPauseImage.Source = _pause_icon;
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            Slider volume = (Slider)sender;

            volume.Value = Math.Round(e.NewValue, 0);

            if (VolumeText != null) {
                VolumeText.Text = $"{volume.Value}%";
            }
        }

        private void Volume_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) {
            Volume.Value += Volume.SmallChange * (e.Delta < 0 ? -1 : 1);
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) {
            
        }

    }
}
