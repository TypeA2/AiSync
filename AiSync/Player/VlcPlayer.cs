using System;
using System.Diagnostics;
using System.IO;

namespace AiSync {
    public class VlcPlayer : Player {

        private readonly string path;
        private readonly ushort port;

        private readonly Process process;

        public VlcPlayer(string path, ushort vlc_port) {
            if (!Path.Exists(path)) {
                throw new InvalidOperationException($"{path} is not a valid path");
            }

            this.path = path;
            port = vlc_port;

            Trace.WriteLine($"VLC at {path} with port {port}");

            string[] argv = new string[] {
                "--intf=http",
                $"--http-port={port}"
            };

            process = Process.Start(this.path, argv);
        }

        public override string Name => "VLC";

        public override void Dispose() {
            process.Kill();
            GC.SuppressFinalize(this);
        }

        public override void SetPlaying(bool playing) {
            throw new NotImplementedException();
        }

        public override void SetSource(string path) {
            throw new NotImplementedException();
        }
    }
}
