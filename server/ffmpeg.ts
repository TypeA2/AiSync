import * as child from "child_process";
import * as util from "util";
import * as fs from "fs";

import Media from "../shared/media";

const exec_file = util.promisify(child.execFile);

const ffmpeg_path = child.execSync("which ffmpeg").toString("utf-8").trim();
const ffprobe_path = child.execSync("which ffprobe").toString("utf-8").trim();

fs.accessSync(ffmpeg_path, fs.constants.X_OK);
fs.accessSync(ffprobe_path, fs.constants.X_OK);

namespace ffmpeg {
    export async function probe(file: string): Promise<Media.FileInfo> {
        const proc = await exec_file(ffprobe_path,
            [
                "-v", "quiet",
                "-print_format", "json",
                "-show_streams", "-show_format",
                file,
            ]
        );

        let res: Media.FileInfo = {
            filename: "",
            format: "",
            duration: NaN,
            size: NaN,
            bitrate: NaN,
            
            streams: {
                video: [],
                audio: [],
                subtitle: [],
                attachment: [],
                other: [],
            }
        };

        const probe = JSON.parse(proc.stdout);
        const format = probe.format;
        res.filename = format.filename;
        res.format = format.format_long_name;
        res.duration = parseFloat(format.duration);
        res.size = parseInt(format.size, 10);
        res.bitrate = parseInt(format.bit_rate, 10);

        for (const stream of probe.streams) {
            const base: Media.Stream = {
                index: stream.index,
                codec_name: stream.codec_name,
                codec_type: stream.codec_type,
                time_base: stream.codec_time_base,
                default: (stream.disposition.default === 1),

                lang: stream?.tags?.language,
                title: stream?.tags?.title
            };
            
            switch (base.codec_type) {
                case "video": {
                    res.streams.video.push({
                        ...base,
                        width: parseInt(stream.width, 10),
                        height: parseInt(stream.height, 10),
                        fps: stream.avg_frame_rate,
                        pix_fmt: stream.pix_fmt,
                        pix_bits: stream.bits_per_raw_sample,
                        aspect_ratio: stream.display_aspect_ratio,
                    });
                    break;
                }

                case "audio": {
                    res.streams.audio.push({
                        ...base,
                        sample_rate: parseInt(stream.sample_rate, 10),
                        sample_fmt: stream.sample_fmt,
                        channels: stream.channels,
                        channel_layout: stream.channel_layout
                    });
                    break;
                }

                case "subtitle": {
                    res.streams.subtitle.push(base);
                    break;
                }

                case "attachment": {
                    res.streams.attachment.push({
                        ...base,
                        filename: stream?.tags?.filename,
                        mimetype: stream?.tags?.mimetype
                    });
                    break;
                }

                default: {
                    res.streams.other.push(base);
                    break;
                }
            }
        }

        return res;
    }

    export async function extract_stream(file: string, id: number, target: string, copy: boolean = true): Promise<void> {
        const argv = [
            "-v", "quiet",
            "-i", file,
            "-map", `0:${id}`
        ];

        if (copy) {
            argv.push("-c", "copy");
        }

        argv.push(target);

        await exec_file(ffmpeg_path, argv);
    }
}

export default ffmpeg;
