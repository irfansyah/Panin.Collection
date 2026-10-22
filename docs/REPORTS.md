# Report: inventaris dan rencana migrasi

Sumber: `origin/multibrowser` (`LOSCOLLECTION/Report`, `Framework/clsReportSys.cs`, `Framework/clsFramework.cs`) dan salinan lokal DB DATA modul 60
(tabel `PIVOTLIST`, view `VW_REPORTSET`). Belum ada kode report di aplikasi baru; dokumen ini hasil pemetaan.

## Yang ternyata ada

"58 XtraReports" di rencana awal hanya sebagian. Menu Reporting (modul **60** dan **70**, menu 201010–201080) memanggil
`ReportSet.aspx?setid=<X>`, yang membaca `VW_REPORTSET` → **103 definisi** di tabel `PIVOTLIST` (8 kumpulan):

| Set | Menu | Jumlah | Jenis | Isi |
|---|---|---|---|---|
| `GEN` | Report (201010) | 26 | 21 **daftar** (grid dinamis), 3 pivot, 2 laporan kustom | Ad Hoc (+Recovery/KPM), Audit Trail, Call/Letter/Visit/Recovery History, Report NPL (+KPM), Tunggakan, Payment, Pending Item, Upload, Vintage 1/2, Unregistered Branch, Collector Productivity (kustom), Kebutuhan & Time Spent Kolektor (pivot) |
| `DT` | Daily Tracking (201020) | 13 | 1 pivot + 12 kustom | Nasional/Region/Branch × (semua, Product Group, Product Type, Product Name) |
| `LE` | Leading Edge (201030) | 12 | kustom | idem |
| `LD` | Lag Delinquency (201040) | 12 | kustom | idem |
| `FR` | Flow Rate (201050) | 24 | pivot | Account/Amount × Nasional/Region/Branch × produk |
| `WASIS` | Was Is (201060) | 1 | kustom | |
| `PRK` | Daily Tracking PRK (201070) | 4 | kustom | |
| `KPM` | Report Khusus KPM (201080) | 7 | kustom | |

Tiga mesin yang berbeda, bukan satu:

1. **Daftar dinamis** (`ReportList.aspx`, ±24 definisi): kolom, filter, dan kondisi tersimpan di `PIVOTLIST` (`PV_COLUMNLIST`, `PV_FILTERLIST`, `PV_COND`).
   Referensi kolom berbentuk `@[VIEW|KOLOM]:Judul:ringkasan` yang diterjemahkan menjadi `[VIEW].[KOLOM]` (kamus rumus `FRAMEWORK_FORMULAFIELD`, `FRAMEWORK_FOREIGNKEY`
   dan `FRAMEWORK_LINDEDSERVER` kosong di salinan lokal, jadi praktis satu view per laporan). Filter bertipe tanggal/rentang tanggal (`d`, `dr`, `df`, `dt`), daftar (`l`, dari SQL referensi),
   angka (`i`, `ir`), bulan–tahun (`m`, `mr`), atau teks (LIKE). Ringkasan kolom: `groupby`, `count`, `sum`, `avg`, `min`, `max`, `groupcount`.
2. **Pivot dinamis** (`ReportPivot.aspx`, ±28 definisi, termasuk 24 Flow Rate): sama sumbernya, tetapi `PV_ROWLIST`/`PV_COLUMNLIST`/`PV_DATALIST` menjadi baris/kolom/nilai pivot.
3. **Laporan kustom** (`ReportCustom.aspx`, ±58 definisi): kelas `XtraReport` di `Report/Template/PV_*.cs` yang dibuat lewat refleksi (`AssemblyObj`).
   Tiap kelas memanggil **satu SP** `SP_VW_REPORT_*` / `SP_VW_REKAP_*` (48 SP ada di salinan lokal) dengan parameter dari filter, lalu menaruh hasilnya ke satu `xrPivotGrid`.
   Tata letaknya (field baris/kolom/nilai, format `#,#.00`) ada di `*.Designer.cs`. Keluarga: Daily Tracking, Leading Edge, Delinquency, Daily PRK, Was/Is, Kolektibilitas KPM, Rincian/Alasan NPL.

