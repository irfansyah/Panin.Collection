# DevExpress di aplikasi lama dan penggantinya

Sumber: `origin/multibrowser` (hanya dibaca). Dihitung dari 204 berkas `.aspx/.ascx` dan semua `.cs`, 23 Oktober 2026.
Versi: halaman lama memakai **DevExpress 8.2** (Web/ASPxGridView/Editors), halaman yang lebih baru **17.2** (PivotGrid, XtraReports, HtmlEditor, TreeList).

Aturan penilaian (dari pemilik produk): komponen pengganti **cukup** bila fitur yang *benar-benar dipakai* aplikasi lama ada di pengganti. Fitur DevExpress yang tidak dipakai tidak perlu ditiru.
Karena itu tabel di bawah menghitung **pemakaian nyata di markup dan kode**, bukan daftar fitur komponennya.

## 1. Komponen yang dipakai

| Komponen | Jumlah | Dipakai di | Pengganti sekarang | Status |
|---|---|---|---|---|
| `ASPxGridView` | 48 grid di 39 berkas | monitoring, pencarian, riwayat, popup, LOSMNT, report daftar | `DataGrid.razor` | **sebagian** (lihat bagian 2) |
| `ASPxPivotGrid` | 3 | ReportPivot (modul 60 dan 70), MySimulasi (kosong, hanya kerangka) | `PivotView.razor` + `PivotBuilder` | cukup (bagian 3) |
| `XtraReport` + `ReportViewer` | 116 berkas kelas di `Report/Template`, 3 penampil | ReportCustom, ReportDetail | pivot dari SP (`CustomPivot`), daftar dari SP | cukup untuk isi; **cetak** belum diuji (bagian 4) |
| `ASPxTreeList` | 1 | `GroupMaker_USC` (hak menu per group) | daftar bertingkat dengan kotak centang di `GroupMaker.razor` | cukup (lama memanggil `ExpandAll`, tak ada fitur lain) |
| `ASPxHtmlEditor` | 1 + 2 formulir sisip gambar/tautan | `NewsEditor` (Add/Edit News) | **belum ada** (B05) | belum |
| `ASPxNewsControl` | 3 | Body, NewsView | `NewsView.razor` | cukup |
| `ASPxDataView` | 3 | ReportSet, ParamSet (daftar kartu) | halaman daftar report/parameter | cukup |
| `ASPxMenu` | 3 | Body tiap modul | `MenuTree` + tab modul | cukup |
| `ASPxPopupControl`, `ASPxPageControl`, `ASPxTabControl`, `ASPxRoundPanel`, `ASPxPanel`, `ASPxCallbackPanel` (30), `ASPxCallback` | 9, 9, 2, 4, 6, 30, 3 | tata letak dan AJAX | Blazor (state, dialog, tab) | cukup, tidak ada logika khusus |
| Editor: `ASPxDateEdit` 29, `ASPxTextBox` 18, `ASPxCheckBox` 20, `ASPxComboBox` 6, `ASPxListBox` 6, `ASPxSpinEdit` 4, `ASPxRadioButton` 6, `ASPxCalendar` 1 | | filter dan formulir | input HTML/`Select.razor` | cukup; pemilih tanggal memakai kontrol bawaan browser |

## 2. Fitur grid: dipakai atau tidak

*Kolom "`DataGrid` baru" menunjukkan keadaan **sebelum** G1–G6; keadaan sekarang di bagian 5.*

Hitungan berkas yang memakai (dari markup; bukan fitur yang tersedia di DevExpress).

