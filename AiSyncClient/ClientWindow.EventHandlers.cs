using AiSync;

using LibVLCSharp.WPF;
using LibVLCSharp.Shared;

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Text.RegularExpressions;
using System.Net;
using System.Windows.Controls.Primitives;
using System.Reflection;
using Microsoft.Extensions.Logging;
using AiSyncClient.Properties;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Shared.Structures;
using System.Collections.Generic;
using System.Threading;

namespace AiSyncClient {
    public partial class ClientWindow {
        #region Disposal
        private void ClientWindow_Closed(object? sender, EventArgs e) {
            /* Also disposes the Media instance */
            Player?.Dispose();
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

            Address.Text = Settings.Default.Address;
            CommPort.Text = Settings.Default.CommPort.ToString();
            DataPort.Text = Settings.Default.DataPort.ToString();
            Volume.Value = Settings.Default.Volume;

            SetPreConnect();
            ValidateConnectionParams();
            UpdateImages();
        }

        private class MediaTrackMenuItem : MenuItem {
            public TrackType Type { get; set; }
            public int TrackID { get; set; }
        }

        private void Video_Loaded(object sender, RoutedEventArgs e) {
            Scrubber.Formatter = AutoToolTipFormatter;
            FullscreenScrubber.Formatter = AutoToolTipFormatter;

            Core.Initialize();

            Player = new MediaPlayer(VLC);

            Player.EndReached += (_, _) => Dispatcher.Invoke(() => {
                _logger.LogInformation("End reached");
            });

            Player.PositionChanged += (_, _) => Dispatcher.Invoke(Player_PositionChanged);
            Player.Playing += (_, _) => Dispatcher.Invoke(UpdateImages);
            Player.Paused += (_, _) => Dispatcher.Invoke(UpdateImages);
            Player.Stopped += (_, _) => Dispatcher.Invoke(() => {
                _logger.LogInformation("Playback stopped: {}", _comm_client?.IsConnected ?? false);
                UpdateImages();
            });

            Player.Playing += SelectDefaultTracks;

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

        private void SelectDefaultTracks(object? sender, EventArgs e) {
            if (!HasMedia) {
                return;
            }

            _logger.LogDebug("Default tracks: {} {} {}", Player.AudioTrack, Player.VideoTrack, Player.Spu);

            Dispatcher.Invoke(() => {
                /* Select currently selected tracks */
                foreach (((int id, TrackType type), MediaTrackMenuItem item) in menu) {
                    if (item.IsChecked) {
                        switch (type) {
                            case TrackType.Audio: Player.SetAudioTrack(id); break;
                            case TrackType.Video: Player.SetVideoTrack(id); break;
                            case TrackType.Text:  Player.SetSpu(id); break;
                        }
                    }
                }
            });

            /* Disconnect self */
            Player.Playing -= SelectDefaultTracks;
        }

        private void ImageButton_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) {
            UpdateImages();
        }

        private void Disconnect_Click(object sender, RoutedEventArgs e) {
            DisconnectServer();
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

            CommClient.GotFile += (_, _) => Dispatcher.Invoke(() => {
                _logger.LogInformation("Got file");
                LoadMedia();
            });
            CommClient.Connected += (_, _) => Dispatcher.Invoke(() => {
                _logger.LogInformation("Connected to server");
                SetDefaulPlayback();
            });
            CommClient.Disconnected += (_, _) => Dispatcher.Invoke(() => {
                _logger.LogInformation("Disconnected from server");
                DisconnectServer();
            });
            CommClient.CloseFile += (_, _) => Dispatcher.Invoke(() => {
                _logger.LogInformation("File closed");
                CloseMedia();
            });

            CommClient.PausePlay += (_, e) => Dispatcher.Invoke(() => {
                _logger.LogInformation("{} at {}", e.IsPlaying ? "Playing" : "Pausing", GetTimeString(e.Position));
                CommClient_PausePlay(e);
            });

            CommClient.Seek += (_, e) => Dispatcher.Invoke(() => {
                if (HasMedia) {
                    _logger.LogInformation("Seek to {}", GetTimeString(e.Target));
                    LastAction.Text = $"Seek to {GetTimeString(e.Target)}";

                    Player.SeekTo(TimeSpan.FromMilliseconds(e.Target));
                    Scrubber.IsEnabled = true;
                    FullscreenScrubber.IsEnabled = true;
                    remote_scrubbing = false;
                }
            });

            CommClient.UpdateStatus += (_, _) => Dispatcher.Invoke(() => {
                _logger.LogInformation("Requesting status");
                if (HasMedia) {
                    CommClient.SetStatus(Player.IsPlaying ? PlayingState.Playing : PlayingState.Paused, Player.PositionMs());
                } else {
                    CommClient.SetStatus(PlayingState.Stopped, 0);
                }
            });

            bool connected = await CommClient.Connect();

            if (connected) {
                LastAction.Text = "Connected to server";
                SetNoMedia();
            } else {
                LastAction.Text = "Failed to connect to server";
                SetPreConnect();
                ValidateConnectionParams();
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
            if (!HasMedia) {
                return;
            }

            SetPlaying(!Player.IsPlaying);
        }

        private void Scrubber_DragStarted(object sender, DragStartedEventArgs e) {
            scrubbing = true;

        }

        private void Scrubber_DragCompleted(object sender, DragCompletedEventArgs e) {
            if (!HasMedia) {
                scrubbing = false;
                return;
            }

            remote_scrubbing = true;
            long new_pos = (long)Math.Round(((AiSlider)sender).Value * Media.Duration);
            Scrub(new_pos);

            scrubbing = false;
        }

        private void Scrubber_DirectSeek(object sender, MouseEventArgs e) {
            /* Prevent double moves */
            if (remote_scrubbing) {
                return;
            }

            if (!HasMedia) {
                return;
            }

            remote_scrubbing = true;
            long new_pos = (long)Math.Round(((AiSlider)sender).Value * Media.Duration);
            Scrub(new_pos);
        }

        private void VideoInteraction_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ClickCount == 2) {
                SetFullscreen(!fullscreen);
            }
        }