## Strategi yang diusulkan

Tidak menulis ulang 103 laporan satu per satu. Tiga komponen, berurutan menurut nilai/biaya:

| Tahap | Cakupan | Pendekatan | Perkiraan |
|---|---|---|---|
| R1 **Daftar dinamis** | ±24 definisi `GEN` | Satu halaman `/reports/{id}` yang membaca `PIVOTLIST`; penerjemah `@[VIEW\|KOLOM]` → SQL **dengan daftar putih** (hanya view/kolom yang muncul di `PIVOTLIST`/katalog, nilai filter selalu parameter); filter sesuai tipe; DataGrid yang sudah ada (urut/filter/ekspor Excel) + total/group sederhana. | paling murah; satu mesin untuk semua |
| R2 **Pivot dinamis** | ±28 definisi (Flow Rate dkk.) | Komponen pivot sisi server/klien yang sama untuk R2 dan R3 (baris, kolom, nilai, total, format angka). | sedang |
| R3 **Pivot dari SP** | ±58 kustom | Tabel konfigurasi (`AssemblyObj` → SP, urutan parameter, field baris/kolom/nilai, format) **diekstrak dari `*.Designer.cs`**, bukan ditulis tangan; satu renderer. Judul/filter/tanggal mengikuti pola `PV_*`. | terbesar; risiko ada pada ekstraksi tata letak |

Ekspor: Excel (NPOI, sudah ada) untuk semuanya; PDF hanya bila operasional memintanya (layar lama mengekspor pdf/xls/rtf/csv dari grid daftar).

## Hal yang harus diputuskan/diukur sebelum mulai

- **Mana yang benar-benar dipakai.** 103 definisi tampak berlebihan (mis. 24 Flow Rate, 12 Delinquency × 4 jenis × 3 level). Minta operasional daftar yang dipakai; log akses report di aplikasi lama (bila ada) menentukan urutan kerja. Tanpa data itu semua dikerjakan berurutan R1 → R2 → R3.
- **Performa SP report.** Belum diukur. Salinan lokal berisi 40.788 pinjaman saja; SP `SP_VW_REPORT_*` pada data produksi harus diukur (catat di `docs/QUERY-REGISTRY.md`/`PERFORMANCE.md`) dan hasil berat perlu cache/semaphore seperti layar monitoring. `Report Upload`, `Audittrail`, `Ad Hock` bisa mengembalikan ratusan ribu baris → butuh batas baris + ekspor langsung ke Excel, bukan render penuh di grid.
- **Keamanan.** `PIVOTLIST` berisi potongan SQL (`PV_COND`, referensi filter `l`). Mesin baru tidak boleh mengeksekusi teks itu mentah: hanya `SELECT` dari view yang ada di daftar putih, kondisi diuji pola, dan data per user/cabang (bila laporan lama memfilternya lewat view) harus tetap dihormati. `Ad Hock*` (`PV_COND` 170+ karakter) perlu ditinjau satu per satu.
- **Menu modul 70 (MIS).** Menu Reporting yang sama ada di modul 70; sumbernya `LOSMIS` (ada `PV_Alasan_NPL`, `PV_Outs_Due_Npl`, `PV_Rincian_NPL` sendiri). Perlu dicek apakah set data/DB-nya sama dengan modul 60.
- **Laporan di luar `Report/`**: tautan di layar Recovery/Facility (`ReportHapusBuku`, `ReportHapusTagih`, `ReportRestrukturisasi`, `AYDA`, `SKPM`) dan memo `Recovery_Memo` belum dipetakan; mereka bagian dari Fase 3, bukan R1–R3.

## Status

| Item | Status |
|---|---|
| Inventaris definisi dan jenis mesin | selesai (dokumen ini) |
| R1 Daftar dinamis | belum |
| R2 Pivot dinamis | belum |
| R3 Pivot dari SP (ekstraksi tata letak) | belum |
