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
| `CustomPivotCatalog` | `pivot-layouts.json` (resource tertanam) berisi SP, jumlah parameter, indeks result set, dan tata letak 54 kelas `PV_*`, diekstrak dari `*.Designer.cs` lama oleh `tools/ReportExtract/extract_pivot_layouts.py` (`python tools/ReportExtract/extract_pivot_layouts.py <repo-lama>`; dijalankan ulang bila kelas lama berubah). |

Cakupan terhadap salinan lokal: seluruh set `DT` (13), `LE` (12), `LD` (12), `FR` (24), `WASIS` (1), `PRK` (4) dan `GEN` (26) tersedia, begitu pula `KPM` (7 dari 7). Tes integrasi menjalankan 46 SP kustom plus semua pivot dinamis terhadap DB nyata tanpa galat.

Beda/asumsi yang harus diketahui:

- **Istilah total.** `ShowRowTotals` = baris subtotal, `ShowRowGrandTotals` = baris total di bawah, `ShowColumnTotals`/`ShowColumnGrandTotals` = kolom subtotal / kolom total di kanan (pemahaman istilah DevExpress; **belum dicocokkan dengan keluaran aplikasi lama**, karena data lokal untuk sebagian besar view report kosong).
- **Pengurutan.** Nilai diurutkan menaik menurut tipe (angka, tanggal, teks tanpa membedakan huruf); `SortBySummaryInfo` lama dipetakan ke urutan menurut ukuran bantu (`URUTAN`). `SortMode` lain (9 pemakaian) tidak ditiru.
- **Sumber data.** View `VW_REPORT_DEBITUR_LOAD`, `VW_REPORT_FLOWRATE_*`, dan `VW_REPORT_DAILY_TRACKING_FULL` kosong di salinan lokal, dan sebagian besar SP kustom mengembalikan sedikit atau nol baris; tampilan dan total pada data berukuran produksi **belum dibandingkan** dengan layar lama.
- **Batas.** 200.000 kelompok data (R2), 400.000 sel hasil; di atasnya dipotong dengan pesan.
- **`SP_REPORT_WAS_IS_ANALYSIS` (Was Is Report) sangat lambat**: 193 detik untuk rentang satu hari di salinan lokal (40 ribu pinjaman). Tidak dijalankan di tes; perlu ditinjau/diindeks sebelum dipakai di produksi (lihat `docs/QUERY-REGISTRY.md`).
- Timeout SP kustom mengikuti nilai lama (dibagi sepuluh bila > 3600), mis. 1000 detik; semuanya lewat batas 6 report bersamaan.

## R1b — Kondisi kustom, filter lintas view, dan report berpita (selesai)

Seluruh 26 definisi set `GEN` kini tersedia di salinan lokal.

| Kasus | Penanganan |
|---|---|
| **`PV_COND` "baris terbaru per kunci"** (Ad Hock Recovery, Ad Hock Recovery KPM, Report NPL, Report NPL KPM) | Hanya pola ini yang dikenali (regex ketat, nama tervalidasi, parameter harus ada): `AND [DATE] IN (SELECT MAX([DATE]) FROM view A WHERE A.kunci = [view].[kunci] AND (A.k1=@1 AND A.k2=@5)) OR [DATE] IS NULL`. Dibuat menjadi klausa terparameter: `([DATE] IS NULL OR [DATE] = (SELECT MAX(A.[DATE]) FROM view A WHERE A.kunci = view.kunci AND (@1 IS NULL OR A.k1=@1) AND (@5 IS NULL OR A.k2=@5)))` di samping semua filter. Kondisi lain tetap ditandai belum tersedia. |
| **Beda perilaku dengan layar lama (disengaja)** | SQL lama menempelkan `OR [DATE] IS NULL` tanpa tanda kurung, sehingga (a) filter KCP, nama, Product Group/Type **tidak berlaku** untuk baris bertanggal, dan (b) bila KCU atau Status Rekening tidak dipilih, baris bertanggal tidak muncul sama sekali (`A.KCU_CODE=NULL` tidak pernah benar). Itu hampir pasti bukan maksudnya, jadi di sini semua filter berlaku dan tanpa KCU/Status dipakai baris terbaru dari semua. **Pada salinan lokal semua baris bertanggal NULL, jadi perbedaan ini belum teramati**; mohon dikonfirmasi ke pengguna report sebelum cutover. |
| **Letter Status Report** (filter KCU/KCP dari `VW_REPORT_AKAN_NPL`) | Layar lama menggabungkan view lain lewat `FRAMEWORK_FOREIGNKEY` (kosong di salinan lokal; tanpa relasi hasilnya perkalian silang). Filter dari view lain **tidak diterapkan** dan ditampilkan sebagai catatan di atas formulir. Bila produksi mengisi relasi itu, perlu keputusan apakah join didukung. |
| **Collector Productivity** (`usp_ViewReportCollProd`) | Daftar dari SP (result set ke-2) dengan 11 kolom dan total, keterangan supervisor/kolektor dari result set pertama (`CustomLists`). |
| **Collector Productivity Recovery** | Kelas report-nya tidak ada di sumber aplikasi lama yang dibaca (di sana report ini tidak bisa dibuka), tetapi `USP_VIEWREPORTCOLLPROD_RECOVERY` ada dan bentuk hasilnya sama; dipakai dengan tata letak yang sama. Perlu dikonfirmasi ke pemilik report. |