        private void Volume_PreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            Volume.Value += Volume.SmallChange * (e.Delta < 0 ? -1 : 1);
        }

        private void CommClient_PausePlay(PausePlayEventArgs e) {
            if (!HasMedia) {
                /* What, how */
                return;
            }

            /* These are kept disabled for the first playback, so clients don't seek prematurely */
            if (!Scrubber.IsEnabled) {
                Scrubber.IsEnabled = true;
            }

            if (!FullscreenScrubber.IsEnabled) {
                FullscreenScrubber.IsEnabled = true;
            }

            /* Adjust position to sync with other clients if we're too far out of sync */
            long diff = Player.PositionMs().Difference(e.Position);
            TimeSpan? adjust = (diff > CommClient.CloseEnoughValue)
                ? TimeSpan.FromMilliseconds(e.Position) : null;

            if (e.IsPlaying) {
                /* Set to playing */
                Player.Play();

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
            } else {
                /* Set to paused */
                Player.Pause();

                /*
                if (adjust is not null) {
                    if (e.Position > Player.PositionMs()) {
                        LastAction.Text = $"Server pause and seek forward by {diff} ms";
                    } else {
                        LastAction.Text = $"Server pause and seek backward by {diff} ms";
                    }

                    Player.SeekTo(adjust.Value);
                } else {
                    LastAction.Text = $"Server pause (Δ={diff} ms)";
                }*/
            }

            PlayPause.IsEnabled = true;
            FullscreenPlayPause.IsEnabled = true;
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

        private void Player_PositionChanged() {
            UpdateCurrentPosition();

            if (scrubbing || remote_scrubbing) {
                /* Ignore */
                return;
            }

            /* No syncing for now */
            return;

            Task.Run(() => {
                AiServerStatus? status = CommClient.GetStatus();

                if (status is null) {
                    _logger.LogInformation("Pausing (unreachable server)");
                    Player.Pause();
                    return;
                }

                if (status.State == PlayingState.Stopped) {
                    _logger.LogInformation("Server stopped, stopping too");
                    return;
                }

                long delta = status.Position.Difference(Player.PositionMs());
                if (delta > CommClient.CloseEnoughValue) {
                    _logger.LogDebug("Resync, delta = {}", delta);
                    CommClient.PauseResync();
                }
            });
        }

        private void MenuItem_Click(object sender, RoutedEventArgs e) {
            MediaTrackMenuItem new_item = (MediaTrackMenuItem)sender;

            _logger.LogInformation("Set {} stream to {}", new_item.Type, new_item.TrackID);

            if (HasMedia) {
                /* Unckeck all other items and check the new item */
                foreach (MediaTrackMenuItem item in MenuItemsFor(new_item.Type)) {
                    item.IsChecked = false;
                }

                switch (new_item.Type) {
                    case TrackType.Audio: {
                        Player.SetAudioTrack(new_item.TrackID);
                        break;
                    }

                    case TrackType.Video: {
                        Player.SetVideoTrack(new_item.TrackID);
                        break;
                    }

                    case TrackType.Text: {
                        Player.SetSpu(new_item.TrackID);
                        break;
                    }
                }

                new_item.IsChecked = true;
            }

            e.Handled = true;
        }

        private void Player_FirstPlay(object? sender, EventArgs e) {
            Player.Playing -= Player_FirstPlay;
            Player.Pause();

            if (menu.Count != 0 || !HasMedia) {
                return;
            }

            _logger.LogInformation("First play, constructing context menus");
            Dispatcher.Invoke(() => {
                /* All streams have been added */
                IEnumerable<(TrackType type, TrackDescription desc)> descriptions =
                            Enumerable.Repeat(TrackType.Audio, Player.AudioTrackCount).Zip(Player.AudioTrackDescription)
                    .Concat(Enumerable.Repeat(TrackType.Video, Player.VideoTrackCount).Zip(Player.VideoTrackDescription))
                    .Concat(Enumerable.Repeat(TrackType.Text, Player.SpuCount).Zip(Player.SpuDescription));

                foreach ((TrackType type, TrackDescription desc) in descriptions) {
                    _logger.LogInformation("Track {}: {} ({})", desc.Id, desc.Name, type);

                    MediaTrackMenuItem item = new() {
                        IsCheckable = true,
                        Header = desc.Name,
                        Type = type,
                        TrackID = desc.Id,
                    };

                    item.Click += MenuItem_Click;

                    MenuItemsFor(type).Add(item);
                    menu.Add((desc.Id, type), item);
                }

                /* Prefer Japanese audio when present */
                foreach (MediaTrack track in Media.Tracks) {
                    if (track.TrackType == TrackType.Audio && track.Language == "jpn") {
                        Player.SetAudioTrack(track.Id);
                        break;
                    }
                }

                if (Player.AudioTrackCount > 0) {
                    menu[(Player.AudioTrack, TrackType.Audio)].IsChecked = true;
                }

                if (Player.VideoTrackCount > 0) {
                    menu[(Player.VideoTrack, TrackType.Video)].IsChecked = true;
                }

                if (Player.SpuCount > 0) {
                    menu[(Player.Spu, TrackType.Text)].IsChecked = true;
                }

                VideoContextMenu.IsEnabled = true;
            });
        }

        #endregion
    }
}
