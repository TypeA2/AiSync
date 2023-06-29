import "./scss/admin.scss";
import "bootstrap";

import axios from "axios";

import { id } from "./common";
import log from "../shared/logging";
import * as Ai from "../shared/api";
import "./ProgressBarComponent";
import "./AlertPanelComponent";
import "./ClientListComponent";
import ClientListComponent from "./ClientListComponent";

import "../shared/utils";


class AdminPanelComponent extends HTMLElement {
    private upload_btn: HTMLButtonElement;
    private upload_input: HTMLInputElement;

    private play_btn: HTMLButtonElement;
    private stop_btn: HTMLButtonElement;

    private progress: ProgressBarComponent;
    private alert: AlertPanelComponent;

    private client_count: HTMLTableCellElement;
    private filename: HTMLTableCellElement;
    private file_mime: HTMLTableCellElement;
    private duration: HTMLTableCellElement;
    private position: HTMLTableCellElement;

    private client_list: ClientListComponent;

    private ws: WebSocket;

    constructor() {
        super();
        
        const protocol = (window.location.protocol === "https:") ? "wss:" : "ws:";
        this.ws = new WebSocket(`${protocol}//${window.location.host}/ws_admin`);

        this.ws.addEventListener("error", console.error);
        this.ws.addEventListener("close", e => log.log(`Closed: ${e.reason}`));
        this.ws.addEventListener("message", e => this.message(e));

        this.upload_btn = id("upload-btn");
        this.upload_input = id("upload-file");
        this.play_btn = id("play-btn");
        this.stop_btn = id("stop-btn");

        this.client_count = id("client-count");
        this.filename = id("filename");
        this.file_mime = id("file-mime");
        this.duration = id("duration");
        this.position = id("position");

        this.upload_btn.addEventListener("click", () => {
            this.upload_input.click();
        });

        this.upload_input.addEventListener("change", (e) => {
            e.preventDefault();
            this.new_input();
        });

        this.stop_btn.addEventListener("click", _ => this.stop());

        this.progress = id("progress");
        this.alert = id("alert");

        this.client_list = id("client-list");
    }

    private async new_input() {
        if (this.upload_input.files?.length !== 1) {
            return;
        }

        const file = this.upload_input.files[0];

        this.alert.hide();
        this.progress.show();
        this.progress.set_bg(null);
        this.upload_btn.disabled = true;


        try {
            /* Start file */
            const file_info = new FormData();
            file_info.append("name", file.name);
            file_info.append("size", file.size.toString());
            const upload_params: Ai.FileCreated | Ai.FailureMessage = await fetch("/admin", {
                method: "POST",
                body: file_info,
            }).then(r => r.json());

            if (!upload_params.success) {
                throw new Error(`Failed to create file: ${upload_params.message}`);
            }

            log.info("Uploading with", upload_params);

            const { chunk_size } = upload_params;
            const chunks = Math.ceil(file.size / chunk_size);

            for (let i = 0; i < chunks; ++i) {

                const part = new FormData();
                part.append("id", upload_params.id);
                part.append("index", i.toString());
                part.append("blob", file.slice(i * chunk_size, Math.min((i + 1) * chunk_size, file.size)));

                const result: Ai.ResponseMessage = await fetch("/admin", {
                    method: "PATCH",
                    body: part,
                }).then(r => r.json());

                if (!result.success) {
                    throw new Error(`Failed to upload index ${i} (chunk size ${upload_params.chunk_size}) - ${result.message}`);
                }

                this.progress.set_progress((i + 1) / chunks);
            }

            this.progress.striped(true, true);
            this.progress.set_text("Processing");

            /* Finish upload with a PUT */
            const end_form = new FormData();
            end_form.append("id", upload_params.id);
            const response: Ai.ResponseMessage = await fetch("/admin", {
                method: "PUT",
                body: end_form,
            }).then(r => r.json());

            if (!response.success) {
                throw new Error(`Failed to finish uploading file - ${response.message}`);
            }

            this.progress.hide();
        } catch (e: any) {
            this.progress.hide();

            this.alert.set_type("danger");
            this.alert.set_html(`Error: ${e.message}`);
            this.alert.show();
            this.upload_btn.disabled = false;
        }
    }

    private async stop() {
        this.play_btn.disabled = true;
        this.stop_btn.disabled = true;
        
        axios.delete("/admin");
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

            [Ai.MessageID.RequestPause]: this.pause,
            [Ai.MessageID.RequestPlay]: this.play,
            [Ai.MessageID.RequestSeek]: this.seek,

            [Ai.MessageID.ClientStatus]: unexpected,
            [Ai.MessageID.ClientStatusUpdate]: this.update_status,
        };

        call_map[msg.msg].call(this, msg);
    }

    private new_file(msg: Ai.NewFile) {
        log.info("New file received", msg);

        this.upload_btn.disabled = true;
        this.play_btn.disabled = false;
        this.stop_btn.disabled = false;

        this.filename.innerText = msg.name;
        this.file_mime.innerText = msg.media_mime;
        this.duration.innerText = msg.duration.toTimeString();
        this.position.innerText = (0).toTimeString();
    }

    private unset_file(msg: Ai.Message) {
        log.info("Unsetting file", msg);
        this.filename.innerText = "(none)";
        this.file_mime.innerText = "(none)";
        this.duration.innerText = "--:--";
        this.position.innerText = "--:--";

        this.upload_btn.disabled = false;
        this.play_btn.disabled = true;
        this.stop_btn.disabled = true;
    }

    private clients_joined(msg: Ai.ClientsJoined) {
        log.info("Clients joined", msg);

        for (const client of msg.clients) {
            this.client_list.add({
                uuid: client.uuid,
                position: client.position,
                playing: client.playing,
                latency: client.latency
            });
        }
    }

    private clients_left(msg: Ai.ClientsLeft) {
        log.info("Clients left", msg);

        for (const client of msg.clients) {
            this.client_list.remove_client(client);
        }
    }

    private pause(msg: Ai.Message) {
        log.info("Pause requested", msg);
    }

    private play(msg: Ai.Message) {
        log.info("Play requested", msg);
    }

    private seek(msg: Ai.Seek) {
        log.info("Seek requested", msg);
    }

    private update_status(msg: Ai.ClientStatusUpdate) {
        log.info("Progress update", msg);

        this.client_list.update_client(msg.id, {
            position: msg.position,
            playing: msg.playing,
            latency: Math.abs((Date.now() / 1000) - msg.sent)
        });
    }
}

customElements.define("admin-panel", AdminPanelComponent);
