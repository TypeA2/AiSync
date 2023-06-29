import { Client } from "../shared/api";
import log from "../shared/logging";
import "../shared/utils";

class ClientListItemComponent extends HTMLTableRowElement {
    private _client: Client;
    private _delta_val: number;

    private _uuid: HTMLTableCellElement = document.createElement("td");
    private _position: HTMLTableCellElement = document.createElement("td");
    private _delta: HTMLTableCellElement = document.createElement("td");
    private _playing: HTMLTableCellElement = document.createElement("td");
    private _latency: HTMLTableCellElement = document.createElement("td");

    constructor(client: Client) {
        super();


        this.append(this._uuid, this._position, this._delta, this._playing, this._latency);

        this._client = client;
        this._delta_val = 0;
        this.uuid = client.uuid;
        this.position = client.position;
        this.delta = 0;
        this.playing = client.playing;
        this.latency = client.latency;
    }

    public set uuid(val: string) {
        this._client.uuid = val;
        this._uuid.innerText = val;
    }

    public get uuid(): string {
        return this._client.uuid;
    }

    public set position(val: number) {
        this._client.position = val;
        this._position.innerText = val.toTimeString();
    }

    public get position(): number {
        return this._client.position;
    }

    public set delta(val: number) {
        this._delta_val = val;
        this._delta.innerText = val.toFixed(3);
    }

    public get delta(): number {
        return this._delta_val;
    }

    public set playing(val: boolean) {
        this._client.playing = val;
        this._playing.innerText = val ? "Playing" : "Paused";
    }

    public get playing(): boolean {
        return this._client.playing;
    }

    public set latency(val: number) {
        this._client.latency = val;
        this._latency.innerText = Math.round(val * 1000).toString();
    }

    public get latency(): number {
        return this._client.latency;
    }
}

customElements.define("client-list-item", ClientListItemComponent, {
    extends: "tr"
});

export default ClientListItemComponent;
