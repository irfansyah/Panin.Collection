#!/usr/bin/env python3
"""
Mengekstrak tata letak report pivot kustom (kelas XtraReport PV_*) dari aplikasi lama menjadi JSON.

Sumber dibaca langsung dari git (branch referensi, read-only):  git show <ref>:<path>
Keluaran: src/Panin.Infrastructure/Reports/Custom/pivot-layouts.json

    python tools/ReportExtract/extract_pivot_layouts.py <path-repo-lama> [ref] > keluaran.json

Satu entri per kelas: SP yang dipanggil (+jumlah parameter), field baris/kolom/ukuran dengan judul, format, urutan, penyortiran menurut
ukuran bantu (SortBySummaryInfo), dan opsi total. Kelas yang tidak sesuai pola (lebih dari satu dataset, report berpita) ditandai
"irregular" dan tidak diekspor sebagai pivot otomatis.
"""
import json
import re
import subprocess
import sys

repo = sys.argv[1]
ref = sys.argv[2] if len(sys.argv) > 2 else "origin/multibrowser"
base = "LOSCOLLECTION/Report/Template"


def git(*args):
    return subprocess.run(["git", "-C", repo, *args], capture_output=True, text=True, encoding="utf-8-sig").stdout


files = git("ls-tree", "-r", "--name-only", ref, base).split()
classes = sorted({f.split("/")[-1].split(".")[0] for f in files if f.endswith(".cs") and "designer" not in f.lower()})
out = {}

AREA = {"RowArea": "row", "ColumnArea": "column", "DataArea": "data", "FilterArea": "filter"}

for cls in classes:
    code = git("show", f"{ref}:{base}/{cls}.cs")
    designer_path = next((f for f in files if f.lower() == f"{base}/{cls}.designer.cs".lower()), None)
    designer = git("show", f"{ref}:{designer_path}") if designer_path else ""
    if "XRPivotGridField" not in designer:
        continue

    entry = {"class": f"LOSCOLLECTION.Report.Template.{cls}"}
    calls = re.findall(r'GetData(Table|Set)\(\s*"EXEC (\w+)((?: @\d+,?)*)"\s*,\s*(\w+)\s*,\s*(\d+)', code)
    if len(calls) != 1 or "DetailReport" in code or "DataBindings" in code:
        entry["irregular"] = f"{len(calls)} panggilan data ({', '.join(c[0] for c in calls) or 'tidak ada'}); laporan berpita atau lebih dari satu dataset"
        out[cls] = entry
        continue

    kind, sp, params, _, timeout = calls[0]
    entry.update(sp=sp, params=len(re.findall(r"@\d+", params)), timeoutSeconds=int(timeout) // 10 if int(timeout) > 3600 else int(timeout))
    if kind == "Set":
        # tabel yang menjadi sumber pivot: xrPivotGrid1.DataSource = dtN, dtN = ds.Tables[k]
        src = re.search(r"xrPivotGrid1\.DataSource = (\w+);", code)
        idx = re.search(rf"DataTable {src.group(1)} = \w+\.Tables\[(\d+)\]", code) if src else None
        if not idx:
            entry["irregular"] = "sumber pivot dari dataset tidak terbaca"
            out[cls] = entry
            continue

        entry["dataTable"] = int(idx.group(1))
        header = re.search(r'\.Text = (\w+)\.Rows\[0\]\["(\w+)"\]', code)
        if header:
            entry["periodColumn"] = header.group(2)
    else:
        entry["dataTable"] = 0

    fields = {}
    for m in re.finditer(r"this\.(\w+)\.([\w.]+) = (.+?);\s*$", designer, re.M):
        name, prop, value = m.groups()
        if not name.lower().startswith(("fld", "xrpivotgridfield")) and name not in re.findall(r"this\.(\w+) = new DevExpress\.XtraReports\.UI\.PivotGrid\.XRPivotGridField", designer):
            continue
        f = fields.setdefault(name, {"id": name})
        value = value.strip()
        if prop == "Area":
            f["area"] = AREA.get(value.split(".")[-1], value)
        elif prop == "AreaIndex":
            f["index"] = int(value)
        elif prop == "FieldName":
            f["field"] = value.strip('"')
        elif prop == "Caption":
            f["caption"] = value.strip('"')
        elif prop == "Visible":
            f["visible"] = value == "true"
        elif prop == "CellFormat.FormatString":
            f["cellFormat"] = value.strip('"')
        elif prop == "ValueFormat.FormatString":
            f["valueFormat"] = value.strip('"')
        elif prop == "SortBySummaryInfo.Field":
            f["sortBy"] = value.replace("this.", "")
        elif prop == "SortMode":
            f["sortMode"] = value.split(".")[-1]
        elif prop == "Options.ShowGrandTotal":
            f["grandTotal"] = value == "true"
        elif prop == "Options.ShowTotals":
            f["totals"] = value == "true"

    # nama id -> field sumber untuk SortBySummaryInfo
    by_id = {k: v for k, v in fields.items() if "field" in v}
    for v in by_id.values():
        if "sortBy" in v:
            v["sortBy"] = by_id.get(v["sortBy"], {}).get("field")
    entry["fields"] = sorted(
        ({k: v for k, v in f.items() if k != "id"} for f in by_id.values() if "area" in f),
        key=lambda f: ({"row": 0, "column": 1, "data": 2}.get(f["area"], 3), f.get("index", 0)),
    )

    opts = {}
    for prop, key in (("ShowRowTotals", "rowTotals"), ("ShowRowGrandTotals", "rowGrandTotals"), ("ShowColumnTotals", "columnTotals"), ("ShowColumnGrandTotals", "columnGrandTotals")):
        m = re.search(rf"OptionsView\.{prop} = (true|false);", designer)
        if m:
            opts[key] = m.group(1) == "true"
    entry["options"] = opts
    out[cls] = entry

json.dump(out, sys.stdout, indent=2, ensure_ascii=False)
print()
