# Serah terima dan daftar periksa cutover

Dokumen kerja untuk hari-hari menjelang dan saat aplikasi lama dimatikan. Detail teknis tidak diulang di sini; lihat
`docs/PARALEL.md` (uji paralel, sinkronisasi DB LOGIN, langkah tukar nama DB, rollback), `docs/WORKER.md` (unggah harian),
`docs/LOGGING.md` (log dan penelusuran galat), `docs/REPORTS.md` (report), `docs/COVERAGE.md` dan `docs/BACKLOG.md` (fitur yang belum ada).

> **Gerbang paling awal.** Cutover tidak dilakukan selama `docs/BACKLOG.md` masih memuat fitur lama yang belum ada dan belum disepakati secara tertulis
> oleh pemilik produk bahwa fitur itu tidak dipakai. Kriteria berhasil: semua fitur ada, lebih cepat, tampilan lebih modern.

## 1. Gerbang masuk (semua harus "ya")

| # | Pemeriksaan | Bukti |
|---|---|---|
| 1 | Setiap baris `docs/BACKLOG.md` bagian A selesai, atau dinyatakan tidak dipakai oleh pemilik fitur (tertulis). | Tabel B01–B11 kosong/ditutup |
| 2 | `docs/COVERAGE.md` dihasilkan ulang dari menu dan `CONTROL_MENU` **produksi**; tidak ada menu **BELUM**. | Keluaran `tools/Coverage/make_coverage.py` |
| 3 | Seluruh report sudah dijalankan di staging dengan `tools/Panin.ReportCheck`; tidak ada `DIFF`/`ERROR`, waktu tiap report dapat diterima, dan beberapa keluaran penting dicocokkan dengan aplikasi lama. | JSON hasil `ReportCheck` + catatan pencocokan |
| 4 | Uji beban pada lingkungan sejenis produksi: **> 700 pengguna bersamaan**, tanpa galat, waktu respons lebih baik dari aplikasi lama untuk layar utama. | `docs/LOAD-TESTING.md` diperbarui |
| 5 | UAT oleh tiap peran (collector, supervisor, recovery, admin, MIS) ditandatangani. | Berita acara UAT |
| 6 | Tinjauan keamanan selesai (rahasia, rotasi credential, hak menu, header/HTTPS). | Daftar temuan tertutup |
| 7 | Tinjauan tampilan dengan pengguna (BACKLOG B19) selesai. | Catatan sesi umpan balik |
| 8 | Latihan rollback dan latihan cutover penuh di staging berhasil. | Catatan waktu tiap langkah |

## 2. Konfigurasi produksi

Nilai di `appsettings.json` hanyalah bawaan pengembangan. Di produksi pakai variabel lingkungan atau secret store (jangan berkas yang masuk Git).

| Kunci | Isi produksi | Catatan |
|---|---|---|
| `ConnectionStrings:Login` | DB LOGIN **baru** | Akun terpisah, hak minimum |
| `ConnectionStrings:Data:60` / `:99` / `:70` | DB DATA modul (atau dibiarkan kosong agar dibaca dari `RFMODULE.DB_*`) | Modul 70 di server lain (`PANINMIS`); uji koneksi dari server aplikasi |
| `ParallelRun:Enabled` | `true` selama uji paralel, **`false` saat cutover** | Mengaktifkan kembali User/Group Maker-Approval dan ganti password |
| `Menu:LegacyUrlMap` | Daftar `MENUURL` lama → rute baru; sesuaikan dengan `RFMENUX` produksi | Hasilkan `docs/COVERAGE.md` untuk memeriksa |
| `Letters:TemplateDirectory`, `Letters:OutputDirectory` | Folder templat surat dan keluaran yang bisa ditulis | Templat `surat.xls` ikut tersalin ke keluaran build |
| `Letters:ProtectionPassword` | **Ganti** (bawaan lama `rahasia` diketahui umum) dan simpan sebagai rahasia | BACKLOG B17 |
| `News:ImageDirectory` | Salinan folder `Image/News` dari server lama | Tanpa ini gambar berita tidak tampil |
| `DataAccess:ReadOnlyProcedures` | Daftar SP yang boleh dijalankan lewat koneksi baca-saja (hanya untuk DB bersama) | Lihat `docs/QUERY-REGISTRY.md` |
| `Session:*` | Detak, batas terbengkalai, peringatan diam | Bawaan 60 / 180 / 20 / 5 |
| `Diagnostics:SlowQueryMs`, `Diagnostics:Enabled` | Ambang query lambat; `Enabled` hanya untuk investigasi | `docs/PERFORMANCE.md` |
| `Serilog:*` | `File.path` **absolut di luar folder aplikasi**; tambah sink Seq/OTLP bila ada | `docs/LOGGING.md` |
| `AllowedHosts`, header forwarded, HTTPS | Nama host sebenarnya di belakang reverse proxy | `UseForwardedHeaders` aktif |
| Penyimpanan kunci Data Protection | Satu lokasi bersama bila lebih dari satu instance | Tanpa ini cookie/tiket tidak sah antar instance |

