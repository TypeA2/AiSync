type LogFunc = { (message?: any, ...optionalParams: any[]): void };
interface Logger {
    trace: LogFunc;
    debug: LogFunc;
    log: LogFunc;
    info: LogFunc;
    warn: LogFunc;
    error: LogFunc;
}

function log_format(cb: LogFunc, level: string): LogFunc {
    return (message?: any, ...optionalParams: any[]) => {
        cb(`[${new Date().toISOString()}] [${level}]`, message, ...optionalParams);
    }
}

const log: Logger = {
    trace: log_format(console.trace, "TRACE"),
    debug: log_format(console.debug, "DEBUG"),
    log: log_format(console.log, "LOG"),
    info: log_format(console.info, "INFO"),
    warn: log_format(console.warn, "WARN"),
    error: log_format(console.error, "ERROR"),
}

export default log;