## Alat pemeriksa report (`tools/Panin.ReportCheck`)

Menjalankan **semua** report menu Reporting terhadap sebuah DB DATA, mencatat waktu dan jumlah baris tiap report, dan memeriksa konsistensi hasilnya dengan
perhitungan kedua yang independen. Dibuat agar bisa dijalankan pada staging atau produksi untuk mengukur waktu (belum terukur di data nyata) dan menangkap
kesalahan penyusunan pivot sebelum cutover.

```bash
dotnet run --project tools/Panin.ReportCheck -- --data "<connection string DB DATA>" --days 7 --json hasil.json
# atau tanpa --data: membaca user-secrets ConnectionStrings:Data:60 / env PANIN_CHECK_DATA
# opsi: --module 70, --only PIVOT65,PIVOT86, --skip PIVOT17, --max-raw 300000, --timeout 300
```

| Jenis report | Pemeriksaan independen |
|---|---|
| Daftar dinamis | jumlah baris hasil = `COUNT(*)` dengan klausa filter yang sama (bila tidak terpotong) |
| Pivot dinamis | baris mentah diambil lalu dikelompokkan **di memori** (jalur lain dari `GROUP BY` SQL), disusun ulang, dan dibandingkan sel demi sel (bentuk, label, nilai) |
| Pivot dari SP | jumlah tiap ukuran penjumlahan dari hasil SP = jumlah sel daun pivot |
| Daftar dari SP | hanya dijalankan dan diukur |

Keamanan: koneksi `ApplicationIntent=ReadOnly`, pernyataan lewat `ReadOnlyDb` (hanya SELECT dan SP report yang ada di katalog), dan sebelum menjalankan SP, definisinya dibaca dan
SP yang tampak menulis ke tabel permanen (selain `#temp`/`@tabel`) dilewati dengan alasan. Kode keluar 1 bila ada `DIFF` atau `ERROR`.

Hasil pada salinan lokal (rentang 10 tahun, tanpa PIVOT17): **86 OK**, 0 DIFF, 5 SKIP, 6 ERROR (semua timeout SP Leading Edge, lihat di bawah), 1 N/A (Alasan NPL).
Pemeriksaan yang berisi data nyata hanya Time Spent (78.636 baris) dan Vintage 1/2 karena view lain kosong di salinan lokal; **jalankan alat ini di staging/produksi untuk pemeriksaan sesungguhnya**.

**Temuan performa (SP report kustom), salinan lokal 40 ribu pinjaman**

| SP / report | Waktu |
|---|---|
| `SP_REPORT_WAS_IS_ANALYSIS` (Was Is, PIVOT17) | 193 dtk untuk 1 hari |
| `SP_VW_REPORT_LEADING_EDGE_*_PGROUP/PTYPE/PID` (Leading Edge per Product Group/Type/Name, 9 report) | 77 dtk untuk **1 hari** (hasil 38.080 baris untuk Branch); lebih dari 280 dtk untuk 7 hari; melewati timeout 1000 dtk untuk 10 tahun |
| `SP_VW_REPORT_LEADING_EDGE_BRANCH/REGION` | 7,4 dtk untuk 30 hari (168.640 baris hasil); 855 dtk / 280 dtk untuk 10 tahun |
| `SP_VW_REPORT_LEADING_EDGE_NATIONAL` | 1,8 dtk untuk 10 tahun |
| Lainnya (Daily Tracking, Lag Delinquency, PRK, KPM, daftar, pivot dinamis) | < 1,1 dtk (banyak yang kosong secara data) |

