// Pelacak aktivitas pengguna di sisi klien. Server hanya bertanya "sudah berapa detik idle?"
// sekali per 15 detik, sehingga tidak ada lalu lintas per gerakan mouse.
let lastActivity = Date.now();
let started = false;
const events = ["mousemove", "mousedown", "keydown", "scroll", "touchstart", "click"];

function mark() {
    lastActivity = Date.now();
}

export function start() {
    if (started) {
        return;
    }

    started = true;
    lastActivity = Date.now();
    events.forEach((e) => window.addEventListener(e, mark, { passive: true }));
}

export function stop() {
    if (!started) {
        return;
    }

    started = false;
    events.forEach((e) => window.removeEventListener(e, mark));
}

export function idleSeconds() {
    return (Date.now() - lastActivity) / 1000;
}

export function reset() {
    mark();
}

export function logout() {
    const form = document.getElementById("logout-form");
    if (form) {
        form.submit();
    } else {
        window.location.href = "/login";
    }
}