Worker unggah harian (`src/Panin.Worker`) memakai kunci `Upload:*` dan `ConnectionStrings:Data:60` miliknya sendiri; lihat `docs/WORKER.md`.

## 3. Urutan hari-H

Langkah teknis tukar nama DB dan rollback ada di `docs/PARALEL.md` bagian "Cutover". Urutan lengkap:

1. **H-7 s/d H-1** — jalankan `Panin.LoginSync` terjadwal; pantau log aplikasi baru (cari `Level=Error`), pastikan pengguna uji paralel tidak punya keluhan terbuka.
2. **H-1** — beri tahu pengguna jendela perawatan; backup semua DB (LOGIN lama, LOGIN baru, DATA); pastikan `PANINSVC` lama dan `Panin.Worker` **tidak** berjalan bersamaan.
3. **Jendela perawatan** — hentikan akses ke aplikasi lama; `Panin.LoginSync --apply` terakhir; hentikan kedua aplikasi; tukar nama DB LOGIN sesuai `docs/PARALEL.md`; pastikan `RFMODULE.DB_NAMA` modul 99 dan `ConnectionStrings:Login` menunjuk nama akhir.
4. Set `ParallelRun:Enabled=false`; hentikan layanan **PANINSVC** lama; ubah `Upload:Mode` worker menjadi `Run` (setelah `Observe` sehari penuh tanpa temuan).
5. Hidupkan aplikasi baru; **uji asap** (bagian 4).
6. Umumkan selesai; matikan (jangan hapus) aplikasi lama; simpan DB lama (`_LAMA`) untuk rujukan dan rollback.
7. **Rollback** bila uji asap gagal atau galat berat dalam jam pertama: kembalikan nama DB, hidupkan aplikasi lama dan PANINSVC lama, matikan `Panin.Worker` (`Upload:Mode=Off`).

## 4. Uji asap setelah cutover

| Modul | Uji |
|---|---|
| Login | Login user biasa, user terkunci, user yang wajib ganti password; satu sesi per user |
| 60 Collection | Cari debitur → detail → Call dan Visit → Letter; Monitoring Debitur dan Recovery (tim, tab Pending/Touched); Distribusi (pindah debitur); Bulk Surat (hasil unduhan); Exception Letter; Janji Debitur; News |
| Report | Satu report tiap jenis: daftar (Payment), pivot dinamis (Time Spent), pivot kustom (Daily Tracking), berpita (Collector Productivity); ekspor Excel |
| 99 Maintenance | Buat dan setujui satu user uji, satu ajuan parameter; hapus datanya |
| 70 MIS | Buka satu report dari DB MIS |
| Worker | `UPLOADRESULT` hari itu: ada `SUCCESS` dari `DataUploader`; berkas pindah ke Done |
| Teknis | `/healthz` hijau; tidak ada `Error` baru di log dalam 15 menit; memori dan koneksi DB stabil |

## 5. Hari-hari sesudahnya

- Pantau 2 minggu: log galat (kode `Ref` pengguna → `docs/LOGGING.md`), query lambat (`Diagnostics:SlowQueryMs`), keluhan pengguna per kanal yang disepakati.
- Hari kerja pertama dan akhir bulan: periksa unggah harian dan report yang berat (Leading Edge, Was Is).
- Setelah dua minggu stabil: arsipkan aplikasi lama, cabut akses jaringannya, rotasi credential yang pernah ada di `Web.config` lama.

## 6. Siapa menyediakan apa

| Kebutuhan | Pihak |
|---|---|
| String koneksi, akun layanan, hak folder UNC, jadwal Task Scheduler | Operasi/infrastruktur |
| Perubahan SP/indeks (Leading Edge, Was Is) dan backup/rename DB | DBA |
| Contoh keluaran lama, konfirmasi fitur dipakai, UAT | Pemilik proses/pengguna |
| Keputusan editor berita, desain tampilan, jadwal | Pemilik produk |
| Rotasi credential dan tinjauan keamanan | Keamanan informasi |
