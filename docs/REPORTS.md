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

## R1 — Daftar dinamis (selesai)

Menu Reporting (modul 60 dan 70) membuka `/reports?SetId=<GEN|DT|LE|LD|FR|WASIS|PRK|KPM>`: daftar report dalam set itu. Yang belum bisa dijalankan tetap tampil
bersama **alasannya** (pivot, kustom, kondisi kustom, view campuran). Memilih report membuka formulir filter + hasil + ekspor Excel.

Cara kerja: `ReportDefinitionParser` membaca `PIVOTLIST` → `ReportDefinition`; `ReportSqlBuilder` menyusun `SELECT TOP (n) [VIEW].[KOLOM] AS c0.. FROM [VIEW] WHERE 1=1 <filter>`;
`ReportService` menjalankan lewat `IModuleDbFactory` (DB modul), cache definisi 5 menit, pilihan filter 2 menit, maksimum 6 report bersamaan.

| Aspek | Perilaku |
|---|---|
| Nama view/kolom | hanya huruf/angka/spasi/titik/garis/`_`, selalu dikutip `[..]`; selain itu report ditandai tidak tersedia |
| Nilai filter | selalu parameter posisional `@1..@n`, tidak pernah disisipkan ke SQL |
| SQL pilihan filter (`:l:SELECT kode,, teks FROM ...`) | hanya SELECT sederhana: tanpa `;`, komentar, `INTO`, `EXEC`, `sys.*`, `sp_*`, `OPENROWSET`, dll.; `WHERE x=@n` merujuk filter sebelumnya (filter bertingkat KCU → KCP) |
| Batas baris | layar 5.000, ekspor 100.000 (pesan bila terpotong) |
| Total kolom | `SUM/COUNT/AVG/MIN/MAX` dari definisi kolom; `groupby` lama tidak ditiru |
| Rentang bulan–tahun (`mr`) | dibandingkan sebagai (tahun, bulan) utuh. **Beda dari lama**: layar lama membandingkan bulan dan tahun terpisah sehingga rentang lintas tahun (Nov 2025–Feb 2026) salah |
| Filter kosong | diabaikan (NULL), seperti lama |
| Menjalankan | tombol TAMPILKAN (lama menjalankan langsung saat halaman dibuka tanpa filter → seluruh view) |

Hasil terhadap salinan lokal (DB modul 60): set `GEN` = 26 definisi → **15 tersedia** (Ad Hock, Audittrail, Call/Letter/Visit/Recovery History, Detail Tunggakan,
Laporan Nasabah Akan NPL, Monitoring Debitur, Payment, Pending Item, Report Tunggakan, Report Upload, Unregistered Branch (2)). Yang belum:
4 dengan `PV_COND` (Ad Hock Recovery, Ad Hock Recovery KPM, Report NPL, Report NPL KPM: logika "baris terakhir per loan" memakai `@1`/`@5` mentah),
Letter Status Report (filter dari dua view), 2 kustom (Collector Productivity) dan 3 pivot (R2/R3). Setiap query tiap report tersedia, termasuk SQL pilihan filter,
dijalankan oleh tes integrasi terhadap view asli tanpa galat; uji browser: formulir, filter tanggal, filter bertingkat KCU→KCP (64 → 20 opsi), hasil, ekspor Excel.

**Belum terukur/teruji:** waktu query pada data produksi (view report bisa besar; salinan lokal 40 ribu pinjaman); modul 70 (halaman dan rute sama, DB modul 70 belum ada di lokal);
definisi `PV_COND` yang memerlukan keputusan (tulis ulang per report); `FRAMEWORK_FORMULAFIELD/FOREIGNKEY/LINDEDSERVER` (kosong di salinan lokal — bila produksi mengisinya,
kolom rumus dan join antar-view akan membuat report ditandai tidak tersedia sampai didukung).

## R2 dan R3 — Pivot (selesai, dengan catatan)

Satu mesin pivot (`Panin.Application/Reports/Pivot`) untuk kedua jenis report; DevExpress `ASPxPivotGrid`/`XRPivotGrid` tidak dipakai lagi.

