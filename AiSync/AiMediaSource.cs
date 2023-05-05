using LibVLCSharp.Shared;

namespace AiSync {
    public sealed class AiMediaSource : IDisposable {
        public sealed class ValidStateWrapper {
            public bool Valid { get; set; }

            public ValidStateWrapper(bool val) {
                Valid = val;
            }
        }

        public sealed class ParsedMedia {
            private readonly ValidStateWrapper _state;

            /* `Media` is disposed if valid is false */
            public bool Valid => _state.Valid;

            public Media Media { get; }

            public ParsedMedia(ValidStateWrapper state, Media media) {
                _state = state;
                Media = media;
            }

            private void ValidCheck() {
                if (!Valid) {
                    throw new ObjectDisposedException(nameof(ParsedMedia));
                }
            }

            /* Proxy methods */
            public long Duration {
                get {
                    ValidCheck();

                    return Media.Duration;
                }
            }
        }

        private static bool core_initialized = false;

        private readonly LibVLC vlc;
        private readonly List<(ValidStateWrapper state, ParsedMedia media)> media_list = new();

        private bool _disposed = false;

        public int Count => media_list.Count;

        public AiMediaSource() {
            if (!core_initialized) {
                Core.Initialize();
                core_initialized = true;
            }

            vlc = new LibVLC();
        }

        private void DisposedCheck() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(AiMediaSource));
            }
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;

            foreach ((ValidStateWrapper state, ParsedMedia media) in media_list) {
                state.Valid = false;
                media.Media.Dispose();
            }

            vlc.Dispose();
        }

        public Task<ParsedMedia> Load(string uri, params string[] opts) {
            return Load(new Uri(uri), opts);
        }

        public async Task<ParsedMedia> Load(Uri uri, params string[] opts) {
            Media media = new(vlc, uri, opts);

            MediaParsedStatus status = await media.Parse();

            if (status != MediaParsedStatus.Done) {
                throw new VLCException($"Media parsing failed: {status}");
            }

            ValidStateWrapper state = new(true);
            ParsedMedia parsed = new(state, media);

            media_list.Add((state, parsed));

            return parsed;
        }

        public void Unload(ParsedMedia? media) {
            for (int i = 0; i < media_list.Count; ++i) {
                if (media_list[i].media == media) {
                    media_list[i].state.Valid = false;
                    media_list[i].media.Media.Dispose();

                    media_list.RemoveAt(i);
                    return;
                }
            }

            throw new InvalidOperationException("Media not found in this source's list");
        }
    }
}