| Fitur DevExpress | Dipakai? | Di mana | `DataGrid` baru | Tindakan |
|---|---|---|---|---|
| Baris filter per kolom (`ShowFilterRow`) | **ya, 26 berkas** | pencarian debitur/AYDA/hapus buku/PTP, monitoring, riwayat, BulkSurat, LOSMNT (6) | hanya satu kotak filter untuk semua kolom | **kurang** |
| Panel pengelompokan (`ShowGroupPanel`, seret kolom) | **ya, 7 berkas** | 6 halaman LOSMNT (User/Group/Parameter Maker dan Approval) dan News | tidak ada | **kurang** |
| Kelompok bawaan (`GroupBy` dari data) | ya, 1 | `ParamMaker`: kolom `GROUPBY` di `PARAMETERSYSTEMFIELD`; di salinan lokal hanya 1 baris konfigurasi berisi `GROUPBY` | tidak ada | ikut pengelompokan |
| Baris total (`ShowFooter`) | ya, 9 berkas | BulkSurat, popup riwayat/detail/templat | tidak ada | **kurang** (total report daftar sudah ada di `ReportRun`) |
| Ringkasan total/kelompok (`TotalSummary`, `GroupSummary`) | hanya dari `clsReportSys`/`clsListSys` | `clsListSys` tidak dipanggil halaman mana pun (kode mati). Di `clsReportSys`, 4 definisi report memakai `sum/count/avg/min/max` | total kolom pada `ReportRun` | cukup |
| Filter header (nilai unik per kolom) | ya, 3 | ReportAdhock (2) dan Simulasi/Search_Debitur | tidak ada | **kurang** (cukup bila baris filter punya pilihan nilai) |
| Urut kolom | ya (bawaan, tidak dimatikan) | semua | ya | cukup |
| Halaman (`PageSize`) | ya, 20 berkas, 12 per halaman umumnya | | 10/25/50/100, bawaan 25 | cukup |
| Pratinjau baris (`ShowPreview`) | ya, 3 | Rekomendasi, ReportAdhock | tidak ada | tinjau di B10 |
| Kolom perintah (pilih/centang/edit) | ya, 8 berkas | LOSMNT, News, RecoveryList | tombol dan kotak centang di halaman masing-masing | cukup bila halaman itu sudah dimigrasi |
| Template sel (tombol, tautan, ikon) | ya, 9 berkas | | `GridColumn.Template` | cukup |
| Pewarnaan baris (`HtmlRowPrepared`) | ya, 2 | | belum ada penanda baris | tinjau per halaman |
| Ekspor: Excel | ya | `ReportList`, `ReportAdhock` | ya (NPOI) | cukup |
| Ekspor: **PDF, RTF, CSV**, dan TXT (pivot) | **ya** | `ReportList` (pdf/xls/rtf/csv), `ReportAdhock`, `ReportPivot` (pdf/xls/rtf/txt) | hanya Excel | **kurang** (CSV mudah; PDF/RTF perlu keputusan) |
| Kolom beku, ubah ukuran/urutan kolom, pemilih kolom, edit di tempat, master-detail | **tidak dipakai** (0) | | tidak ada | tidak perlu |

Catatan: kolom dan ringkasan banyak yang dibuat lewat kode (`clsReportSys`), bukan markup; hitungan di atas mencakup keduanya untuk fitur yang terlacak.

## 3. Pivot

- `AllowDrag` (pengguna menyeret field) dibaca dari `PIVOTLIST.PV_SETTING`; di salinan lokal **semua** 40 definisi berisi `false:false` (23 lainnya kosong), jadi pivot tidak pernah interaktif. Pengganti statis sudah cukup.
- Total baris/kolom, format angka, ringkasan (sum, count, rata-rata, min, max, persen kolom/baris) dihitung `PivotBuilder`.
- Filter header pivot hanya dipakai sebagai opsi tampilan, bukan pada pivot yang ada di menu.
- Kurang: ekspor PDF/RTF/TXT (hanya Excel).
- Produksi mungkin punya `PV_SETTING` lain; cek sekali dengan SELECT sederhana di DB produksi.

## 4. XtraReports (laporan cetak)

- 116 berkas kelas (±58 report kustom) berisi satu pivot yang diberi data oleh satu SP. Isi sudah ditiru (`CustomPivot`, diekstrak dari `*.Designer.cs`).
- Penampil hanya memakai tiga tombol: **Cetak, halaman berikut, halaman terakhir**. Cetak/pemenggalan halaman di aplikasi baru **belum diuji**: perlu gaya `@media print` pada `PivotView` dan pengecekan di printer/PDF pemakai, atau ekspor PDF.
- Tidak ada pemakaian editor tata letak laporan untuk pengguna akhir (`ReportDesigner.aspx` hanya halaman kosong dengan kelas `MasterPage`; `ReportAdhock.aspx` membaca view `VW_RPT_ADHOCK` ke grid tanpa filter).

