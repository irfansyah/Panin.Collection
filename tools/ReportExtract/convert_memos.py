#!/usr/bin/env python3
"""
Mengubah lima halaman memo cetak aplikasi lama (LOSCOLLECTION/Report/*.aspx) menjadi templat HTML statis dengan penanda {{NAMA}}.

    python tools/ReportExtract/convert_memos.py <path-repo-lama> [ref]

Keluaran: src/Panin.Web/Templates/Memo/*.html. Tata letak, teks, dan gaya disalin apa adanya dari markup lama; yang dibuang hanya
direktif server (<%@ %>, <form runat>, tombol "Export to Word"), dan setiap <asp:Label ID="X"> menjadi {{X}}. Pemetaan label -> kolom view
(`SELECT * FROM VW_REPORT_* WHERE ACC_NO = @1`) ada di Panin.Infrastructure/Memos/MemoCatalog.cs, disalin dari kode-behind lama.
"""
import re
import subprocess
import sys
from pathlib import Path

repo, ref = sys.argv[1], (sys.argv[2] if len(sys.argv) > 2 else "origin/multibrowser")
out = Path(__file__).resolve().parents[2] / "src" / "Panin.Web" / "Templates" / "Memo"
out.mkdir(parents=True, exist_ok=True)

FILES = {
    "skpm": "SKPM",
    "hapus-buku": "ReportHapusBuku",
    "hapus-tagih": "ReportHapusTagih",
    "restrukturisasi": "ReportRestrukturisasi",
    "ayda": "AYDA",
}

for name, old in FILES.items():
    src = subprocess.run(["git", "-C", repo, "show", f"{ref}:LOSCOLLECTION/Report/{old}.aspx"], capture_output=True, text=True, encoding="utf-8-sig").stdout
    s = src.replace("\r\n", "\n")
    s = re.sub(r"<%@.*?%>", "", s, flags=re.S)                 # direktif
    s = re.sub(r"<%--.*?--%>", "", s, flags=re.S)              # komentar server
    s = re.sub(r"<!--\s*#include[^>]*-->", "", s)              # server-side include
    s = re.sub(r"<asp:Button\b[^>]*?/>", "", s, flags=re.S)    # tombol export
    s = re.sub(r"</?form\b[^>]*>", "", s, flags=re.I)
    s = re.sub(r"<head\s+runat=\"server\"\s*>", "<head>", s, flags=re.I)

    def label(m):
        attrs = m.group(1)
        ident = re.search(r'\bID="([^"]+)"', attrs, re.I)
        return "{{" + ident.group(1) + "}}" if ident else ""

    def checkbox(m):
        text = re.search(r'\bText="([^"]*)"', m.group(1))
        return '<input type="checkbox" /> <label>' + (text.group(1) if text else "") + "</label>"

    s = re.sub(r"<asp:CheckBox\b([^>]*?)/>", checkbox, s, flags=re.S)
    s = re.sub(r"<asp:Label\b([^>]*?)/>", label, s, flags=re.S)
    s = re.sub(r"<asp:Label\b([^>]*?)>.*?</asp:Label>", label, s, flags=re.S)
    s = re.sub(r"<meta[^>]*charset=[^>]*>", '<meta charset="utf-8" />', s, flags=re.I)
    s = re.sub(r"\n\s*\n+", "\n", s).strip() + "\n"
    leftovers = re.findall(r"runat=|<asp:|<%", s)
    (out / f"{name}.html").write_text(s, encoding="utf-8")
    tokens = sorted(set(re.findall(r"\{\{(\w+)\}\}", s)))
    print(f"{name}: {len(s.splitlines())} baris, token={tokens}, sisa-server={leftovers}", file=sys.stderr)
