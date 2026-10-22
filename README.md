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
docs/
  MIGRATION-PLAN.md     rencana migrasi, fase, risiko
  DMS-LEGACY-CONTRACT.md  kontrak DLL lama DMS.* (hasil inspeksi)
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
| `ConnectionStrings:Data:60` / `:70` / `:99` | DB DATA per modul. Kosong = pakai kolom `DB_*` di `RFMODULE` seperti sistem lama |

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
