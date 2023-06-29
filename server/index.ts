
import * as multer from "multer";
import argv from "./cli";
import Server from "./server";
import log from "../shared/logging";
import { RequestHandler } from "express";
import { WebsocketRequestHandler } from "express-ws";
import { SocketCloseReasons } from "../shared/api";

const client_root = { root: `${__dirname}/../client/` };

const server = new Server({
    js_dir: client_root.root + "js",
    css_dir: client_root.root + "css",
    assets_dir: client_root.root + "assets",

    max_session_age: 24 * 3600 * 1000,
});

const { app, pass } = server.app;

const upload = multer();

const exit_handler = () => {
    server.tmp_dir.removeCallback();

    process.exit(0);
}

process.on("SIGINT", exit_handler);
process.on("SIGTERM", exit_handler);

function ensure_login(user: "user" | "admin", mode: "redirect" | "error"): RequestHandler {
    return (req, res, next) => {
        /* Lazy evaluation */
        const ret = (arg: string = "") => {
            if (mode === "redirect") {
                return res.redirect(`/login${arg}`);
            } else {
                return res.status(403).json({ success: false, message: "Not logged in" });
            }
        }

        if (!req.isAuthenticated || !req.isAuthenticated() || typeof(req.user) !== "object") {
            return ret();
        }

        /* Only allow admin */
        if (user === "admin") {
            if (req.user.user !== "admin") {
                return ret((req.user.user === "user") ? "?admin" : "");
            }
        } else {
            /* Admin can access both pages */
            if (req.user.user !== "user" && req.user.user !== "admin") {
                return ret();
            }
        }

        next();
    }
}

function ensure_login_ws(user: "user" | "admin"): WebsocketRequestHandler {
    return (ws, req, next) => {
        if (!req.isAuthenticated || !req.isAuthenticated() || typeof(req.user) !== "object") {
            return ws.close();
        }

        if (user === "admin") {
            if (req.user.user !== "admin") {
                return ws.close(SocketCloseReasons.InvalidLogin, "Invalid login");
            }
        } else {
            if (req.user.user !== "user" && req.user.user !== "admin") {
                return ws.close(SocketCloseReasons.InvalidLogin, "Invalig login");
            }
        }

        next();
    }
}

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

app.use("/favicon.ico", (_, res) => {
    res.sendFile("favicon.ico", client_root);
});

app.get("/watch", ensure_login("user", "redirect"), (_, res) => {
    res.sendFile("html/watch.html", client_root);
});

app.get("/admin", ensure_login("admin", "redirect"), (req, res) => {
    res.sendFile("html/admin.html", client_root);
});

app.get("/login", (_, res) => {
    res.sendFile("html/login.html", client_root)
});

app.post("/login",
    pass.authenticate("local", { failureRedirect: `/login?error`, failureMessage: "what" }),
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

app.get("/file/:uuid/:stream", (req, res) => {
    if (!server.has_file || req.params.uuid !== server.current_uuid) {
        return res.status(404).json({ success: false, message: "File not found"});
    }
    
    const file = server.get_file(req.params.stream);
    if (file === null) {
        return res.status(404).json({ success: false, message: "File not found" });
    }

    return res.sendFile(file);
});

app.post("/admin", ensure_login("admin", "error"), upload.none(), async (req, res) => {
    if (server.has_file()) {
        return res.status(403).json({ success: false, message: "File already active" });
    }

    return res.json(await server.allocate_file(req.body.name as string, parseInt(req.body.size)));
});

app.patch("/admin", ensure_login("admin", "error"), upload.single("blob"), async (req, res) => {
    if (server.has_file()) {
        return res.status(403).json({ success: false, message: "File already active" });
    }

    if (req.body.id !== server.uploading_file) {
        return res.status(403).json({ success: false, message: "Invalid upload ID" });
    }

    try {
        return res.json(await server.write_file(parseInt(req.body.index), req.file!.buffer));
    } catch (err: any) {
        log.error("Error writing data:", err.message);
        return res.status(500).json({ success: false, message: "Error writing data" });
    }
});

app.put("/admin", ensure_login("admin", "error"), upload.none(), async (req, res) => {
    if (server.has_file()) {
        return res.status(403).json({ success: false, message: "File already active" });
    }

    if (req.body.id !== server.uploading_file) {
        return res.status(403).json({ success: false, message: "Invalid upload ID" });
    }

    try {
        return res.json(await server.set_file());
    } catch (err: any) {
        log.error("Error processing file:", err.message);
        return res.status(500).json({ success: false, message: "Error processing file" });
    }
});

app.delete("/admin", ensure_login("admin", "error"), (req, res) => {
    if (!server.has_file()) {
        return res.status(403).json({ success: false, message: "No file active" });
    }

    server.unset_file();
});

app.ws("/ws_admin", ensure_login_ws("admin"), (ws, req) => {
    ws.on("error", (data: string) => { log.error(`Error: ${data}`); });
    ws.on("message", (data: string) => { log.log(`Message: ${data}`)});

    server.set_admin(ws);

    ws.on("close", () => server.remove_admin());

    log.info("Admin connected", req.ip);
});

app.ws("/ws_watch", ensure_login_ws("user"), (ws, _) => {
    ws.on("error", (data: string) => { log.error(`Error: ${data}`); });
    
    const id = server.add_client(ws);

    ws.on("close", () => server.remove_client(id));

    log.info("Client connected:", id);
});

app.listen(argv.port, () => {
    log.info(`Server started on port ${argv.port}`);
});
