class AlertPanelComponent extends HTMLElement {
    constructor() {
        super();
        this.classList.add("alert");
        this.style.display = "block";
    }

    public show() {
        this.classList.remove("d-none");
    }

    public hide() {
        this.classList.add("d-none");
    }

    public set_type(type: "primary" | "secondary" | "success" | "danger" | "warning" | "info" | "light" | "dark") {
        this.classList.remove(
            "alert-primary", "alert-secondary", "alert-success",
            "alert-danger", "alert-warning", "alert-info", "alert-light", "alert-dark"
        );
        
        this.classList.add(`alert-${type}`);
    }

    public set_text(text: string) {
        this.innerText = text;
    }

    public set_html(html: string) {
        this.innerHTML = html;
    }
}

customElements.define("alert-panel", AlertPanelComponent);
