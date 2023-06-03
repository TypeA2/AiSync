import * as dotenv from "dotenv";
import * as minimist from "minimist-lite";

dotenv.config();

export interface Args {
    user: string;
    admin: string;
    port: Number;
}
const argv = minimist<Args>(process.argv.slice(2), {
    default: {
        user: process.env.AISYNC_USER_PASSWORD,
        admin: process.env.AISYNC_ADMIN_PASSWORD,
        port: process.env.AISYNC_PORT ? parseInt(process.env.AISYNC_PORT, 10) : 80,
    }
});

function usage() {
    console.info(
`
Usage: ${process.argv0} [--port <port>] [--user <user_password>] [--admin <admin_password>]

  --port <port>  Server port to bind to (environ: AISYNC_PORT)
  --user <pass>  User login password (environ: AISYNC_USER_PASSWORD)
  --admin <pass> Admin login password (environ: AISYNC_ADMIN_PASSWORD)
`);
    process.exit(1);
}

if (typeof argv.user !== "string" || argv.user === "") {
    console.info("No user password specified");
    usage();
}

if (typeof argv.admin !== "string" || argv.admin == "") {
    console.info("No admin password specified");
    usage();
}

if (typeof argv.port !== "number") {
    console.info("No port number specified");
    usage();
}

if (argv.user.length < 6) {
    console.info(`User password should be at least 6 characters (${argv.user.length} < 6)`);
    usage();
}

if (argv.admin.length < 6) {
    console.info(`Admin password should be at least 6 characters (${argv.admin.length} < 6)`);
    usage();
}

if (argv.admin === argv.user) {
    console.info("Admin and user passwords cannot match");
    usage();
}

export default argv;
