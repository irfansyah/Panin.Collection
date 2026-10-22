# Panin.Worker (pengganti PANINSVC)

Layanan latar yang mengerjakan **unggah data harian**: menunggu berkas teks dari sistem inti, menjalankan `SP_UPLOAD_CUSTACC` pada DB DATA modul 60,
mencatat hasilnya, lalu memindahkan berkas. Berjalan sebagai Windows Service (`AddWindowsService`) atau proses konsol/Linux.

## Alur (sama dengan PANINSVC, dengan perbaikan)

1. Di dalam jendela `WindowStart`–`WindowFinish`, tiap `IntervalMinutes` worker memeriksa folder `Upload:Path`.
2. Berkas harian: `<awalan><ddMM>.TXT`, mis. `CUS2210.TXT`. Awalan wajib: `AOC, CAB, PRD, REL, CUS, ACC, HIS, COL, DUE`; `MIS` opsional (ikut dipindah bila ada).
3. Bila **semua** berkas wajib ada → jalankan `PostStatements` (bawaan `exec SP_UPLOAD_CUSTACC @1`; `@1` = path folder dengan pemisah di belakang), dalam satu koneksi.
4. Sukses → `USPV_UPLOADSUCCESS`, berkas ke `DonePath` sebagai `AOC2210-20261022093005.TXT`. Gagal → `USPV_UPLOADFAIL` (+ pesan), berkas ke `FailPath`.
5. Bila berkas belum lengkap sampai kesempatan terakhir di jendela → satu baris `FAIL` "All or some required files(...) does not exists".

Perbedaan dari PANINSVC lama:

| Lama | Baru |
|---|---|
| Status "sudah selesai" di memori; restart ulang = unggah lagi dan baris FAIL palsu | Cukup lihat `UPLOADRESULT` (ada `SUCCESS` hari ini → tidak mengerjakan apa pun). Aman di-restart. |
| Berkas belum lengkap → semua dipindah ke Fail dan dicatat gagal tiap pemeriksaan | Tunggu; berkas tidak disentuh; gagal dicatat sekali di kesempatan terakhir (~850 baris "All or some…" di data lama) |
| Gagal → diulang tiap selang dengan berkas sudah dipindah | Gagal sekali → berhenti hari itu (perbaiki, kembalikan berkas, restart worker) |
| Menunggu `MIS` walau SP tidak membacanya | `MIS` opsional |
| Tidak ada kunci; dua instance bisa berjalan bersamaan | `sp_getapplock` `PANIN_UPLOAD` pada koneksi eksekusi |
| Log teks ke berkas lokal | Serilog (konsol + JSON `logs/worker-.log`, Seq/OTLP bisa ditambah), `TraceId`/`Ref` per putaran |

## Mode (`Upload:Mode`)

| Mode | Perilaku |
|---|---|
| `Off` (bawaan) | tidak melakukan apa pun |
| `Observe` | memeriksa folder dan melaporkan "berkas lengkap", **tanpa** menjalankan SP atau memindahkan berkas. Aman dipakai selama uji paralel. |
| `Run` | mengerjakan unggah. **Matikan PANINSVC lama lebih dulu.** |

## Konfigurasi

`appsettings.json` (atau variabel lingkungan `Upload__...`, user-secrets untuk pengembangan). Salin nilai produksi dari `App.config` PANINSVC di server:

| Kunci | Keterangan |
|---|---|
| `ConnectionStrings:Data:60` | DB DATA modul 60 (sama dengan aplikasi web). Akun butuh hak `EXEC` SP unggah. |
| `Upload:Path` / `DonePath` / `FailPath` | Folder. **`Path` harus dapat dibaca SQL Server dengan teks yang sama** (SP membuka berkas lewat path ini) → biasanya UNC `\server\share\`. |
| `WindowStart` / `WindowFinish` / `IntervalMinutes` / `PollSeconds` | Jendela harian (HH:mm), selang pemeriksaan, selang bangun worker. Data lama menunjukkan sukses sekitar 05:00 dan "tidak lengkap" tercatat sekitar 17:00. **Nilai produksi harus dicocokkan.** |
| `RequiredFiles` / `OptionalFiles` | Awalan berkas. Daftar bawaan ada di `appsettings.json` (pengikat konfigurasi .NET menambah, bukan menimpa, array bawaan di kode). |
| `PreStatements` / `PostStatements` | Perintah SQL; `@1` = path. |
| `CommandTimeoutSeconds` | Bawaan 7200 (lama: 100000 detik). |

Konfigurasi tidak valid pada mode selain `Off` membuat worker menolak start (pesan jelas di log) daripada berjalan setengah jalan.

## Yang perlu diketahui operasional

- `SP_UPLOAD_CUSTACC` memanggil `LOSPANINLOGIN_COLLECTION.DBO.SP_RECREATE_DOWLINE` (nama DB LOGIN tertanam). Pada salinan lokal yang tidak punya DB itu SP gagal (diuji: tercatat `FAIL`, berkas ke Fail). Di paralel/cutover ikuti `docs/PARALEL.md`.
- Pesan galat SQL dipotong sebelum "Last Query" dan dibatasi 6000 karakter (kolom `UPLOADRESULT.UPLOADRESULT`).
- Satu instance saja. Dua worker tidak merusak data (kunci), tetapi yang kedua hanya menunggu.

## Pengujian

- Unit (`UploadCycleTests`, 14 kasus): mode Off/Observe, jendela, selang, berkas tak lengkap, sukses, gagal, sudah sukses hari ini, kunci terpakai, pemisah path, validasi.
- Lokal (Docker): Observe tidak memindah; Run + perintah uji `SELECT 1` → 10 berkas ke Done, baris `SUCCESS` tercatat; restart → "Unggah hari ini sudah sukses"; SP asli pada salinan tanpa DB LOGIN → `FAIL` + berkas ke Fail. Baris uji `UPLOADRESULT` dihapus setelahnya.
- **Belum**: eksekusi `SP_UPLOAD_CUSTACC` sungguhan dengan berkas nyata (bergantung data dan DB LOGIN produksi/staging).
