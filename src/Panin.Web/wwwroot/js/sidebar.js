// Menu samping: ciut/lebar lewat tombol di header atau Ctrl+B (Cmd+B di Mac). Pilihan diingat di browser ini.
// Dimuat di <head> supaya keadaan terpasang sebelum halaman digambar (tanpa kedipan). Seluruh keadaan ada di atribut
// data-side pada <html>; CSS (site.css) yang mengubah tampilan, sehingga render ulang Blazor tidak mengganggunya.
(function () {
    var KEY = "panin.side-hidden";
    var root = document.documentElement;

    function stored() {
        try { return localStorage.getItem(KEY) === "1"; } catch (e) { return false; }
    }

    function apply(collapsed, remember) {
        if (collapsed) { root.setAttribute("data-side", "collapsed"); } else { root.removeAttribute("data-side"); }
        var label = collapsed ? "Tampilkan menu samping" : "Sembunyikan menu samping";
        document.querySelectorAll("[data-side-toggle]").forEach(function (b) {
            b.setAttribute("aria-expanded", collapsed ? "false" : "true");
            b.setAttribute("aria-label", label);
            b.setAttribute("title", label + " (Ctrl+B)");
        });
        if (remember) {
            try { localStorage.setItem(KEY, collapsed ? "1" : "0"); } catch (e) { /* mode privat atau storage diblokir: pilihan tidak diingat */ }
        }
    }

    function toggle() {
        // Fokus yang tertinggal di menu (setelah memilih item) membuat rel tetap melebar; lepaskan bila menu diciutkan.
        var a = document.activeElement;
        if (a && a.closest && a.closest(".side-panel")) { a.blur(); }
        apply(root.getAttribute("data-side") !== "collapsed", true);
    }

    apply(stored(), false);

    document.addEventListener("click", function (e) {
        if (e.target.closest && e.target.closest("[data-side-toggle]")) { toggle(); }
    });

    document.addEventListener("keydown", function (e) {
        var mac = navigator.platform.toUpperCase().indexOf("MAC") >= 0;
        if ((mac ? e.metaKey : e.ctrlKey) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === "b") {
            e.preventDefault();
            toggle();
        }
    });

    // Tombol dibuat Blazor setelah skrip ini jalan; samakan atributnya begitu tombol muncul.
    // Blazor menggambar ulang tombol saat sirkuit interaktif tersambung (dan bisa lagi kapan saja): samakan atributnya bila berbeda.
    var queued = false;
    new MutationObserver(function () {
        if (queued) { return; }
        queued = true;
        requestAnimationFrame(function () {
            queued = false;
            var b = document.querySelector("[data-side-toggle]");
            var collapsed = root.getAttribute("data-side") === "collapsed";
            if (b && b.getAttribute("aria-expanded") !== (collapsed ? "false" : "true")) { apply(collapsed, false); }
        });
    }).observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["aria-expanded"] });
})();
