import * as tmp from "tmp";
import * as uuid from "uuid";
import * as fs from "fs/promises";
import * as path from "path";
import { WebSocket } from "ws";
import log from "../shared/logging";
import MediaFile, { ActiveMediaFile } from "../shared/MediaFile";
import * as Ai from "../shared/api";
import make_app, { AppOptions, App } from "./server_setup";

interface ServerWithFile {
    media: ActiveMediaFile;
}

class Server {
    tmp_dir: tmp.DirResult;
    public get tmp() {
        return this.tmp_dir.name;
    }

    /* 64 MiB */
    file_chunk_size: number = 64 * 1024 * 1024;

    /* Information about the currently uploading file */
    uploading_file: string | undefined;
    uploading_size: number | undefined;
    uploading_name: string | undefined;
    uploading_handle: fs.FileHandle | undefined;

    media: MediaFile;
    public has_file(): this is ServerWithFile {
        return this.media.has_file();
    }
    
    public get current_uuid(): string {
        if (!this.media.has_file()) {
            throw new Error("No current file");
        }

        return this.media.current_uuid;
    }

    public get file(): Ai.NewFile {
        return this.media.file;
    }

    app: App;
    public get ws_clients() {
        return this.app.ws_inst.getWss().clients;
    }

    clients: { [uuid: string]: { ws: WebSocket; client: Ai.Client; } } = {};
    admin: WebSocket | undefined;

    constructor(options: AppOptions) {
        this.app = make_app(options);

        this.tmp_dir = tmp.dirSync({
            unsafeCleanup: true,
        });

        log.log("Temp directory at", this.tmp);

        this.media = new MediaFile(this.tmp);
    }

    public async allocate_file(name: string, size: number): Promise<Ai.FileCreated | Ai.FailureMessage> {
        if (this.uploading_file !== undefined) {
            return {
                success: false,
                message: "Upload already in progress",
            }
        }

        if (size > 100 * 1024 * 1024 * 1024) {
            return {
                success: false,
                message: `File too large (${size} > 100 GiB)`
            };
        }

        log.info("Allocating new file of size", size, `with original name "${name}"`);

        /* Allocate new file */
        this.uploading_size = size;
        this.uploading_name = name;
        this.uploading_file = uuid.v4();

        const tmp_path = path.join(this.tmp, this.uploading_file);

        /* Create empty file and truncate */
        this.uploading_handle = await fs.open(tmp_path, "wx");
        await this.uploading_handle.truncate(size);

        return {
            success: true,
            chunk_size: this.file_chunk_size,
            id: this.uploading_file
        };
    }

    public async write_file(index: number, buf: Buffer): Promise<Ai.ResponseMessage> {
        if (this.uploading_file === undefined || this.uploading_size === undefined || this.uploading_handle === undefined) {
            return {
                success: false,
                message: "No upload in progress"
            };
        }
        
        const offset = index * this.file_chunk_size;

        if (offset > this.uploading_size) {
            return {
                success: false,
                message: "Offset out of range"
            }
        }

        log.debug("Index", index, "buffer of size", buf.length, "max size", this.uploading_size);

        let write_size = buf.length;
        if (offset + buf.length > this.uploading_size) {
            write_size = this.uploading_size - offset;
        }

        await this.uploading_handle.write(buf, 0, write_size, offset);
        await this.uploading_handle.sync();

        log.debug("Wrote", write_size, "bytes at index", offset);

        return {
            success: true
        };
    }

    public async set_file(): Promise<Ai.ResponseMessage> {
        if (this.uploading_file === undefined || this.uploading_name === undefined || this.uploading_handle === undefined) {
            return {
                success: false,
                message: "No upload in progress"
            };
        }

        await this.uploading_handle.close();

        log.info("Upload of", this.uploading_file, "done");

        const tmp_path = path.join(this.tmp, this.uploading_file);

        const file_info = await this.media.set_file(tmp_path, this.uploading_name);

        await fs.unlink(tmp_path);

        this.uploading_file = undefined;
        this.uploading_size = undefined;
        this.uploading_name = undefined;
        this.uploading_handle = undefined;

        log.log(`Sending clients new file ${file_info.uuid}`)
        const data = JSON.stringify(this.media.file);
        this.ws_clients.forEach(client => client.send(data));

        return { success: true };
    }

    public async unset_file() {
        const data = JSON.stringify({ msg: Ai.MessageID.FileRemoved });
        this.ws_clients.forEach(client => client.send(data));

        await this.media.unset_file();
    }

    public get_file(stream: string): string | null {
        if (!this.media.has_file()) {
            throw new Error("No current file");
        }

        if (stream === "media") {
            return `${this.media.current_outdir}/media.${this.media.streams.media_type}`;
        }

        const parsed = parseInt(stream, 10);

        if (isNaN(parsed) || !this.media.streams.subtitle.map(stream => stream.index).includes(parsed)) {
            return null;
        }

        return `${this.media.current_outdir}/${parsed}.vtt`;
    }