## 5. Status pekerjaan (23 Oktober 2026)

Dikerjakan di komponen bersama (`DataGrid.razor`, `GridEngine.cs`, `ExportButton.razor`, `PrintButton.razor`, `tailwind.css`) sehingga semua tabel mendapatkannya.

| # | Pekerjaan | Status | Cara memakai |
|---|---|---|---|
| G1 | Filter per kolom | **selesai** | Tombol "Filter kolom" (aktif bawaan). Kolom angka: `>100`, `<=5`, `=0`, `<>3`; kolom teks: "mengandung", awalan `=` untuk sama persis. Kolom teks dengan ≤ 60 nilai unik menyarankan nilainya (pengganti filter header). "Hapus filter" menghapus semua. Parameter `ColumnFilters`/`ColumnFiltersVisible` |
| G2 | Pengelompokan | **selesai** | Seret judul kolom ke panel "Kelompokkan" atau pilih dari daftar; bertingkat; buka/tutup per kelompok, "Buka semua"/"Tutup semua"; jumlah baris per kelompok; total kolom ber-`Summary` per kelompok. Parameter `AllowGrouping` |
| G3 | Baris total | **selesai** | `GridColumn.Summary = GridSummary.Sum/Count/Average/Min/Max`; baris menempel di bawah tabel, dihitung dari semua baris yang lolos filter. Sudah dipasang pada kolom jumlah/outstanding di Inquiry Debitur, Monitoring Debitur, Worklist, Bulk Surat, Surat Pengecualian |
| G4 | Ekspor CSV | **selesai** (CSV); PDF/RTF/TXT **menunggu jawaban operasional** | Tombol "CSV" di semua tabel dan di halaman report (daftar dan pivot). Format mengikuti `WriteCsv` lama: pemisah `;`, angka berkoma desimal, UTF-8 dengan BOM; sel diawali `= + @` diberi apostrof agar tidak dibaca sebagai rumus |
| G5 | Cetak | **selesai** (diuji dengan media cetak emulasi, bukan printer fisik) | Tombol "Cetak" membuka dialog cetak browser; gaya cetak menyembunyikan menu, header, footer, dan kontrol, melepas batas tinggi tabel, mengulang header/footer tabel di tiap halaman |
| G6 | Warna baris | **tersedia** | Parameter `RowClass` (`Func<TItem,string?>`). Dua pemakai lama (`ProcJatuhTempo`, `UC_ListDetail`) ada di halaman Recovery (B07) yang belum dimigrasi; dipasang saat halaman itu dibuat |

Uji: 22 tes unit baru (filter angka/teks, ringkasan, pengelompokan bertingkat dan lipat, CSV); uji browser di Inquiry Status Debitur (19 baris): filter DPD `>30` → 5 baris, filter nama → 1 baris, total outstanding 17.740.575.905, 3 kelompok Fasilitas dengan total per kelompok, seret kolom ke panel, unduh CSV (BOM, `;`, `2869422137,51`), media cetak (menu, header, footer, kontrol tersembunyi).
**Belum:** cetak ke printer fisik/PDF di komputer pemakai; menjalankan seret-lepas di browser selain Chrome/Edge (Firefox butuh data seret, jadi di sana gunakan pilihan kolom dari daftar); ekspor PDF/RTF/TXT.

## Metode (agar bisa diulang)

- Hitung tag: `grep -rhoE "<dx[a-z]*:[A-Za-z]+" --include=*.aspx --include=*.ascx` pada salinan sumber lama.
- Fitur grid: atribut `<Settings .../>`, `<SettingsBehavior .../>`, `<SettingsPager .../>`, dan API kode (`GroupBy`, `GroupSummary`, `TotalSummary`, `Write*`).
- Pemakaian runtime (misalnya apakah pengguna benar-benar mengelompokkan) **tidak** bisa dilihat dari kode. Ini hanya menunjukkan fitur yang *tersedia* bagi pengguna; keputusan akhir ada di operasional.
