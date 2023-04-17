namespace ai_sync.Player {
    public abstract class Player {
        public abstract string Name { get; }

        public abstract void SetSource(string path);

        public abstract void SetPlaying(bool playing);
    }
}