    public add_client(ws: WebSocket): string {
        const id = uuid.v4();

        const unexpected = (id: string,msg: Ai.Message) => log.warn("Unexpected message received:", id, msg);

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const call_map: { [type in Ai.MessageID]: { (id: string, msg: any): void } } = {
            [Ai.MessageID.None]: (id: string, msg: Ai.Message) => console.log("Unknown message received for", id, msg),
            [Ai.MessageID.NewFile]: this.client_new_file,
            [Ai.MessageID.FileRemoved]: this.client_unset_file,
            [Ai.MessageID.ClientsJoined]: this.client_clients_joined,
            [Ai.MessageID.ClientsLeft]: this.client_clients_left,

            [Ai.MessageID.ClientPause]: this.client_pause,
            [Ai.MessageID.ClientPlay]: this.client_play,
            [Ai.MessageID.ClientSeek]: this.client_seek,

            [Ai.MessageID.RequestPause]: unexpected,
            [Ai.MessageID.RequestPlay]: unexpected,
            [Ai.MessageID.RequestSeek]: unexpected,

            [Ai.MessageID.ClientStatus]: this.client_status,
            [Ai.MessageID.ClientStatusUpdate]: unexpected,
        };

        ws.on("message", (data: string) => {
            const msg: Ai.Message = JSON.parse(data);

            call_map[msg.msg].call(this, id, msg);
        });

        this.clients[id] = {
            ws,
            client: {
                uuid: id,
                position: 0,
                playing: false,
                latency: 0,
            }
        };

        ws.on("close", () => this.remove_client(id));

        if (this.has_file()) {
            ws.send(JSON.stringify(this.file));
        }

        if (this.admin) {
            const msg: Ai.ClientsJoined = {
                msg: Ai.MessageID.ClientsJoined,
                clients: [ this.clients[id].client ]
            };

            this.admin.send(JSON.stringify(msg));
        }

        return id;
    }

    public remove_client(id: string) {
        if (id in this.clients) {
            if (this.admin) {
                log.info("Client left", id);
                
                const msg: Ai.ClientsLeft = {
                    msg: Ai.MessageID.ClientsLeft,
                    clients: [ id ]
                };

                this.admin.send(JSON.stringify(msg));
            }


            delete this.clients[id];
        }
    }

    public set_admin(ws: WebSocket) {
        this.admin = ws;

        /* Send current file info, clients if present */
        const msg: Ai.ClientsJoined = {
            msg: Ai.MessageID.ClientsJoined,
            clients: [],
        };

        for (const client of Object.values(this.clients)) {
            msg.clients.push(client.client);
        }

        if (msg.clients.length > 0) {
            ws.send(JSON.stringify(msg));
        }

        if (this.has_file()) {
            ws.send(JSON.stringify(this.file));
        }
    }

    public remove_admin() {
        log.log("Admin left");
        this.admin = undefined;
    }

    private client_new_file(id: string, msg: Ai.NewFile) {
        log.log("client_new_file", id, msg);
    }

    private client_unset_file(id: string, msg: Ai.Message) {
        log.log("client_unset_file", id, msg);
    }

    private client_clients_joined(id: string, msg: Ai.ClientsJoined) {
        log.log("client_clients_joined", id, msg);
    }

    private client_clients_left(id: string, msg: Ai.ClientsLeft) {
        log.log("client_clients_left", id, msg);
    }

    private client_pause(id: string, msg: Ai.Message) {
        log.log("client_pause", id, msg);

        const reply: Ai.Message = {
            msg: Ai.MessageID.RequestPause,
        };

        const text = JSON.stringify(reply);

        if (this.admin) {
            this.admin.send(text);
        }

        for (const client of Object.values(this.clients)) {
            if (id === client.client.uuid) {
                continue;
            }

            client.ws.send(text);
        }
    }

    private client_play(id: string, msg: Ai.Message) {
        log.log("client_play", id, msg);

        const reply: Ai.Message = {
            msg: Ai.MessageID.RequestPlay,
        };

        const text = JSON.stringify(reply);

        if (this.admin) {
            this.admin.send(text);
        }

        for (const client of Object.values(this.clients)) {
            if (id === client.client.uuid) {
                continue;
            }

            client.ws.send(text);
        }
    }

    private client_seek(id: string, msg: Ai.Seek) {
        log.log("client_seek", id, msg);

        const reply: Ai.Seek = {
            msg: Ai.MessageID.RequestSeek,
            position: msg.position
        };
        const text = JSON.stringify(reply);

        if (this.admin) {
            this.admin.send(text);
        }

        for (const client of Object.values(this.clients)) {
            if (id === client.client.uuid) {
                continue;
            }

            client.ws.send(text);
        }
    }

    private client_status(id: string, msg: Ai.ClientStatus) {
        if (this.admin) {
            const reply: Ai.ClientStatusUpdate = {
                ...msg,
                id: id,
            };

            reply.msg = Ai.MessageID.ClientStatusUpdate;
            
            const text = JSON.stringify(reply);

            this.admin.send(text);
        }
    }
}

export default Server;
