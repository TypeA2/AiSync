import "./scss/login.scss";

if (window.location.search.length > 0) {
    const is_admin = window.location.search.startsWith("?admin");

    console.log(`Admin: ${is_admin}`);

    if (is_admin) {
        let type = document.getElementById("login-type") as HTMLSelectElement;

        type.value = "admin";
    }

    if (window.location.search.endsWith("error")) {
        (document.getElementById("password") as HTMLInputElement).style.borderColor = "red";
    }
}
