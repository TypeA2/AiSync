namespace Media {
    export interface Stream {
        index: number;
        codec_name: string;
        codec_type: "video" | "audio" | "subtitle" | "attachment";
        time_base: string,
        default: boolean;
    
        lang: string | undefined;
        title: string | undefined;
    }
    
    export interface VideoStream extends Stream {
        width: number;
        height: number;
        fps: string;
        pix_fmt: string;
        pix_bits: number;
        aspect_ratio: string;
    }
    
    export interface AudioStream extends Stream {
        sample_rate: number;
        sample_fmt: string;
        channels: number;
        channel_layout: string;
    }
    
    export interface AttachmentStream extends Stream {
        filename: string | undefined;
        mimetype: string | undefined;
    }
    
    interface Streams {
        video: VideoStream[];
        audio: AudioStream[];
        subtitle: Stream[];
        attachment: AttachmentStream[];
        other: Stream[];
    }
    
    export interface FileInfo {
        filename: string;
        format: string;
        duration: number;
        size: number;
        bitrate: number;
    
        streams: Streams;
    }
    
    interface Config {
        video_codecs: string[];
        audio_codecs: string[];
        subtitle_codecs: string[];
    }
    
    export const config: Config = {
        video_codecs: [
            "h264",
            "hevc",
            "vp8",
            "vp9",
            "av1",
        ],
    
        audio_codecs: [
            "pcm_s16le",
            "mp3",
            "aac",
            "vorbis",
            "opus",
            "flac",
        ],
    
        subtitle_codecs: [
            "ass",
            "srt",
        ]
    }

    export function codec_container(codec: string): string {
        if (config.video_codecs.includes(codec)) {
            switch (codec) {
                case "h264":
                case "hevc":
                case "av1":
                    return "mp4";

                case "vp8":
                case "vp9":
                    return "webm";
            }
        } else if (config.audio_codecs.includes(codec)) {
            switch (codec) {
                case "pcm_s16le": return "wav";
                case "mp3": return "mp3";
                case "aac": return "m4a";

                case "vorbis":
                case "opus":
                    return "ogg";

                case "flac": return "flac";
            }
        } else {
            switch (codec) {
                case "ass": return "ass";
                case "srt": return "srt";
            }
        }

        throw new Error(`Unknown codec ${codec}`);
    }
}

export default Media;
