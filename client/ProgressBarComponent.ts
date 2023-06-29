class ProgressBarComponent extends HTMLElement {
    readonly inner: HTMLDivElement;
    
    constructor() {
        super();
        this.classList.add("progress");

        this.inner = document.createElement("div");
        this.inner.classList.add("progress-bar");

        this.appendChild(this.inner);
    }

    public show() {
        this.classList.remove("d-none");
    }

    public hide() {
        this.classList.add("d-none");
    }

    public set_text(text: string) {
        this.inner.innerHTML = text;
    }

    public clear_text() {
        this.inner.innerHTML = "";
    }

    public set_progress(ratio: number) {
        this.inner.style.width = Math.round(ratio * 100).toString() + "%";
        this.inner.innerHTML = Math.round(ratio * 100) + "%";
    }

    public set_bg(bg: null | "success" | "info" | "warning" | "danger") {
        if (bg === null) {
            this.inner.classList.remove("bg-success", "bg-info", "bg-warning", "bg-danger");
        } else {
            this.inner.classList.add(`bg-${bg}`);
        }
    }

    public striped(is_striped: boolean, animated: boolean = false) {
        if (is_striped) {
            this.inner.classList.add("progress-bar-striped", animated ? "progress-bar-animated" : "");
        } else {
            this.inner.classList.remove("progress-bar-striped", "progress-bar-animated");
        }
    }
}

customElements.define("progress-bar", ProgressBarComponent);
