using Microsoft.Extensions.Logging;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace AiSyncServer {
    internal sealed class PositionChangedEvent : EventArgs {

        public bool IsSeek { get; }
        public long Position { get; set; }

        public PositionChangedEvent() : this(false, 0) { }

        public PositionChangedEvent(bool is_seek, long position) : base() {
            IsSeek = is_seek;
            Position = position;
        }
    }

    internal class PlayingChangedEventArgs : EventArgs {
        public AiSync.PlayingState State { get; }

        public PlayingChangedEventArgs(AiSync.PlayingState new_state) : base() {
            State = new_state;
        }
    }

    internal sealed class AiPlaybackTimer : IDisposable {
        private bool _disposed = false;

        private readonly ILogger _logger;
        private readonly Random _rng = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly long _duration;
        private readonly int _min_interval;
        private readonly int _max_interval;
        private readonly Thread _thread;
        private readonly object _state_lock = new();

        /* Timestamp of the latest play event, or null if not yet started */
        private DateTime? _prev_start;

        /* Logical position in ms of the latest play event */
        private long _start_pos;

        public event EventHandler<PositionChangedEvent>? PositionChanged;
        public event EventHandler<PlayingChangedEventArgs>? PlayingChanged;

        public AiSync.PlayingState State { get; private set; } = AiSync.PlayingState.Stopped;

        public long Position {
            get {
                DisposeCheck(_disposed);

                if (_prev_start is not null && State == AiSync.PlayingState.Playing) {
                    /* Playback has started, calculate */
                    TimeSpan elapsed = DateTime.UtcNow - _prev_start.Value;

                    return (long)Math.Round(elapsed.TotalMilliseconds) + _start_pos;
                }

                /* Not playing or not started yet, so always exactly this */
                return _start_pos;
            }

            private set {
                DisposeCheck(_disposed);

                _prev_start = DateTime.UtcNow;
                _start_pos = value;
            }
        }

        public AiPlaybackTimer(ILoggerFactory fact, long duration, int min_interval = 75, int max_interval = 100) {
            _logger = fact.CreateLogger("AiPlaybackTimer");
            _logger.LogInformation("Timer: {}", AiSync.Utils.FormatTime(duration));
            
            _duration = duration;
            _min_interval = min_interval;
            _max_interval = max_interval;

            _thread = new Thread(() => Poll(_cts.Token));
            _thread.Start();
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }

            _logger.LogInformation("Stopping timer");

            if (_thread.IsAlive) {
                _cts.Cancel();
                _thread.Join();
            }

            _cts.Dispose();

            lock (_state_lock) {
                State = AiSync.PlayingState.Stopped;
                Position = 0;
            }

            PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(AiSync.PlayingState.Stopped));

            _disposed = true;
        }

        private static void DisposeCheck([DoesNotReturnIf(true)] bool disposed) {
            if (disposed) {
                throw new ObjectDisposedException(nameof(AiPlaybackTimer));
            }
        }

        private void Poll(CancellationToken ct) {
            /* Absolute start time of the polling thread */
            DateTime start = DateTime.Now;

            PositionChangedEvent event_args = new(false, 0);

            while (!ct.IsCancellationRequested) {
                /* Wait for initial start */
                while (true) {
                    lock (_state_lock) {
                        if (_prev_start is not null) {
                            break;
                        }
                    }

                    Thread.Sleep(_rng.Next(_min_interval, _max_interval));

                    if (ct.IsCancellationRequested) {
                        return;
                    }
                }

                lock (_state_lock) {
                    /* Calculate new pos */
                    event_args.Position = Int64.Min(Position, _duration);

                    if (event_args.Position >= _duration) {
                        State = AiSync.PlayingState.Stopped;
                    }
                }

                if (State == AiSync.PlayingState.Stopped) {
                    _logger.LogInformation("Poller EOF: {} >= {}", event_args.Position, _duration);

                    PositionChanged?.Invoke(this, event_args);
                    PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(State));
                    return;
                } else {
                    PositionChanged?.Invoke(this, event_args);
                }

                Thread.Sleep(_rng.Next(_min_interval, _max_interval));
            }
        }

        /* Indicates a start at the given position */
        public void Play(long pos) {
            DisposeCheck(_disposed);

            lock (_state_lock) {
                Position = pos;
                State = AiSync.PlayingState.Playing;
            }

            _logger.LogInformation("Playing at {}", AiSync.Utils.FormatTime(pos));

            PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(AiSync.PlayingState.Playing));
        }

        /* Ditto for a pause */
        public void Pause(long pos) {
            DisposeCheck(_disposed);

            lock (_state_lock) {
                Position = pos;
                State = AiSync.PlayingState.Paused;
            }

            _logger.LogInformation("Pausing at {}", AiSync.Utils.FormatTime(pos));

            PlayingChanged?.Invoke(this, new PlayingChangedEventArgs(AiSync.PlayingState.Paused));
        }

        /* Seeking doesn't change Playing status but does change position */
        public void Seek(long pos) {
            DisposeCheck(_disposed);

            lock (_state_lock) {
                Position = pos;
            }

            _logger.LogInformation("Seeking to {}", AiSync.Utils.FormatTime(pos));

            PositionChanged?.Invoke(this, new PositionChangedEvent(true, pos));
        }
    }
}
