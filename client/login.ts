import "./scss/login.scss";

if (window.location.search.length > 0) {
    const is_admin = window.location.search.includes("admin");

    if (is_admin) {
        let type = document.getElementById("login-type") as HTMLSelectElement;

        type.value = "admin";
    }

    if (window.location.search.includes("error")) {
        document.getElementById("password")?.classList.add("is-invalid");
    }
}
