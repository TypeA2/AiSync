import { Client } from "../shared/api";

import ClientListItemComponent from "./ClientListItemComponent";

class ClientListComponent extends HTMLElement {
    clients: { [uuid: string]: Client & { item: ClientListItemComponent } } = {};

    constructor() {
        super();

        this.classList.add("list-group");
    }

    public add(client: Client) {
        if (client.uuid in this.clients) {
            return; /* No-op */
        }

        const item = new ClientListItemComponent(client);
        this.appendChild(item);

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

    public update_client(id: string, fields: Partial<Client>) {
        if (id in this.clients) {
            if (typeof fields.uuid !== "undefined") {
                this.clients[id].item.uuid = fields.uuid;
            }

            if (typeof fields.position !== "undefined") {
                this.clients[id].item.position = fields.position;
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
