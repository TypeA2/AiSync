import * as express from "express";
import * as session from "express-session";
import * as http from "http";
import * as crypto from "crypto";
import * as ws from "ws";
import * as cookie_parser from "cookie-parser";
import argv from "./cli";
import "./session";

const app = express();
const server = http.createServer(app);
const wss = new ws.WebSocketServer({ server });

const client_root = { root: `${__dirname}/../client/` };

app.use("/js", express.static(`${__dirname}/../client/js`));
app.use("/css", express.static(`${__dirname}/../client/css`));
app.use("/assets", express.static(`${__dirname}/../client/assets`));

app.use(session({
    secret: crypto.randomBytes(64).toString("hex"),
    resave: false,
    saveUninitialized: false,
    cookie: {
        maxAge: 1000 * 3600 * 24,
    }
}));
app.use(cookie_parser(), express.json(), express.urlencoded({ extended: true }));

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

app.get("/", (req, res) => {
    switch (req.session.login_type) {
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

app.get("/watch", (req, res) => {
    if (req.session.login_type !== "user" && req.session.login_type !== "admin") {
        res.redirect("/login");
    } else {
        res.sendFile("html/watch.html", client_root);
    }
});

app.get("/admin", (req, res) => {
    if (req.session.login_type !== "admin") {
        res.redirect("/login?admin");
    } else {
        res.sendFile("html/admin.html", client_root);
    }
});

app.get("/login", (req, res) => {
    res.sendFile("html/login.html", client_root)
});

app.post("/login", (req, res) => {
    switch (req.body.type) {
        case "user": {
            if (process.env.AISYNC_USER_PASSWORD === req.body.password) {
                req.session.login_type = "user";
                res.redirect("/watch");
            } else {
                res.redirect("/login?error");
            }
            break;
        }

        case "admin": {
            if (process.env.AISYNC_ADMIN_PASSWORD == req.body.password) {
                req.session.login_type = "admin";
                res.redirect("/admin");
            } else {
                res.redirect("/login?admin_error");
            }
            break;
        }
    }
});

app.get("/logout", (req, res) => {
    req.session.destroy(_ => res.redirect("/login"));
});

app.use("/favicon.ico", (_, res) => {
    res.sendFile("favicon.ico", client_root);
});

server.listen(argv.port, () => {
    console.log(`Server started on port ${(server.address() as ws.AddressInfo).port}`);
});
