import { Client } from "../shared/api";

import ClientListItemComponent from "./ClientListItemComponent";

class ClientListComponent extends HTMLElement {
    clients: { [uuid: string]: Client & { item: ClientListItemComponent } } = {};

    table: HTMLTableElement;

    tbody: HTMLElement;

    constructor() {
        super();

        this.classList.add("list-group");

        this.table = document.createElement("table");
        this.table.classList.add("table", "table-striped");

        const thead = document.createElement("thead");

        const id = document.createElement("th");
        id.innerText = "UUID";

        const pos = document.createElement("th");
        pos.innerText = "Position";

        const delta = document.createElement("th");
        delta.innerText = "Delta";

        const state = document.createElement("th");
        state.innerText = "State";

        const latency = document.createElement("th");
        latency.innerText = "Latency";

        const tr = document.createElement("tr");
        tr.append(id, pos, delta, state, latency);
        thead.append(tr);

        this.tbody = document.createElement("tbody");
        this.table.append(thead, this.tbody);

        this.append(this.table);
    }

    public add(client: Client) {
        if (client.uuid in this.clients) {
            return; /* No-op */
        }

        const item = new ClientListItemComponent(client);
        this.tbody.appendChild(item);

        this.clients[client.uuid] = {
            ...client,
            item: item,
        };
    }

    public remove_client(id: string) {
        if (!(id in this.clients)) {
            return;
        }

        this.clients[id].item.remove();
    }

    public update_client(id: string, fields: Partial<Client & { delta: number }>) {
        if (id in this.clients) {
            if (typeof fields.uuid !== "undefined") {
                this.clients[id].item.uuid = fields.uuid;
            }

            if (typeof fields.position !== "undefined") {
                this.clients[id].item.position = fields.position;
            }

            if (typeof fields.delta !== "undefined") {
                this.clients[id].item.delta = fields.delta;
            }

            if (typeof fields.playing !== "undefined") {
                this.clients[id].item.playing = fields.playing;
            }

            if (typeof fields.latency !== "undefined") {
                this.clients[id].item.latency = fields.latency;
            }
        }
    }
}

customElements.define("client-list", ClientListComponent);

export default ClientListComponent;
