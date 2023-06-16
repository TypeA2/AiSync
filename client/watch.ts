import "./scss/watch.scss";

import videojs from "video.js";
import Player from "video.js/dist/types/player";
import "video.js/dist/video-js.css"
import Ai from "../shared/api";

class AiPlayer {
    player: Player;
    ws: WebSocket;

    constructor() {
        this.player = videojs("player", {});
        this.ws = new WebSocket(`ws://${window.location.host}/`);

        this.ws.addEventListener("error", console.error);
        this.ws.addEventListener("close", e => console.log(`Closed: ${e.reason}`));
        this.ws.addEventListener("message", e => this.message(e));
    }

    private message(e: MessageEvent) {
        const msg: Ai.Message = JSON.parse(e.data);

        const call_map: { [type in Ai.MessageID]: { (msg: any): void } } = {
            [Ai.MessageID.None]: (msg: Ai.Message) => console.log("Unknown message received:", msg),
            [Ai.MessageID.NewFile]: this.new_file,
        };

        call_map[msg.msg].call(this, msg);
    }

    private new_file(msg: Ai.NewFile) {
        console.log(this);
        console.log("Got new file", msg);

        const sources: { type: string; src: string; }[] = [];

        const streams = [
            ...Object.entries(msg.video),
            ...Object.entries(msg.audio),
            ...Object.entries(msg.subtitle),
        ];

        for (const [id, mime] of streams) {
            sources.push({
                type: mime,
                src: `${window.location.origin}/file/${msg.uuid}/${id}`
            });
        }

        this.player.src(sources);
    }
}

const player = new AiPlayer();
