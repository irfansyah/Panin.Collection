# Masa uji paralel dan cutover

Rencana: aplikasi lama dan baru berjalan **bersamaan** untuk uji, lalu aplikasi lama dimatikan (cutover). Selama paralel:

- **DB DATA dipakai bersama** (satu `LOSPANINCOLLECTION`).
- **DB LOGIN dipisah**: aplikasi lama memakai DB LOGIN lama; aplikasi baru memakai DB LOGIN baru (`MENUURL` di `RFMENUX` berbeda).

## Temuan yang menentukan desain ini

Di DB DATA, `SCUSER`, `RFGROUP`, dan `TEMP_DOWNLINE` adalah **view yang menunjuk ke DB LOGIN dengan nama tertanam** (`LOSPANINLOGIN_COLLECTION` di produksi; `SP_UPLOAD_CUSTACC` juga memanggil `SP_RECREATE_DOWLINE` di DB itu). Sebaliknya DB LOGIN memiliki 14 objek yang menunjuk ke DATA (`RFBRANCH`, `RFPRODUCT`, `RFREGIONAL`, `VW_RFBRANCH`, dst.) dan `SP_AUDITTRAIL` yang mengubah `LOAN_MASTER` saat user dihapus.

Akibatnya, di sisi data **hanya satu DB LOGIN yang dianggap benar: yang lama.** Kewenangan atas rekening (pemilik/atasan), hierarki bawahan, dan nama collector di Call/Visit/Distribusi/Monitoring semuanya dibaca dari sana, termasuk saat dipakai aplikasi baru.

## Apa yang berbeda selama paralel

| Hal | Perilaku selama paralel |
|---|---|
| Login, menu, `MENUURL` | Aplikasi baru memakai DB LOGIN barunya. |
| User, group, akses menu group | **Dikelola hanya di aplikasi lama.** Di aplikasi baru halaman User/Group Maker-Approval hanya untuk dilihat (`ParallelRun:Enabled`). |
| Password | **Diubah hanya di aplikasi lama.** Ganti password di aplikasi baru dinonaktifkan; user yang wajib ganti password diarahkan ke aplikasi lama. |
| Isi DB LOGIN baru | Salinan satu arah dari DB LOGIN lama lewat `tools/Panin.LoginSync`. |
| Aturan satu sesi per user | Berlaku **per aplikasi** (flag `SU_LOGON` ada di DB LOGIN masing-masing); user bisa login di keduanya sekaligus. |
| Buka kunci login / cabut revoke | Dikerjakan di aplikasi tempat user terkunci (flag tidak disinkronkan). |
| Call, Visit, Letter, pindah debitur, parameter General/Process/Group Treatment | **Aman dua arah.** Semua lewat SP/tabel DATA yang sama; hasilnya sama di kedua aplikasi. |
| Cache aplikasi baru (menu 5 menit, daftar tim 60 detik, referensi 10 menit) | Perubahan dari aplikasi lama baru terlihat setelah cache habis. |

## Mengaktifkan mode paralel

Di konfigurasi aplikasi baru (`appsettings.Production.json`, user-secrets, atau variabel lingkungan `ParallelRun__Enabled=true`):

```json
"ParallelRun": { "Enabled": true, "OldAppUrl": "http://192.168.88.11:15002/login.aspx" }
```

Efeknya (diperiksa di server, bukan hanya disembunyikan): tambah/ubah/hapus user dan group, simpan akses menu, setujui ajuan user/group, reset password, dan ganti password ditolak dengan pesan yang menunjuk ke aplikasi lama. Ajuan Group Treatment dan parameter lain tetap bisa.

## Sinkronisasi DB LOGIN (lama → baru)

`tools/Panin.LoginSync`:

| Disalin | Tidak disalin |
|---|---|
| `SCALLGROUP`, `SCALLUSER` (termasuk password), `GRPMENUX`, `TEMP_DOWNLINE`; baris `SCALLUSERFLAG` yang belum ada dibuat | `RFMENUX` (`MENUURL` baru), `RFMODULE`, flag login/revoke yang sudah ada, `SCALLUSERLOG`, `AUDITTRAIL*`, `ESAPPTOKEN`, `LOGINPARAM` |

```bash
# connection string lewat variabel lingkungan agar tidak tercatat di riwayat shell
export PANIN_SYNC_SOURCE="Server=<lama>;Database=<login lama>;User Id=...;Password=...;TrustServerCertificate=True"
export PANIN_SYNC_TARGET="Server=<baru>;Database=<login baru>;User Id=...;Password=...;TrustServerCertificate=True"

dotnet run --project tools/Panin.LoginSync                 # dry-run: hitung perubahan, lalu batalkan
dotnet run --project tools/Panin.LoginSync -- --apply      # simpan
```