Perlindungan di aplikasi baru: SP kustom memakai pintu terpisah (maks. 2 bersamaan) agar tidak menghabiskan pintu report lain (6), dan timeout dibatasi 600 dtk.
Namun SP Leading Edge per produk **perlu dioptimalkan di sisi DB** (atau dibatasi rentang tanggalnya) sebelum dipakai di produksi.

## Status

| Item | Status |
|---|---|
| Inventaris definisi dan jenis mesin | selesai |
| R1 Daftar dinamis | selesai |
| R1b `PV_COND` (4), Letter Status, Collector Productivity (2) | selesai (lihat beda perilaku di atas) |
| R2 Pivot dinamis | selesai (belum dibandingkan dengan keluaran lama pada data nyata) |
| R3 Pivot dari SP | selesai untuk 54 kelas, termasuk `PV_Alasan_NPL` (pivot + tabel keterangan alasan di bawahnya sebagai tabel pendamping) |
| Pemeriksaan konsistensi + pengukuran waktu semua report (`Panin.ReportCheck`) | alat selesai; dijalankan di salinan lokal, belum di staging/produksi |
| Perbandingan keluaran dengan layar lama (tampilan DevExpress) pada data nyata | belum (butuh akses ke aplikasi lama dan data produksi) |

## Hasil `Panin.ReportCheck` pada DB bersama 192.168.88.11 (23 Oktober 2026, hanya baca)

Dijalankan terhadap `LOSPANINCOLLECTION` (ApplicationIntent=ReadOnly), modul 60, tiga putaran: jendela tanggal 7 hari; ulang dengan 540 hari (data terakhir diperbarui 7 Mei 2025);
report yang sangat lambat dijalankan terpisah dan dilewati pada putaran berikutnya.

| Hasil | Jumlah | Catatan |
|---|---|---|
| **DIFF** (hasil aplikasi berbeda dari perhitungan independen) | **0** | |
| OK | 81 pada putaran 540 hari, ditambah report yang diukur di putaran lain (PIVOT12 dan lainnya) | pivot dicocokkan dengan perhitungan kedua dari data mentah; daftar dicocokkan dengan `COUNT(*)` |
| SKIP | 2 (`PIVOT17` Was Is By Branch/Product Name, `PIVOT96` Alasan Tunggakan > 90 Hari) | SP lebih dari 30 detik, pemeriksaan independen tidak dijalankan |
| ERROR: batas waktu | `PIVOT07` Payment Report (180 dtk dengan jendela 540 hari; 3,8 dtk dengan 7 hari), `PIVOT11` Pending Item Report (180 dtk pada satu putaran, 112 dtk pada putaran lain), `PIVOT105` Ad Hock Recovery dan `PIVOT110` Ad Hock Recovery KPM (180 dtk), `PIVOT30` Leading Edge By Branch (600 dtk) | SP-nya lambat di DB ini; lihat `docs/PERFORMANCE.md` |
| Tidak diukur | 11 report Leading Edge lainnya (`PIVOT36/37/38/51–58`) | diperkirakan sama lambatnya dengan `PIVOT30`; dilewati supaya pemeriksaan selesai |

Yang **tidak** dibuktikan oleh pemeriksaan ini:
- Alat ini membandingkan hasil aplikasi baru dengan **perhitungan independen dari data yang sama**, bukan dengan **keluaran aplikasi lama**. Ia menangkap kesalahan di mesin pivot/daftar, bukan beda tafsir terhadap aplikasi lama.
- Banyak report (Daily Tracking, Flow Rate, KPM, PRK, Lag Delinquency) mengembalikan 0 baris atau 1 sel pada data ini, jadi hanya jalur data kosong yang teruji. Pivot yang berisi data nyata: Vintage 2 (16.267 baris, 15.175 baris mentah), Time Spent for Collector.
- Ini DB dev bersama; produksi belum diperiksa.
