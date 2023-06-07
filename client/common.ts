export function throw_error(msg: string | undefined): never {
    throw new Error(msg);
}

export function id<Res extends HTMLElement>(name: string): Res {
    return (document.getElementById(name) as Res) || throw_error(`ID ${id} not found`);
}
