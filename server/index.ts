
import * as multer from "multer";
import * as tmp from "tmp";
import { ensureLoggedIn } from "connect-ensure-login";
import argv from "./cli";
import MediaFile from "./media_file";

import * as setup from "./server_setup";
import { Ai } from "../shared/api";

const client_root = { root: `${__dirname}/../client/` };

const options: setup.AppOptions = {
    js_dir: client_root.root + "js",
    css_dir: client_root.root + "css",
    assets_dir: client_root.root + "assets",

    max_session_age: 24 * 3600 * 1000,
};

const { app, server, wss, pass } = setup.make_app(options);

const tmp_dir = tmp.dirSync({
    unsafeCleanup: true,
});

console.log("Temp directory at", tmp_dir.name);

const upload = multer({
    dest: tmp_dir.name,
});

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

wss.on("connection", (ws) => {

    //connection is up, let's add a simple simple event
    ws.on('message', (message: string) => {

        //log the received message and send it back to the client
        console.log('received: %s', message);
        ws.send(`Hello, you sent -> ${message}`);
    });

    //send immediatly a feedback to the incoming connection    
    ws.send('Hi there, I am a WebSocket server');
});

app.post("/admin", upload.single("file"), (req, res) => {
    if (typeof(req.user) !== "object" || req.user.user !== "admin") {
        return res.status(403).json({ success: false });
    }

    console.log(req.files);
    console.log(req.file?.fieldname);
    console.log(req.file?.originalname);
    console.log(req.file?.mimetype);
    console.log(req.file?.size);
    console.log(req.file?.destination);
    console.log(req.file?.filename);
    console.log(req.file?.path);

    return res.json({
        success: true,
    });
});

server.listen(argv.port, () => {
    console.log(`Server started on port ${(server.address() as { port: number; }).port}`);
});
