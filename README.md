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



## Konfigurasi & rahasia

Jangan commit connection string atau password. Gunakan User Secrets untuk lokal:

```bash
dotnet user-secrets init --project src/Panin.Web
dotnet user-secrets set "ConnectionStrings:Login" "<connection string>" --project src/Panin.Web
```

Untuk server gunakan environment variable atau secret store.
