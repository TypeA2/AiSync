import "express-session";

declare module "express-session" {
    interface SessionData {
        login_type: "admin" | "user";
    }
}
