import * as express from "express";
import * as passport from "passport";
import * as passport_local from "passport-local";
import * as ws from "ws";
import * as http from "http";
import * as session from "express-session";
import * as crypto from "crypto";
import * as cookie_parser from "cookie-parser";

declare global {
    namespace Express {
        interface User {
            user: "user" | "admin";
        }
    }
}

export interface AppOptions {
    js_dir: string;     /* Relative path to serve JS from */
    css_dir: string;    /* Relative path to serve CSS from */
    assets_dir: string; /* Relative path to server static assets from */

    max_session_age: number; /* Maximimum session cookie age */
}

interface App {
    app: express.Express;
    server: http.Server;
    wss: ws.WebSocketServer;

    pass: passport.Authenticator;
}

export function make_app(options: AppOptions): App {
    const app = express();
    const server = http.createServer(app);
    const wss = new ws.WebSocketServer({ server });

    const pass = new passport.Authenticator();
    pass.use(new passport_local.Strategy((username, password, done) => {
        switch (username) {
            case "user": {
                if (process.env.AISYNC_USER_PASSWORD === password) {
                    done(null, { user: "user" });
                } else {
                    done(null, false, { message: "Incorrect user password" });
                }
                break;
            }
    
            case "admin": {
                if (process.env.AISYNC_ADMIN_PASSWORD == password) {
                    done(null, { user: "admin" });
                } else {
                    done(null, false, { message: "Incorrect admin password" });
                }
                break;
            }
    
            default: {
                done(null, false, { message: "Unknown user" });
                break;
            }
        }
    }));

    pass.serializeUser((user, cb) => {
        process.nextTick(() => {
            return cb(null, user);
        });
    });
    
    pass.deserializeUser((user: Express.User, cb) => {
        process.nextTick(() => {
            return cb(null, user);
        });
    });

    app.use("/js", express.static(options.js_dir));
    app.use("/css", express.static(options.css_dir));
    app.use("/assets", express.static(options.assets_dir));

    app.use(session({
        secret: crypto.randomBytes(64).toString("hex"),
        resave: false,
        saveUninitialized: false,
        cookie: {
            maxAge: 1000 * 3600 * 24,
        }
    }));
    app.use(pass.session(), express.json(), express.urlencoded({ extended: true }));

    return {
        app,
        server,
        wss,
        pass
    };
}