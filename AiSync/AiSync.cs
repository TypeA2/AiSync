using System;
using System.Diagnostics;
using System.IO;
using System.Net;

namespace ai_sync {
    public sealed class AiSync : IDisposable {
        public event EventHandler? PlaybackStarted;
        public event EventHandler? PlaybackStopped;

        private readonly string vlc;

        bool started = false;

        public AiSync(string vlc) {
            this.vlc = vlc;

            if (!Path.Exists(vlc)) {
                throw new InvalidOperationException($"{vlc} is not a valid path");
            }

            Trace.WriteLine($"VLC at {vlc}");
        }

        private bool disposed = false;
        private void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {

                }

                 

                disposed = true;
            }
        }

        ~AiSync() {
            Dispose(disposing: false);
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public void StartClient(IPAddress addr, ushort port) {
            if (started) {
                throw new InvalidOperationException("Already started");
            }

            started = true;

            Trace.WriteLine($"Client to {addr}:{port}");
        }

        public void StartServer(IPAddress addr, ushort port) {
            if (started) {
                throw new InvalidOperationException("Already started");
            }

            started = true;

            Trace.WriteLine($"Server on {addr}:{port}");
        }
    }
}
