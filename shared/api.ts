export enum SocketCloseReasons {
    InvalidLogin = 4000,
    DoubleAdmin = 4001,
}

export enum MessageID {
    None,
    NewFile,
    FileRemoved,

    ClientsJoined,
    ClientsLeft,

    ClientPause,
    ClientPlay,
    ClientSeek,

    RequestPause,
    RequestPlay,
    RequestSeek,

    ClientStatus,
    ClientStatusUpdate,
}

export interface Message {
    msg: MessageID;
}

export type StreamMap = { [id: string]: string };

export interface SubtitleStream {
    index: number;
    default: boolean;

    lang: string | undefined;
    title: string | undefined;
}

export interface NewFile extends Message {
    uuid: string; /* GUID of the new file */

    media_mime: string;

    duration: number; /* Duration in seconds */
    name: string; /* Filename */

    /* Subtitle stream IDs */
    subtitle: SubtitleStream[];
}


export interface SuccessMessage {
    success: true;
    message?: string;
}

export interface FailureMessage {
    success: false;
    message: string;
}

export type ResponseMessage = SuccessMessage | FailureMessage;

export interface FileCreated extends SuccessMessage {
    chunk_size: number;
    id: string;
}

export interface Client {
    uuid: string;
    position: number;
    playing: boolean;
    latency: number;
}

export interface ClientsJoined extends Message {
    clients: Client[];
}

export interface ClientsLeft extends Message {
    clients: string[];
}

export interface Seek extends Message {
    position: number;
}

export interface ClientStatus extends Message {
    sent: number; /* UTC timestamp */
    position: number;
    playing: boolean;
}

export interface ClientStatusUpdate extends ClientStatus {
    id: string;
}
