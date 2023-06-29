import "./scss/login.scss";

if (window.location.search.length > 0) {
    const is_admin = window.location.search.includes("admin");
    if (is_admin) {
        document.getElementById("invalid-feedback")!.innerText = "Access denied";
    }

    if (is_admin || window.location.search.includes("error")) {
        document.getElementById("password")?.classList.add("is-invalid");
    }
}
