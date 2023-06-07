import "./scss/admin.scss";
import "bootstrap";

import { id } from "./common";

class AdminPanel {
    upload_btn: HTMLButtonElement;
    upload_input: HTMLInputElement;

    play_btn: HTMLButtonElement;
    stop_btn: HTMLButtonElement;

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
    }

    private async new_input() {
        if (this.upload_input.files?.length !== 1) {
            return;
        }

        console.log(this.upload_input.files[0]);
        
        //const data = new FormData();
        //data.append("file", this.upload_input.files[0]);

        //const response = await fetch("/admin", {
        //    method: "post",
        //    body: data,
        //});

        //console.log(await response.json());
    }
}

const panel = new AdminPanel();
