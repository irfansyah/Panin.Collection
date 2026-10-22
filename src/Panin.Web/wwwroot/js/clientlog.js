// Mengirim galat JavaScript dan kegagalan sambung sirkuit ke /api/client-log supaya tidak hanya terlihat di konsol browser.
// Maksimal 10 laporan per halaman dibuka; server juga membatasi per IP. Tidak ada data isian pengguna yang dikirim.
(function () {
    var sent = 0;
    var MAX = 10;

    function post(kind, data) {
        if (sent >= MAX) {
            return;
        }

        sent++;
        var meta = document.querySelector('meta[name="trace-id"]');
        var body = JSON.stringify(Object.assign({ kind: kind, url: location.pathname, pageTrace: meta ? meta.content : null }, data));
        try {
            if (navigator.sendBeacon && navigator.sendBeacon('/api/client-log', new Blob([body], { type: 'application/json' }))) {
                return;
            }

            fetch('/api/client-log', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: body, keepalive: true });
        } catch (e) {
            // melaporkan galat tidak boleh menimbulkan galat baru
        }
    }

    window.addEventListener('error', function (e) {
        post('error', { message: e.message, source: e.filename, line: e.lineno, column: e.colno, stack: e.error && e.error.stack });
    });

    window.addEventListener('unhandledrejection', function (e) {
        var r = e.reason;
        post('unhandledrejection', { message: String((r && r.message) || r), stack: r && r.stack });
    });

    // Sirkuit Blazor putus/ditolak: penting untuk menelusuri keluhan "layar mati sendiri".
    document.addEventListener('components-reconnect-state-changed', function (e) {
        var state = e.detail && e.detail.state;
        if (state === 'failed' || state === 'rejected' || state === 'paused') {
            post('circuit-' + state, { message: 'Status sambungan sirkuit: ' + state });
        }
    });
})();
