import "./scss/admin.scss";
import "bootstrap";

import axios from "axios";

import { id } from "./common";

import "./ProgressBarComponent";
import "./AlertPanelComponent";

class AdminPanel {
    private upload_btn: HTMLButtonElement;
    private upload_input: HTMLInputElement;

    private play_btn: HTMLButtonElement;
    private stop_btn: HTMLButtonElement;

    private progress: ProgressBarComponent;
    private alert: AlertPanelComponent;

    constructor() {
        this.upload_btn = id("upload-btn");
        this.upload_input = id("upload-file");
        this.play_btn = id("play-btn");
        this.stop_btn = id("stop-btn");

        this.upload_btn.addEventListener("click", () => {
            this.upload_input.click();
        });

        this.upload_input.addEventListener("change", (e) => {
            e.preventDefault();
            this.new_input();
        });

        this.progress = id("progress");
        this.alert = id("alert");
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

        const form = new FormData();
        form.append("file", file);
        const result = await axios.post("/admin", form, {
            timeout: 0,
            onUploadProgress: (e) => {
                this.progress.set_progress(e.progress || 0);

                if (e.progress === 1) {
                    this.progress.striped(true, true);
                    this.progress.set_text("Processing");
                }
            },
            validateStatus: () => true,
        });

        console.log(result);
        console.log(result.data.file.uuid);

        this.progress.striped(false, false);
        this.progress.clear_text();

        if (result.statusText !== "OK" || !result.data.success) {
            this.progress.hide();
            this.alert.set_type("danger");
            this.alert.set_html(`Error: ${result.status} - ${result.statusText}<hr>${result.data.message}`);
            this.alert.show();
            this.upload_btn.disabled = false;
        } else {
            this.progress.hide();
        }
    }
}

const panel = new AdminPanel();
