namespace Ai {
    export enum MessageID {
        None,
        NewFile,
    }

    export interface Message {
        msg: MessageID;
    }

    export type StreamMap = { [id: string]: string };

    export interface NewFile extends Message {
        uuid: string; /* GUID of the new file */

        /* ID -> MIME mapping for streams */
        video: StreamMap;
        audio: StreamMap;
        subtitle:StreamMap;
    }
}

export default Ai;