Jaminan alat ini (teruji di salinan lokal):

- Sumber **hanya dibaca** (`SELECT`, `ApplicationIntent=ReadOnly`); tidak ada perintah tulis ke DB lama.
- Tujuan diubah dalam **satu transaksi**: gagal di tengah = tidak ada perubahan. Default dry-run.
- Pengaman penghapusan: bila satu tabel akan kehilangan lebih dari 5% (minimal 10) barisnya, seluruhnya dibatalkan dengan kode keluar 3 (biasanya tanda salah sumber). `--allow-mass-delete` untuk menimpa.
- Idempoten: dijalankan ulang tanpa perubahan di sumber = 0 perubahan.
- Sumber dan tujuan yang sama persis ditolak.
- `--skip-passwords`: tidak menimpa password user yang sudah ada (bila ingin password aplikasi baru berdiri sendiri).
- Trigger `INS/UPD/DEL_USER` dimatikan selama sinkronisasi dalam transaksi yang sama (mereka mengasumsikan satu baris per perintah) dan `TEMP_DOWNLINE` disalin dari sumber; dinyalakan kembali sebelum commit.

Jadwalkan (Task Scheduler) tiap beberapa menit dengan `--apply`, atau jalankan manual sebelum sesi uji. Perubahan user di aplikasi lama baru masuk ke aplikasi baru setelah sinkronisasi berikutnya.

## Cutover (aplikasi lama dimatikan)

Prasyarat: semua fitur yang dipakai operasional sudah ada di aplikasi baru. **Per hari ini belum:** sebagian kecil report (laporan berpita Collector Productivity/Alasan NPL, 4 report recovery/KPM berkondisi kustom, Letter Status; status di `docs/REPORTS.md`) dan perbandingan keluaran report pivot dengan aplikasi lama pada data nyata, serta tautan lanjutan Monitoring Recovery/NPL (Janji Debitur, Upload Data AYDA, Upload Visit, View News, Manual Update). Pekerjaan latar PANINSVC sudah digantikan `Panin.Worker` (lihat `docs/WORKER.md`; aktifkan hanya saat cutover). Itu harus selesai, atau dipastikan tidak dipakai, sebelum langkah di bawah.

Cara paling sederhana adalah **menukar nama DB**, karena nama DB LOGIN tertanam di view DATA:

1. Jadwalkan jendela perawatan; hentikan akses ke aplikasi lama.
2. Jalankan `Panin.LoginSync --apply` satu kali terakhir. Hentikan layanan **PANINSVC** lama dan ubah `Upload:Mode` worker baru menjadi `Run` (jangan keduanya aktif).
3. Hentikan aplikasi lama dan aplikasi baru.
4. Ganti nama DB (putuskan koneksi dulu: `ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE`):
   - DB LOGIN lama → `<nama lama>_LAMA` (disimpan untuk rujukan/rollback).
   - DB LOGIN baru → `<nama lama>` (nama yang dirujuk view DATA).
5. Di DB LOGIN baru: pastikan `RFMODULE.DB_NAMA` modul 99 berisi nama akhir itu, dan `ConnectionStrings:Login` aplikasi baru menunjuk ke nama akhir.
6. Set `ParallelRun:Enabled=false` (Maintenance User/Group dan ganti password aktif kembali); hentikan jadwal sinkronisasi.
7. Verifikasi: login, menu, Call/Visit pada satu rekening, kewenangan atasan, Maintenance User (buat dan setujui satu user uji).
8. **Rollback** bila perlu: kembalikan nama DB seperti semula dan nyalakan aplikasi lama (DB lama tidak pernah diubah oleh alat sinkronisasi).

Alternatif tanpa ganti nama: ubah 3 view DATA (`SCUSER`, `RFGROUP`, `TEMP_DOWNLINE`) dan `SP_UPLOAD_CUSTACC` agar menunjuk ke nama DB LOGIN baru.

Yang tidak ikut pindah otomatis: riwayat `SCALLUSERLOG` dan `AUDITTRAIL*` di DB LOGIN lama. Simpan DB lama (`_LAMA`) untuk rujukan, atau salin sekali bila perlu laporan audit lintas masa.

## Worker unggah harian selama uji paralel

PANINSVC lama dan `Panin.Worker` **tidak boleh sama-sama menjalankan unggah**: keduanya memanggil `SP_UPLOAD_CUSTACC` pada DB DATA yang sama. Selama paralel biarkan `Upload:Mode` = `Off` (bawaan) atau `Observe` (hanya melaporkan berkas yang terlihat; tidak menjalankan SP dan tidak memindahkan berkas). Detail: `docs/WORKER.md`.
