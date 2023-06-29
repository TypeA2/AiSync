// eslint-disable-next-line @typescript-eslint/no-unused-vars
interface Number {
    toTimeString: () => string;
}

Number.prototype.toTimeString = function(): string {
    let res = "";

    let val = this.valueOf();

    if (val >= 3600) {
        const hours = Math.floor(val / 3600);

        res = hours.toString() + ":";
        val -= hours * 3600;
    }

    const minutes = Math.floor(val / 60);
    res = res + minutes.toString().padStart(2, "0");
    val -= minutes * 60;

    const seconds = Math.floor(val);
    res = res + ":" + seconds.toString().padStart(2, "0");
    val -= seconds;

    /* 3-digit, milisecond precision */
    const ms = Math.round(val * 1000);
    res = res + "." + ms.toString().padStart(3, "0");

    return res;
}
