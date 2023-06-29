import * as express from "express";
import * as express_ws from "express-ws";
import * as passport from "passport";
import * as passport_local from "passport-local";
import * as express_session from "express-session";
import * as crypto from "crypto";

import "express-ws";

declare global {
    // eslint-disable-next-line @typescript-eslint/no-namespace
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

export interface App {
    app: express_ws.Application;
    ws_inst: express_ws.Instance;

    pass: passport.Authenticator;
}

export default function make_app(options: AppOptions): App {
    const ws_inst = express_ws(express());
    const app = ws_inst.app;

    const pass = new passport.Authenticator();
    pass.use(new passport_local.Strategy((_username, password, done) => {
        if (password === process.env.AISYNC_ADMIN_PASSWORD) {
            done(null, { user: "admin" });
        } else if (password === process.env.AISYNC_USER_PASSWORD) {
            done(null, { user: "user" });
        } else {
            done(null, false, { message: "Invalid password" });
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

    const session = express_session({
        secret: crypto.randomBytes(64).toString("hex"),
        resave: false,
        saveUninitialized: false,
        cookie: {
            maxAge: 1000 * 3600 * 24,
        }
    });

    app.use(session);
    app.use(pass.session(), express.json(), express.urlencoded({ extended: true }));

    return {
        app,
        ws_inst,
        pass,
    };
}
