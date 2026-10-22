#!/usr/bin/env python3
"""
Membuat docs/COVERAGE.md: peta fitur aplikasi lama -> aplikasi baru.

Masukan:
  tools/Coverage/data/menus.txt     hasil  SELECT TYPEID,MENUID,MENUDESC,MENUURL,MENUPARENT FROM RFMENUX  (sqlcmd -W -s"|" -h -1)
  tools/Coverage/data/controls.txt  hasil  SELECT CTL_FUNC,CTL_LINK,CTL_DISPLAY,ACTIVE FROM CONTROL_MENU  (DB DATA modul 60)
  src/Panin.Web/**/*.razor          atribut [ModulePage(modul, rute)]
  src/Panin.Web/appsettings.json    Menu:LegacyUrlMap
  <repo lama> via git               daftar berkas aspx/ascx (untuk memastikan kontrol memang ada kodenya)

Pemakaian:  python tools/Coverage/make_coverage.py <path-repo-lama> [ref]  >  docs/COVERAGE.md

Status per kontrol ditulis tangan di KONTROL di bawah (hanya manusia yang bisa menilai "sudah setara"); menu diselesaikan otomatis.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

repo_old = sys.argv[1]
ref = sys.argv[2] if len(sys.argv) > 2 else "origin/multibrowser"
root = Path(__file__).resolve().parents[2]
data = root / "tools" / "Coverage" / "data"

# ---- halaman baru
pages = {}
for f in (root / "src" / "Panin.Web").rglob("*.razor"):
    for m in re.finditer(r'@attribute \[ModulePage\("(\d+)", "([^"]+)"\)\]', f.read_text(encoding="utf8")):
        pages[(m.group(1), m.group(2).lower())] = f.relative_to(root).as_posix()

legacy = {k.lower(): v for k, v in json.loads((root / "src" / "Panin.Web" / "appsettings.json").read_text(encoding="utf8"))["Menu"]["LegacyUrlMap"].items()}


def resolve(module, url):
    key = url.strip().rstrip("/").lower()
    target = legacy.get(module + "|" + key) or legacy.get(key) or key
    route = target.split("?")[0].rstrip("/") or "/"
    return pages.get((module, route.lower()))


# ---- berkas lama
files = subprocess.run(["git", "-C", repo_old, "ls-tree", "-r", "--name-only", ref], capture_output=True, text=True, encoding="utf-8").stdout.split("\n")
existing = {f.split("/")[-1].lower() for f in files if re.search(r"\.(aspx|ascx)$", f, re.I)}

# ---- status kontrol (CONTROL_MENU.CTL_FUNC)
SELESAI, SEBAGIAN, BELUM, MATI = "Selesai", "Sebagian", "BELUM", "Tidak ada kode"
KONTROL = {
    "Call": (SELESAI, "Form Call (`/collection/debitur/call`) termasuk bagian Hasil"),
    "Visitation": (SELESAI, "Form Visit (`/collection/debitur/visit`)"),
    "Letter": (SELESAI, "Form Letter (`/collection/debitur/letter`)"),
    "ExceptionLetter": (SELESAI, "Account Exception Letter"),
    "accmon": (SELESAI, "Monitoring Debitur"),
    "recmon": (SELESAI, "Monitoring Recovery/NPL (tautan Janji Debitur dan View News ada; Upload Data AYDA, Upload Visit belum)"),
    "worklistmonitoring": (SEBAGIAN, "Distribusi Debitur: pindah debitur selesai; tombol distribusi ulang (`SP_WORKLIST_DISTRIBUTION_*`) dan Excel agen visit (`SP_GENERATE_EXCEL_VISIT*`) belum"),
    "news": (SEBAGIAN, "News View selesai; Add News (editor HTML, unggah gambar) dan edit berita belum"),
    "MAINCOL": (SELESAI, "Digantikan oleh layar detail debitur (`/collection/debitur/detail`)"),
    "00020": (SEBAGIAN, "Data nasabah tampil di kartu Nasabah; penyuntingan hanya telepon/alamat lewat bagian Hasil Call/Visit"),
    "00021": (SEBAGIAN, "Idem (pekerjaan): tampil, penyuntingan penuh belum"),
    "00022": (SEBAGIAN, "Idem (pasangan): tampil, penyuntingan penuh belum"),
    "00023": (SEBAGIAN, "Idem (pekerjaan pasangan): tampil, penyuntingan penuh belum"),
    "accsrcmon": (BELUM, "Search Recovery (`AccountSearchMonitoring.ascx`)"),
    "list": (BELUM, "Recovery Kelolaan (`List.ascx`)"),
    "monhist": (BELUM, "Monitoring History"),
    "Contact": (BELUM, "Contact info (`CustContactInfo.ascx`)"),
    "CorespAddr": (BELUM, "Edit alamat korespondensi"),
    "Relation": (BELUM, "Relation account"),
    "agunan": (BELUM, "Agunan kredit (`AgunanKredit.ascx`), termasuk unggah dan lihat gambar agunan"),
    "agunanCAR": (BELUM, "Agunan mobil/motor"),
    "agunanDEP": (BELUM, "Agunan lain-lain"),
    "agunanTNH": (BELUM, "Agunan tanah dan bangunan"),
    "asuransijiwa": (BELUM, "Asuransi jiwa"),
    "asuransikerugian": (BELUM, "Asuransi kerugian"),
    "DetailTunggakan": (BELUM, "Panel Detail Tunggakan di layar detail (laporan Detail Tunggakan sudah ada di menu Report)"),
    "Payment Schedule": (BELUM, "Payment schedule"),
    "PayOff": (BELUM, "Pay off"),
    "riwayatbayar": (BELUM, "Riwayat pembayaran (`SP_RIWAYAT_BAYAR_LIHAT`)"),
    "notes": (BELUM, "Notes (`SP_GET_NOTES`, `SP_INSERT_NOTES`)"),
    "Meeting": (BELUM, "Meeting"),
    "Skip Tracing": (BELUM, "Skip tracing"),
    "Lelang": (BELUM, "Balai Lelang (`SAVE_DATA_LELANG`) dan History Lelang"),
    "Recovery": (BELUM, "Workflow Recovery (`Recovery.ascx`, `Treatment_Recovery`, `Information_Recovery`, halaman `Recovery/*`)"),
    "callrec": (BELUM, "Call versi Recovery (`Call_Rec.ascx`)"),
    "visitrec": (BELUM, "Visit versi Recovery (`visit_rec.ascx`)"),
    "OtherHist": (BELUM, "History accrued interest (`History/AccrInterestHistory.ascx`)"),
}

menus = []
for line in (data / "menus.txt").read_text(encoding="utf8").splitlines():
    p = line.split("|")
    if len(p) == 5 and p[0].strip().isdigit():
        menus.append([x.strip() for x in p])

controls = []
for line in (data / "controls.txt").read_text(encoding="utf8").splitlines():
    p = line.split("|")
    if len(p) == 4 and p[3].strip() in ("0", "1"):
        controls.append([x.strip() for x in p])

MODUL = {"60": "Collection", "70": "MIS Report", "99": "Maintenance", "40": "LOS (di luar lingkup)"}

out = []
w = out.append
w("# Peta fitur: aplikasi lama -> aplikasi baru\n")
w("Dibuat otomatis oleh `tools/Coverage/make_coverage.py` dari menu `RFMENUX` (DB LOGIN dev), `CONTROL_MENU` (DB DATA dev), dan halaman Blazor yang ada.")
w("Data menu dan kontrol berasal dari DB **dev**; menu/kontrol di produksi bisa berbeda dan harus dicocokkan ulang (jalankan ulang skrip dengan ekspor dari produksi).\n")

# ---- menu
total = done = 0
rows = []
for typ, mid, desc, url, parent in menus:
    if not url or url.upper() == "NULL":
        continue
    if typ == "40":
        continue
    total += 1
    page = resolve(typ, url)
    if page:
        done += 1
    rows.append((typ, mid, desc, url, page))

w("## 1. Menu\n")
w(f"**{done} dari {total}** menu yang punya halaman (modul 60, 70, 99) sudah punya halaman baru.\n")
w("| Modul | Menu | URL lama | Status | Halaman baru |")
w("|---|---|---|---|---|")
for typ, mid, desc, url, page in rows:
    if page:
        status, where = "Selesai", f"`{page}`"
    else:
        status, where = "**BELUM**", ""
    short = url if len(url) <= 70 else url[:67] + "..."
    w(f"| {typ} {MODUL.get(typ, '')} | {desc} | `{short}` | {status} | {where} |")

# ---- kontrol
w("\n## 2. Fungsi layar detail dan monitoring (`CONTROL_MENU`)\n")
w("Setiap baris adalah satu fungsi yang bisa dipanggil layar lama (`GET_CONTROL`). \"Tidak ada kode\" = entri ada di tabel tetapi berkasnya tidak ada di repo (warisan LOS) atau tidak aktif; **konfirmasi ke pengguna bahwa fungsi itu memang tidak dipakai**.\n")
w("| Fungsi | Kontrol lama | Status | Keterangan |")
w("|---|---|---|---|")
counts = {SELESAI: 0, SEBAGIAN: 0, BELUM: 0, MATI: 0}
for func, link, display, active in controls:
    name = link.split("/")[-1].lower()
    if func in KONTROL:
        status, note = KONTROL[func]
    else:
        status, note = MATI, "Berkas tidak ada di repo" if name not in existing else "Tidak aktif atau tidak dipakai menu"
        if active == "1" and name in existing:
            status, note = BELUM, "Berkas ada di repo tetapi belum dinilai; periksa apakah dipakai"
    if active == "0" and func not in KONTROL:
        status, note = MATI, "Tidak aktif (`ACTIVE=0`)"
    counts[status] += 1
    shown = status if status != BELUM else "**BELUM**"
    w(f"| `{func}` | `{link.split('/')[-1]}` | {shown} | {note} |")
w(f"\nRingkasan: Selesai {counts[SELESAI]}, Sebagian {counts[SEBAGIAN]}, **BELUM {counts[BELUM]}**, tidak ada kode {counts[MATI]}.")

print("\n".join(out))
