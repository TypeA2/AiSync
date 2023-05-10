namespace AiSync {
    public record struct MessageResponse<Msg>(Guid Guid, Msg? Response) where Msg : AiProtocolMessage {
        public static implicit operator bool(MessageResponse<Msg> resp) => resp.Response is not null;
    }
}
