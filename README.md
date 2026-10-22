# Panin.Collection

Penulisan ulang aplikasi Panin Collection (LOS Collection) dari ASP.NET WebForms menjadi **.NET 10 + Blazor Server**.
Database SQL Server tetap sama (view, stored procedure, dan skema tidak diubah).

Repo lama (hanya referensi, tidak diubah): `dms-digital-new/Panin-Collection`, branch `multibrowser`.

## Struktur

```
src/
  Panin.Domain          entity & DTO
  Panin.Application     use case, abstraksi (IDb, ...)
  Panin.Infrastructure  akses data (SqlDb), integrasi
  Panin.Web             Blazor Server
  Panin.Worker          pengganti Windows Service PANINSVC
tests/
  Panin.UnitTests
tools/
  Panin.DbClone         salin DB sumber ke Docker lokal
  Panin.LoginSync       sinkronisasi satu arah DB LOGIN lama ke baru (masa paralel)
  Panin.LoadTest        uji beban (HTTP, sirkuit, browser Playwright)
  Panin.ReportCheck     jalankan semua report, ukur waktu, periksa konsistensi (hanya baca)
  Coverage              peta fitur lama -> baru (make_coverage.py)
  ReportExtract         ekstraksi tata letak pivot dari kelas report lama
docs/
  MIGRATION-PLAN.md     rencana migrasi, fase, risiko
  DMS-LEGACY-CONTRACT.md  kontrak DLL lama DMS.* (hasil inspeksi)
  PARALEL.md            masa uji paralel dengan aplikasi lama dan langkah cutover
  LOGGING.md            logging terlacak ujung ke ujung, cara menelusuri kode galat
  LOAD-TESTING.md       uji beban (HTTP, sirkuit, browser sungguhan)
  QUERY-REGISTRY.md     ukuran dan keputusan per query/SP
  PERFORMANCE.md        temuan performa dan aturan
  REPORTS.md            report: inventaris, mesin daftar/pivot, alat pemeriksa
  WORKER.md             unggah harian (pengganti PANINSVC)
  COVERAGE.md           peta fitur aplikasi lama -> aplikasi baru (dibuat otomatis)
  BACKLOG.md            pekerjaan tertunda dan yang dibutuhkan agar bisa dilanjutkan
  CUTOVER.md            serah terima dan daftar periksa cutover
```

## Menjalankan

```bash
dotnet build
dotnet test
dotnet run --project src/Panin.Web
```

Perlu .NET SDK 10 (lihat `global.json`) dan Node.js 20+ (build Tailwind CSS otomatis saat `dotnet build`; lewati dengan `-p:SkipTailwind=true`).

## Konvensi

- UI: Blazor Server + **Tailwind CSS** (tanpa Bootstrap). Sumber `src/Panin.Web/Styles/tailwind.css`, hasil `wwwroot/css/site.css` (tidak di-commit). Saat pengembangan: `npm run watch:css` di `src/Panin.Web`.
- Data: **Dapper** lewat `IDb` (parameter posisional `@1..@n`, selalu parameter SQL sungguhan). Tanpa EF Core.

## Konfigurasi & rahasia

Jangan commit connection string atau password. Gunakan User Secrets untuk lokal:

```bash
dotnet user-secrets init --project src/Panin.Web
dotnet user-secrets set "ConnectionStrings:Login" "<connection string>" --project src/Panin.Web
```

Kunci yang dipakai (nilai asli diisi per mesin, jangan di-commit):

| Kunci | Isi |
|---|---|
| `ConnectionStrings:Login` | DB LOGIN (`LOSPANINLOGIN`) |
| `ConnectionStrings:Data:60` / `:70` / `:99` | DB DATA per modul. Kosong = pakai kolom `DB_*` di `RFMODULE` seperti sistem lama. Bila berisi `ApplicationIntent=ReadOnly`, DB dibuka **baca-saja** (hanya `SELECT` dan SP yang ada di `DataAccess:ReadOnlyProcedures`; operasi tulis ditolak aplikasi) |
| `DataAccess:ReadOnly:<id>` | `true`/`false` untuk memaksa mode baca-saja per modul |

Untuk server gunakan environment variable (`ConnectionStrings__Login`) atau secret store.

## Database lokal (Docker)

Salinan DB LOGIN yang boleh diubah (mis. `RFMENUX.MENUURL`) untuk pengembangan. DB produksi/lama **tidak diubah**.

```bash
docker compose -f docker/docker-compose.yml up -d
```

- SQL Server 2022 di `localhost,14333`, database `LOSPANINLOGIN`. Password SA ada di `docker/.env` (tidak di-commit); buat file itu dengan isi `MSSQL_SA_PASSWORD=<password kuat>` sebelum menjalankan.
- User-secrets: `ConnectionStrings:Login` menunjuk ke container ini; `ConnectionStrings:LoginSource` menunjuk ke DB LOGIN lama (hanya dibaca untuk menyalin data).

Menyalin ulang data dari DB lama ke container (sumber hanya dibaca; target didrop dan dibuat ulang):

```bash
dotnet run --project tools/Panin.DbClone
```

### Salinan lokal DB DATA (skema lengkap + data contoh)

DB DATA asli (`LOSPANINCOLLECTION`) ±64 GB, jadi tidak disalin penuh. Alat yang sama membuat salinan yang boleh ditulis untuk pengembangan halaman yang menyimpan data:

```bash
dotnet run --project tools/Panin.DbClone -c Release -- data
# atau pilih user contoh sendiri:
dotnet run --project tools/Panin.DbClone -c Release -- data --users USER1,USER2,USER3
```

- Skema lengkap (409 tabel, 233 view, 432 SP/fungsi/trigger). Data: tabel kecil (≤ 100 ribu baris) penuh; tabel besar yang berindeks ACC_NO/CU_REF hanya berisi rekening/nasabah milik 12 user contoh; tabel besar lain hanya 2.000 baris pertama; tabel sementara/unduhan hanya skema. Hasil ±680 ribu baris, ±460 MB.
- Sumber hanya dibaca (`ReadOnly`, `READ UNCOMMITTED`), target wajib server lokal.
- View DATA yang membaca DB LOGIN (`SCUSER`, `RFGROUP`, `TEMP_DOWNLINE`) diarahkan ke `LOSPANINLOGIN` lokal, sehingga user dan hak akses sama dengan salinan DB LOGIN.
- 3 objek tidak dapat dibuat karena rusak di sumber atau butuh linked server: `FNC_VW_REKOMENDASI` (merujuk `VW_REKOMENDASI` yang tidak ada), `VW_REPORT_FLOW_RATE` (kolom `PRODUCT_TYPE` tidak ada), `SP_MOVE_HIS_TO_MIS` (linked server `SVRMIS`).
- Untuk menjalankan aplikasi terhadap salinan ini (boleh menulis), arahkan `ConnectionStrings:Data:60` ke nilai `ConnectionStrings:DataLocal:60` di user-secrets. Untuk kembali membaca data asli (baca-saja), kembalikan ke koneksi server dev.
