import "./scss/watch.scss";

import * as bootstrap from "bootstrap";
import videojs from "video.js";
import { Player } from "./player_stub";
import "video.js/dist/video-js.css"
import * as Ai from "../shared/api";
import log from "../shared/logging";


declare module "./player_stub" {
    interface Player {
        textTrackSettings: {
            setValues: (options: Record<string, string>) => void;
        };
    }
}

class AiPlayer {
    player!: Player;
    ws!: WebSocket;

    bock_events: boolean = false;

    private async setup() {
        document.getElementById("player")?.classList.remove("d-none");
        this.player = videojs("player", {
            persistTextTrackSettings: false,
        });

        this.player.on("ready", () => {
            const settings  = this.player.textTrackSettings;
            settings.setValues({
                "backgroundOpacity": "0",
                "edgeStyle": "uniform"
            });
        });

        this.player.on("play", () => this.request_play());
        this.player.on("pause", () => this.request_pause());
        this.player.on("seeked", () => this.request_seek());

        this.player.on("play", () => this.report_progress());
        this.player.on("pause", () => this.report_progress());
        this.player.on("seeked", () => this.report_progress());
        this.player.on("timeupdate", () => this.report_progress());
    }

    constructor() {
        this.setup().then(() => {
            const protocol = (window.location.protocol === "https:") ? "wss:" : "ws:";
            this.ws = new WebSocket(`${protocol}//${window.location.host}/ws_watch`);

            this.ws.addEventListener("error", console.error);
            this.ws.addEventListener("close", e => log.log(`Closed: ${e.reason}`));
            this.ws.addEventListener("message", e => this.message(e));
        });
    }

    private request_play() {
        if (this.bock_events) {
            this.bock_events = false;
            return;
        }

        log.info("Play");
        const msg: Ai.Message = {
            msg: Ai.MessageID.ClientPlay,
        };

        this.ws.send(JSON.stringify(msg));
    }

    private request_pause() {
        if (this.bock_events) {
            this.bock_events = false;
            return;
        }

        log.info("Pause");
        const msg: Ai.Message = {
            msg: Ai.MessageID.ClientPause,
        };

        this.ws.send(JSON.stringify(msg));
    }

    private request_seek() {
        if (this.bock_events) {
            this.bock_events = false;
            return;
        }

        log.info("Seek", this.player.currentTime());
        const msg: Ai.Seek = {
            msg: Ai.MessageID.ClientSeek,
            position: this.player.currentTime(),
        };

        this.ws.send(JSON.stringify(msg));
    }

    private report_progress() {
        const msg: Ai.ClientStatus = {
            msg: Ai.MessageID.ClientStatus,
            sent: Date.now() / 1000,
            position: this.player.currentTime(),
            playing: !this.player.paused()
        };

        this.ws.send(JSON.stringify(msg));
    }

    private message(e: MessageEvent) {
        const msg: Ai.Message = JSON.parse(e.data);

        const unexpected = (msg: Ai.Message) => log.warn("Unexpected message received:", msg);

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const call_map: { [type in Ai.MessageID]: { (msg: any): void } } = {
            [Ai.MessageID.None]: (msg: Ai.Message) => log.warn("Unknown message received:", msg),
            [Ai.MessageID.NewFile]: this.new_file,
            [Ai.MessageID.FileRemoved]: this.unset_file,
            [Ai.MessageID.ClientsJoined]: this.clients_joined,
            [Ai.MessageID.ClientsLeft]: this.clients_left,

            [Ai.MessageID.ClientPause]: unexpected,
            [Ai.MessageID.ClientPlay]: unexpected,
            [Ai.MessageID.ClientSeek]: unexpected,

            [Ai.MessageID.RequestPause]: this.server_request_pause,
            [Ai.MessageID.RequestPlay]: this.server_request_play,
            [Ai.MessageID.RequestSeek]: this.server_request_seek,

            [Ai.MessageID.ClientStatus]: unexpected,
            [Ai.MessageID.ClientStatusUpdate]: unexpected,
        };

        call_map[msg.msg].call(this, msg);
    }

    private new_file(msg: Ai.NewFile) {
        log.info("Got new file", msg);

        this.player.src({
            type: "video/mp4", // msg.media_mime, fake it because it usually just works lol,
            src: `${window.location.origin}/file/${msg.uuid}/media`
        });

        for (const stream of msg.subtitle) {
            this.player.addRemoteTextTrack({
                src: `${window.location.origin}/file/${msg.uuid}/${stream.index}`,
                srclang: stream.lang,
                label: stream.title,
                default: stream.default,
            }, false);
        }
    }

    private unset_file(msg: Ai.Message) {
        log.info("Unsetting file", msg);

        this.player.reset();
    }

    private clients_joined(msg: Ai.ClientsJoined) {
        log.info("Clients joined", msg);
    }

    private clients_left(msg: Ai.ClientsLeft) {
        log.info("Clients left", msg);
    }

    private server_request_pause(msg: Ai.Message) {
        log.info("Pause requested", msg);
        this.bock_events = true;
        this.player.pause();
    }

    private server_request_play(msg: Ai.Message) {
        log.info("Play requested", msg);
        this.bock_events = true;
        this.player.play();
    }

    private server_request_seek(msg: Ai.Seek) {
        log.info("Seek requested", msg);
        this.bock_events = true;
        this.player.currentTime(msg.position);
    }
}

const modal_el = document.getElementById("autoplay-modal") as HTMLDivElement;

modal_el.addEventListener("hidden.bs.modal", _ => {
    log.info("Modal hidden, instantiating player");
    const _player = new AiPlayer();
});

const modal = new bootstrap.Modal(modal_el);
modal.show();
