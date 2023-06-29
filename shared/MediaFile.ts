import * as path from "path";
import * as uuidgen from "uuid";
import * as fs from "fs";
import * as mime from "mime";

import * as ffmpeg from "../server/ffmpeg";
import * as media from "./media";
import * as Ai from "./api";
import log from "./logging";

export interface MediaFileStreams {
    media_mime: string;
    media_type: string;

    subtitle: media.Stream[];
}

export interface ActiveMediaFile {
    tmp_dir: string;

    current_uuid: string;
    current_info: media.FileInfo;

    streams: MediaFileStreams;
}

export default class MediaFile {
    tmp_dir: string;

    current_uuid: string | undefined;
    current_info: media.FileInfo | undefined;

    streams: MediaFileStreams | undefined;

    constructor(tmp_dir: string) {
        this.tmp_dir = tmp_dir;
    }

    public get current_outdir(): string {
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

    public async unset_file() {
        if (!this.has_file) {
            return;
        }

        /* Delete current outdir */
        await fs.promises.rm(this.current_outdir, {
            recursive: true,
            force: true
        });

        this.current_uuid = undefined;
        this.current_info = undefined;
        this.streams = undefined;
    }

    /* Parse a file, return it's UUID */
    public async set_file(file: string, orig_name: string): Promise<media.FileInfo & { uuid: string }> {
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
        if (!media.config.video_codecs.includes(video.codec_name)) {
            throw new Error(`Unsupported codec ${video.codec_name}, supported video codecs are: ${media.config.video_codecs.join(", ")}`);
        }

        for (const stream of probe.streams.audio) {
            if (!media.config.audio_codecs.includes(stream.codec_name)) {
                throw new Error(`Unsupported codec ${stream.codec_name}, supported audio codecs are: ${media.config.audio_codecs.join(", ")}`);
            }
        }

        for (const stream of probe.streams.subtitle) {
            if (!media.config.subtitle_codecs.includes(stream.codec_name)) {
                throw new Error(`Unsupported codec ${stream.codec_name}, supported subtitle codecs are: ${media.config.subtitle_codecs.join(", ")}`);
            }
        }

        const old_outdir = this.has_file() ? this.current_outdir : null;

        this.current_uuid = uuidgen.v4();

        log.debug("Old outdir:", old_outdir, " new UUID:", this.current_uuid);
        /* Directory for individual streams */
        await fs.promises.mkdir(this.current_outdir);

        try {
            const ext = path.extname(probe.filename).substring(1);

            const extract_subtitle_track = async (index: number) => {
                const target = path.join(this.current_outdir, `${index}.vtt`);

                await ffmpeg.extract_stream(file, index, target, false);
            };

            const extract_media_tracks = async (indices: number[]) => {
                const target = path.join(this.current_outdir, `media.${ext}`);

                await ffmpeg.extract_streams(file, indices, target, true);
            };

            const get_id = (stream: media.Stream) => stream.index;
            const media_ids = [
                get_id(video),
                ...probe.streams.audio.map(get_id)
            ];

            /* Separate all subtitle streams, combile all video and audio streams */
            const promises = [
                extract_media_tracks(media_ids),
                ...probe.streams.subtitle.map(stream => extract_subtitle_track(stream.index)),
            ]

            await Promise.all(promises);

            this.streams = {
                media_mime: mime.getType(probe.filename)!,
                media_type: ext,

                subtitle: probe.streams.subtitle
            };

            this.current_info = probe;

            if (old_outdir !== null) {
                log.log("Deleting old file at", old_outdir);
                await fs.promises.rm(old_outdir, { recursive: true, force: true });
            }
        } catch (e: any) {
            log.error("Failed extracting streams", e);

            /* Cleanup */
            this.unset_file();

            throw e;
        }

        return {
            ...probe,
            uuid: this.current_uuid
        };
    }

    public get file(): Ai.NewFile {
        if (!this.has_file) {
            throw new Error("No file present");
        }

        return {
            msg: Ai.MessageID.NewFile,
            uuid: this.current_uuid!,
            media_mime: this.streams!.media_mime,

            duration: this.current_info!.duration,
            name: this.current_info!.filename,

            subtitle: this.streams!.subtitle.map(stream => {
                return {
                    index: stream.index,
                    default: stream.default,
                    lang: stream.lang,
                    title: stream.title,
                }
            }),
        };
    }
}
