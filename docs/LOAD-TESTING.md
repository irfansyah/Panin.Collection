# Uji beban

Syarat: aplikasi harus melayani **> 700 user bersamaan** (lihat `MIGRATION-PLAN.md` bagian 2F).

## Harness awal (`tools/Panin.LoadTest`)

Dua jenis uji: (1) tingkat HTTP (`run`): login massal, halaman shell, logout; (2) sirkuit Blazor sungguhan (`circuits`), lihat bagian "Uji sirkuit Blazor".

```bash
# 1. Buat 700 user uji (LT0001..LT0700) di DB LOGIN lokal (Docker). Menolak server non-lokal.
dotnet user-secrets set "LoadTest:Password" "<password uji>" --project tools/Panin.LoadTest
dotnet run --project tools/Panin.LoadTest -c Release -- seed --count 700

# 2. Jalankan aplikasi (Diagnostics:Enabled=true di user-secrets supaya /_diag aktif)
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Panin.Web -c Release --no-launch-profile --urls http://localhost:5122

# 3. Beban
dotnet run --project tools/Panin.LoadTest -c Release -- run --url http://localhost:5122 --users 700 --concurrency 150 --hold 15
```

Laporan: login berhasil/gagal, latensi p50/p95/p99 per langkah, dan memori server dari `/_diag` (sirkuit, sesi, memori terkelola, working set).
User uji dipakai bergantian di tiga group (3, 2, dan 1 modul). Aturan satu sesi per user tetap berlaku, jadi tiap user virtual memakai akun sendiri.

## Hasil pertama (laptop dev, SQL Server 2022 di Docker, DB LOGIN salinan dev)

| Skenario | Tanpa indeks `SCALLUSERLOG` | Dengan indeks |
|---|---|---|
| 700 login, konkurensi 150 | 700/700 berhasil, 372 login/detik, `POST /login` p95 ±730 ms | 700/700, 683 login/detik, `POST /login` p95 ±245 ms |
| Logout untuk pengguna | 11 ms (dicatat di latar) | 2 ms |
| Pencatatan logout 700 user ke DB selesai dalam | ±60 detik | ±6 detik |
| Logout yang tidak tercatat | 1 dari 700 (deadlock) | 0 |

Catatan: hasil ini mengukur sisi HTTP + DB. Satu instance aplikasi memakai ±280 MB working set setelah 700 sesi HTTP (tanpa sirkuit). Ini baru baseline, bukan angka kapasitas.

## Temuan

1. **`SP_LOGUSER` memindai `SCALLUSERLOG` (±1 juta baris) pada setiap logout.** Tabel itu hanya punya indeks PK pada `_ID`, sedangkan SP mencari `USERID` + `IP_ADDR` terbaru. Satu logout ±140 ms CPU; logout serentak saling menunggu dan **deadlock (error 1205)**. Ini juga bagian dari alasan sistem lama tidak kuat di atas ±300–400 user.
   - Mitigasi di aplikasi (sudah ada): pencatatan logout lewat antrean latar dengan 4 worker (`LogoutWriter`), sesi dilepas seketika, dan operasi tulis login/logout diulang bila menjadi korban deadlock (`DeadlockRetry`).
   - Perbaikan sebenarnya adalah indeks di DB (perubahan DB lama, **butuh persetujuan**): `docker/sql/recommended-indexes.sql`. Sudah diterapkan di salinan lokal; di DB lama belum.
2. `SU_ALLUSERACTIVITY` saat login mengubah `SCALLUSERFLAG`; di bawah beban 150 konkurensi tidak terlihat masalah.
3. Lonjakan login 700 user selesai dalam ±2–5 detik di mesin ini; bottleneck bergeser ke DB, bukan aplikasi.

## Uji sirkuit Blazor (WebSocket)

`circuits` membuka sirkuit sungguhan tanpa browser: login lewat HTTP, lalu klien protokol Blazor (`CircuitClient`) terhubung ke hub `/_blazor` dengan protokol `blazorpack`, memulai sirkuit dari penanda prarender, mengakui tiap batch render, dan menjawab panggilan JS interop server (`import`, `idleSeconds`, dst.) seperti browser yang idle. Timer idle/detak sesi di server (`SessionKeeper`) ikut berjalan, jadi `SU_MONITORACTIVITY` benar-benar terpanggil tiap menit per sirkuit.

```bash
# restart server tiap uji supaya registry sesi bersih (aturan satu sesi per user berlaku)
dotnet run --project tools/Panin.LoadTest -c Release -- circuits --url http://localhost:5122 --users 700 --concurrency 50 --hold 75
```

Memori dibaca dari `/_diag?gc=true` (memaksa GC penuh, hanya aktif bila `Diagnostics:Enabled=true`) sehingga angka tidak berisi sampah.

### Hasil (laptop dev, satu instance, shell kosong tanpa data modul)

| Sirkuit aktif | Terbuka | Terputus dalam 60–75 s | Memori terkelola (setelah GC) | Working set proses | Per sirkuit (terkelola) |
|---|---|---|---|---|---|
| 0 (baseline) | – | – | 2 MB | ±73 MB | – |
| 100 | 100/100 dalam 1,1 s | 0 | 24 MB | ±168 MB | ±220 KB |
| 700 | 700/700 dalam 2,7 s | 0 | 116 MB | ±345 MB | ±163 KB |
| 1.000 | 1000/1000 dalam 3,5 s | 0 | 165 MB | ±500 MB | ±160 KB |

Selama uji, semua sirkuit menerima batch render awal (±5 per sirkuit) dan memanggil JS interop setiap 15 detik; DB menerima 700–1.000 detak per menit tanpa kegagalan.

**Cara membacanya:** angka ini adalah *lantai*, bukan kapasitas. Yang diukur hanya shell (login, menu, footer, timer sesi). Halaman modul sungguhan (grid, filter, hasil query) akan menambah state per sirkuit; anggaran awal di rencana adalah ≤ 5 MB rata-rata per sirkuit, jadi masih ada ruang ±30× dari lantai ini. Anggaran harus dicek ulang setiap modul masuk (lihat "Berikutnya").

## Belum tercakup (pekerjaan berikutnya)

- Klien sirkuit belum mengirim event (klik menu, input). Untuk itu batch render harus di-parse (format biner Blazor) atau memakai browser sungguhan (Playwright, sudah disetujui; browser Chromium sudah ada di mesin ini). Dipakai saat halaman modul pertama selesai.
- Lonjakan reconnect, restart server, dan pemulihan sesi.
- Pola kerja nyata (worklist, pencarian debitur) setelah modul Collection tersedia; harus melawan DB DATA yang realistis.
- Server target (CPU/RAM) belum diketahui; anggaran memori per sirkuit dari 2F perlu dibandingkan dengan RAM nyata.
