# Sisa pekerjaan dan kebutuhannya (ringkas)

Ringkasan dari `docs/BACKLOG.md` untuk dibagikan. Fitur aplikasi lama yang **belum** ada di aplikasi baru, dan apa yang saya butuhkan agar bisa dikerjakan.
Menurut `docs/COVERAGE.md`: 8 menu lama masih belum punya halaman baru, dan sebagian fungsi layar detail belum ada. Tanpa butir-butir ini
aplikasi baru belum setara aplikasi lama, jadi **cutover belum boleh** (`docs/CUTOVER.md`).

## A. Fitur yang belum ada

| # | Fitur | Ukuran | Yang saya butuhkan |
|---|---|---|---|
| B05 | **Add/Edit News** (menu 5010) | M | **Keputusan**: editor sederhana (teks dengan format dasar, unggah gambar ke folder) atau editor HTML lengkap seperti lama (butuh komponen pihak ketiga dan lisensi/pemeliharaannya). Folder gambar berita dari server lama (`Image/News`). |
| B04 | **Unggah dan perbarui data**: Upload Data AYDA, Upload Visit, Manual Update, Upload Textfile, Upload Manual | L | Berkas contoh nyata untuk tiap jenis; aturan validasi (kolom wajib, format tanggal, ukuran maksimum); siapa yang boleh memakainya; apa yang terjadi bila ada baris salah (batal semua atau sebagian). Semua menulis ke DB. |
| B06 | **Panel detail debitur**: edit data nasabah/pasangan/pekerjaan/kontak/alamat, agunan (+ unggah gambar), asuransi, detail tunggakan, payment schedule, pay off, riwayat pembayaran, notes, meeting, skip tracing | L–XL | Ekspor `CONTROL_MENU` dari **produksi** (tahu mana yang aktif dipakai); tangkapan layar atau akses layar lama tiap panel; aturan siapa boleh mengubah apa; contoh data agunan/asuransi/gambar. |
| B07 | **Workflow Recovery, Lelang, Litigasi**, Search Recovery, Recovery Kelolaan, Monitoring History | XL | Penjelasan proses bisnis dan siapa terlibat; dokumen UAT/SIT modul ini (di sumber lama modul ini masih berubah, terakhir 2025-07) dan **baseline yang dibekukan**; akun uji dengan hak Recovery; data uji. Ini pekerjaan terbesar yang tersisa. |
| B08 | **Inquiry Data AYDA/WO** (menu 4020) dan perhitungan AYDA | M–L | Contoh layar dan hasil perhitungan AYDA dari aplikasi lama untuk dicocokkan; aturan perhitungan bila tidak seluruhnya ada di SP; data uji. |
| B09 | **Simulasi** (menu 6010): cari debitur dengan diskon, PSJT, restrukturisasi | L | Contoh perhitungan dari layar lama (masukan dan hasil) untuk mencocokkan rumus; apakah rumus ada di SP atau di kode lama. |
| B10 | **Popup dan templat**: cetak surat tunggal, templat SMS dan pengguna, ubah peringatan, rekomendasi, detail nasabah, riwayat lengkap, memo Call/Visit | M–L | Contoh keluaran tiap templat; konfirmasi mana yang masih dipakai; berkas templat bila ada di luar repo. |
| B11 | **Fasilitas lain** (`Atribut_user`, `Maintenance_Userid`, `Problem_Handlin`) dan alat `ReportDesigner`/`ReportAdhock` | S–M | **Konfirmasi** dari admin/pengguna apakah masih dipakai (tidak muncul di menu dev). |

## B. Yang sudah ada tetapi perlu bukti

| Hal | Yang saya butuhkan |
|---|---|
| Keluaran report (103 definisi) cocok dengan aplikasi lama | Akses hanya-baca ke staging/produksi untuk menjalankan `tools/Panin.ReportCheck`; beberapa keluaran lama (Excel/tangkapan layar) untuk report utama dengan parameter yang sama. |
| Cetak memo, Excel agen visit, report | Contoh cetakan/berkas lama untuk dibandingkan visual; pemakai mencoba di Excel/Word mereka. |
| SP lambat (Leading Edge per produk, Was Is) | DBA mengukur dan memperbaiki indeks/rencana eksekusi. |
| Panin.Worker (pengganti PANINSVC) | Isi `App.config` PANINSVC produksi (jendela waktu, path UNC, akun layanan); uji dengan berkas nyata. |
| Modul 70 (MIS) | String koneksi dan akun baca ke DB MIS; jaringan dari server aplikasi baru. |
| Menu produksi | Ekspor `RFMENUX`, `GRPMENUX`, `CONTROL_MENU` produksi (hanya baca). |

## C. Kecepatan, tampilan, keamanan

| Hal | Yang saya butuhkan |
|---|---|
| Bukti lebih cepat dari aplikasi lama | Akun uji di aplikasi lama (`http://192.168.88.11:15002`) atau pengukuran dari pengguna untuk layar utama; lingkungan uji beban mirip produksi. |
| Tampilan modern, responsif | Pedoman merek (logo, warna), contoh tampilan yang disukai, keputusan mode gelap, sesi umpan balik dengan 3–5 pengguna. Pass pertama sudah dikerjakan (lihat `docs/DESIGN.md`). |
| Keamanan | Tempat menyimpan rahasia produksi; rotasi credential lama; tinjauan keamanan; sertifikat HTTPS. |
| UAT dan pelatihan | Jadwal UAT per peran, staging mirip produksi, jendela perawatan. |

## Paritas komponen DevExpress

Aplikasi lama memakai DevExpress luas (48 grid, 3 pivot, 116 kelas XtraReport, TreeList, HtmlEditor, dll.). Hasil pemeriksaan di `docs/DEVEXPRESS-PARITY.md`:
`DataGrid` sudah dilengkapi (G1–G6): filter per kolom, pengelompokan, baris total, ekspor CSV, cetak, warna baris. **Sisa:** ekspor PDF/RTF/TXT, menunggu jawaban operasional apakah masih dipakai; cetak ke printer fisik belum diuji.

## Komponen pihak ketiga

Aplikasi baru **tidak memakai DevExpress** (aplikasi lama memakainya luas; lihat bagian di atas). Grid, pivot, ekspor Excel, dan report diganti komponen sendiri dan NPOI/HtmlSanitizer (semuanya sumber terbuka).
Satu-satunya tempat yang mungkin membutuhkan komponen pihak ketiga adalah editor HTML untuk B05; keputusannya ada di tabel A.
