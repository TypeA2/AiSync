import * as path from "path";
import * as uuid from "uuid";
import * as fs from "fs";
import * as mime from "mime";

import ffmpeg from "./ffmpeg";
import Media from "../shared/media";
import { throw_error } from "../client/common";

export interface StreamDescriptor {
    file: string;
    mime: string;
}

export type StreamMap = { [id: string]: StreamDescriptor };

export interface MediaFileStreams {
    video: StreamMap;
    audio: StreamMap;
    subtitle: StreamMap;
}

export interface ActiveMediaFile {
    tmp_dir: string;
    current_uuid: string;
    streams: MediaFileStreams;
}

export default class MediaFile {
    tmp_dir: string;

    current_uuid: string | undefined;

    streams: MediaFileStreams | undefined;

    constructor(tmp_dir: string) {
        this.tmp_dir = tmp_dir;
    }

    private get current_outdir(): string {
        if (!this.current_uuid) {
            throw new Error("No current UUID set");
        }

        return path.join(this.tmp_dir, this.current_uuid);
    }

    public get get_streams() {
        return this.streams;
    }

    public has_file(): this is ActiveMediaFile {
        return this.current_uuid !== undefined;
    }

    /* Parse a file, return it's UUID */
    public async set_file(file: string, orig_name: string): Promise<Media.FileInfo | { uuid: string }> {
        const probe = await ffmpeg.probe(file);
        probe.filename = orig_name || path.basename(probe.filename);
        
        /* Validate that the file makes sense to play
         * Requirements:
         *  - Exactly 1 video stream
         *  - 0 or more audio streams
         *  - 0 or more subtitle streams
         */

        if (probe.streams.video.length !== 1) {
            throw new Error(`${probe.streams.video.length} video streams in file, should be exactly 1`);
        }
        
        const video = probe.streams.video[0];

        /* Make sure all codecs are supported */
        if (!Media.config.video_codecs.includes(video.codec_name)) {
            throw new Error(`Unsupported codec ${video.codec_name}, supported video codecs are: ${Media.config.video_codecs.join(", ")}`);
        }

        for (const stream of probe.streams.audio) {
            if (!Media.config.audio_codecs.includes(stream.codec_name)) {
                throw new Error(`Unsupported codec ${stream.codec_name}, supported audio codecs are: ${Media.config.audio_codecs.join(", ")}`);
            }
        }

        for (const stream of probe.streams.subtitle) {
            if (!Media.config.subtitle_codecs.includes(stream.codec_name)) {
                throw new Error(`Unsupported codec ${stream.codec_name}, supported subtitle codecs are: ${Media.config.subtitle_codecs.join(", ")}`);
            }
        }

        this.current_uuid = uuid.v4();

        /* Directory for individual streams */
        await fs.promises.mkdir(this.current_outdir);

        /* Extract the video stream, and all audio and subtitle streams */
        const extract_one_impl = async (map: StreamMap, stream: Media.Stream, ext: string | undefined) => {
            const target = path.join(
                this.current_outdir,
                `${stream.index}.${ext || Media.codec_container(stream.codec_name)}`
            );

            await ffmpeg.extract_stream(file, stream.index, target, ext ? false : true);

            map[stream.index] = {
                file: target,
                mime: mime.getType(target) ?? throw_error(`Could not get MIME for ${path.basename(target)}`)
            };
        };

        const extract_one = (map: StreamMap, stream: Media.Stream) => extract_one_impl(map, stream, undefined);

        try {
            this.streams = { video: {}, audio: {}, subtitle: {} };

            const promises = [
                extract_one(this.streams.video, video),
                ...probe.streams.audio.map(stream => extract_one(this.streams!.audio, stream)),
                ...probe.streams.subtitle.map(stream => extract_one_impl(this.streams!.subtitle, stream, "vtt")),
            ]
            
            await Promise.all(promises);
        } catch (e) {
            /* Cleanup */
            await fs.promises.rm(this.current_outdir, {
                recursive: true
            });

            this.current_uuid = undefined;
            this.streams = { video: {}, audio: {}, subtitle: {} };

            throw e;
        }

        return {
            ...probe,
            uuid: this.current_uuid
        };
    }
}
