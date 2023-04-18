using System;

namespace AiSync {
    public abstract class Player : IDisposable {

        public abstract string Name { get; }

        public abstract void SetSource(string path);

        public abstract void SetPlaying(bool playing);

        public abstract void Dispose();
    }
}