| Komponen | Isi |
|---|---|
| `PivotBuilder` | Menyusun baris/kolom bertingkat, banyak ukuran, subtotal per level, total keseluruhan, urutan menurut ukuran bantu (`URUTAN`), persentase kolom/baris. Agregat dihitung dari agregat parsial (jumlah, banyak, min, maks) sehingga rata-rata di subtotal tepat (bukan rata-rata dari rata-rata). |
| `PivotQuery` | **R2**: agregasi di SQL (`GROUP BY` semua field baris/kolom; `SUM/COUNT/MIN/MAX` hanya untuk kolom angka; interval tahun/bulan/hari hanya untuk kolom tanggal; tipe kolom diketahui lewat `SELECT TOP 0`). **R3**: pengelompokan baris hasil SP di memori. |
| `PivotLayoutView` + `PivotView.razor` | Tabel HTML bertingkat dengan sel gabung; ekspor Excel datar (judul kolom digabung " / "). Tampilan dibatasi 30.000 sel; ekspor penuh. |
| `CustomPivotCatalog` | `pivot-layouts.json` (resource tertanam) berisi SP, jumlah parameter, indeks result set, dan tata letak 53 kelas `PV_*`, diekstrak dari `*.Designer.cs` lama oleh `tools/ReportExtract/extract_pivot_layouts.py` (`python tools/ReportExtract/extract_pivot_layouts.py <repo-lama>`; dijalankan ulang bila kelas lama berubah). |

Cakupan terhadap salinan lokal: seluruh set `DT` (13), `LE` (12), `LD` (12), `FR` (24), `WASIS` (1), `PRK` (4) tersedia; `KPM` 6 dari 7; di `GEN` kini 19 dari 26 (selain 15 daftar: Vintage 1/2,
Time Spent, Perkiraan Kebutuhan Kolektor). Tes integrasi menjalankan 46 SP kustom plus semua pivot dinamis terhadap DB nyata tanpa galat.

Beda/asumsi yang harus diketahui:

- **Istilah total.** `ShowRowTotals` = baris subtotal, `ShowRowGrandTotals` = baris total di bawah, `ShowColumnTotals`/`ShowColumnGrandTotals` = kolom subtotal / kolom total di kanan (pemahaman istilah DevExpress; **belum dicocokkan dengan keluaran aplikasi lama**, karena data lokal untuk sebagian besar view report kosong).
- **Pengurutan.** Nilai diurutkan menaik menurut tipe (angka, tanggal, teks tanpa membedakan huruf); `SortBySummaryInfo` lama dipetakan ke urutan menurut ukuran bantu (`URUTAN`). `SortMode` lain (9 pemakaian) tidak ditiru.
- **Sumber data.** View `VW_REPORT_DEBITUR_LOAD`, `VW_REPORT_FLOWRATE_*`, dan `VW_REPORT_DAILY_TRACKING_FULL` kosong di salinan lokal, dan sebagian besar SP kustom mengembalikan sedikit atau nol baris; tampilan dan total pada data berukuran produksi **belum dibandingkan** dengan layar lama.
- **Batas.** 200.000 kelompok data (R2), 400.000 sel hasil; di atasnya dipotong dengan pesan.
- **`SP_REPORT_WAS_IS_ANALYSIS` (Was Is Report) sangat lambat**: 193 detik untuk rentang satu hari di salinan lokal (40 ribu pinjaman). Tidak dijalankan di tes; perlu ditinjau/diindeks sebelum dipakai di produksi (lihat `docs/QUERY-REGISTRY.md`).
- Timeout SP kustom mengikuti nilai lama (dibagi sepuluh bila > 3600), mis. 1000 detik; semuanya lewat batas 6 report bersamaan.

## Status

| Item | Status |
|---|---|
| Inventaris definisi dan jenis mesin | selesai |
| R1 Daftar dinamis | selesai |
| R2 Pivot dinamis | selesai (belum dibandingkan dengan keluaran lama pada data nyata) |
| R3 Pivot dari SP | selesai untuk 53 kelas; 1 dengan laporan berpita (`PV_Alasan_NPL`: pivot + daftar alasan) belum |
| R1b `PV_COND` (4 report recovery/KPM) | belum |
| Collector Productivity (+ Recovery) — laporan berpita dari `usp_ViewReportCollProd` | belum |
| Letter Status Report (filter dari dua view) | belum |
