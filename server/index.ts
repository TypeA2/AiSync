
import * as multer from "multer";
import * as tmp from "tmp";
import * as fs from "fs";
import { ensureLoggedIn } from "connect-ensure-login";

import argv from "./cli";
import * as MediaFile from "./MediaFile";
import * as setup from "./server_setup";
import Ai from "../shared/api";

const client_root = { root: `${__dirname}/../client/` };

const options: setup.AppOptions = {
    js_dir: client_root.root + "js",
    css_dir: client_root.root + "css",
    assets_dir: client_root.root + "assets",

    max_session_age: 24 * 3600 * 1000,
};

const { app, ws_inst, pass } = setup.make_app(options);

const tmp_dir = tmp.dirSync({
    unsafeCleanup: true,
});

const media = new MediaFile.default(tmp_dir.name);

console.log("Temp directory at", tmp_dir.name);

const upload = multer({
    dest: tmp_dir.name,
});

const exit_handler = () => {
    tmp_dir.removeCallback();

    process.exit(0);
}

process.on("SIGINT", exit_handler);
process.on("SIGTERM", exit_handler);

app.get("/", (req, res) => {
    switch (req.user?.user) {
        case "admin":
            res.redirect("/admin");
            break;

        case "user":
            res.redirect("/watch");
            break;

        default:
            res.redirect("/login");
    }
});

app.get("/watch", ensureLoggedIn("/login"), (req, res) => {
    res.sendFile("html/watch.html", client_root);
});

app.get("/admin", ensureLoggedIn("/login?admin"), (req, res) => {
    res.sendFile("html/admin.html", client_root);
});

app.get("/login", (req, res) => {
    res.sendFile("html/login.html", client_root)
});

app.use("/favicon.ico", (_, res) => {
    res.sendFile("favicon.ico", client_root);
});

app.post("/login",
    (req, res, next) => {
        /* Dynamically redirect error page */
        const type = (req.body?.username === "admin") ? "admin" : "user";
        
        const callback = pass.authenticate("local", { failureRedirect: `/login?${type}_error`, failureMessage: "what" });

        return callback(req, res, next);
    },

    (req, res) => {
        res.redirect((req.user?.user === "admin") ? "/admin" : "/watch");
    }
);

app.get("/logout", (req, res, next) => {
    req.logout(err => {
        if (err) { 
            return next(err);
        }
        
        res.redirect("/login");
    })
});

app.post("/admin", upload.single("file"), (req, res, next) => {
    const bound = res.writeHead.bind(res);
    res.writeHead = (status_code, status_message?, headers?) => {
        if (req.file) {
            fs.unlinkSync(req.file.path);
        }

        return bound(status_code, status_message as any, headers as any);
    };

    next();
}, async (req, res) => {
    if (typeof(req.user) !== "object" || req.user.user !== "admin") {
        return res.status(403).json({ success: false, message: "Not logged in" });
    }

    if (!req.file) {
        return res.status(400).json({ success: false, message: "No file attached" });
    }

    if (!req.file.mimetype.startsWith("video/")) {
        return res.status(415).json({ success: false, message: "Invalid MIME" });
    }

    try {
        const file = await media.set_file(req.file.path, req.file.originalname);

        return res.json({
            success: true,
            file: file
        });
    } catch (err: any) {
        return res.status(415).json({ success: false, message: err.message});
    }
});

app.get("/file/:uuid/:stream", (req, res) => {
    if (req.params.uuid !== media.current_uuid) {
        return res.status(404).send("File not found");
    }

    const stream = req.params.stream;

    if (isNaN(parseInt(stream, 10)) || !media.has_file()) {
        return res.status(404).send("Stream not found");
    }

    const file = media.streams.video[stream]
              ?? media.streams.audio[stream]
              ?? media.streams.subtitle[stream];

    if (file === undefined) {
        return res.status(404).send("Stream not found");
    }

    return res.sendFile(file.file);
});

app.ws("/", (ws, req, next) => {
    /* Emulate connect-ensure-login */
    if (!req.isAuthenticated || !req.isAuthenticated()) {
        return ws.close();
    }

    next();
}, (ws, req) => {
    ws.on("error", (data: string) => { console.error(`Error: ${data}`); });
    ws.on("message", (data: string) => { console.log(`Message: ${data}`)});
    
    if (media.has_file()) {
        console.log("Sending new client file info");

        const mapping = (map: MediaFile.StreamMap) => {
            const res: Ai.StreamMap = {};

            for (const [key, val] of Object.entries(map)) {
                res[key] = val.mime;
            }

            return res;
        };

        const msg: Ai.NewFile = {
            msg: Ai.MessageID.NewFile,
            uuid: media.current_uuid,
            video: mapping(media.streams.video),
            audio: mapping(media.streams.audio),
            subtitle: mapping(media.streams.subtitle),
        };

        ws.send(JSON.stringify(msg));
    }
});

app.listen(argv.port, () => {
    console.log(`Server started on port ${argv.port}`);
});
