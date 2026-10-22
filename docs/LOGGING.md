# Logging dan penelusuran galat

Tujuan: setiap galat **tercatat dan bisa ditelusuri dari layar sampai query DB**, tanpa bergantung pada pengguna mengingat apa yang ia lakukan.

## Cara kerja

Satu kejadian = satu **TraceId** (W3C `Activity`). TraceId dibuat otomatis untuk:

| Asal | Dibuat oleh |
|---|---|
| Request HTTP (login, halaman awal, logout, API) | ASP.NET Core; juga dikirim balik sebagai header `X-Trace-Id` dan `<meta name="trace-id">` |
| Setiap interaksi di sirkuit Blazor (klik, input, panggilan JS) | `CircuitCorrelationHandler` (satu Activity per aktivitas masuk) |
| Timer di dalam sirkuit (detak sesi) | `SessionKeeper` |
| Pekerjaan latar (pencatatan logout, pembersih sesi) | `LogoutWriter`, `SessionReaper` |

Setiap baris log otomatis memuat (lihat `CorrelationEnricher`):

| Properti | Isi |
|---|---|
| `TraceId` / `SpanId` | id alur; sama untuk semua log dari satu interaksi, termasuk log query DB |
| `Ref` | 8 karakter pertama TraceId (huruf besar) = **kode yang dilihat pengguna** |
| `CircuitId` | sirkuit Blazor (satu tab browser) |
| `UserId`, `GroupId`, `SessionId` | pengguna yang sedang bertindak |
| `SourceContext`, `Application`, `Machine` | asal log |

## Yang dicatat (dan tidak boleh hilang)

- **Galat DB**: `QueryStats` mencatat setiap query yang gagal (`Error`) berikut exception, nama DB, nomor galat SQL, durasi, dan teks SQL (terpotong 500 karakter). **Nilai parameter tidak pernah dicatat.** Query lambat dicatat `Warning` (ambang `Diagnostics:SlowQueryMs`).
- **Galat halaman**: `PaninErrorBoundary` membungkus setiap halaman modul. Exception di render/event mana pun dicatat `Error` dan pengguna melihat kotak "Halaman ini mengalami kesalahan" berisi kode + tombol *Coba lagi* (tab dan halaman lain tidak ikut mati).
- **Pesan galat ke pengguna** (semua yang berbunyi "…hubungi administrator") berakhiran `(Kode: XXXXXXXX)`.
- **Galat tak tertangani**: di request (`UseExceptionHandler` → halaman `/Error` dengan kode), di sirkuit (log bawaan Blazor), di luar request (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`), dan saat start (logger awal + `Log.Fatal`).
- **Pekerjaan latar**: kegagalan satu item tidak lagi mematikan worker atau host; dicatat dan lanjut.
- **Browser**: `clientlog.js` mengirim `error`, `unhandledrejection`, dan kegagalan sambung sirkuit (`failed/rejected/paused`) ke `POST /api/client-log` (maks. 10 per halaman, 20 per menit per IP, badan maks. 16 KB). Laporan memuat `pageTrace` = TraceId halaman yang sedang dibuka.
- **Request HTTP**: satu baris per request (`status`, durasi); 4xx `Warning`, 5xx/exception `Error`; aset statis dan `/healthz` tidak dicatat.
- **Siklus sirkuit**: `Sirkuit dibuka` / `Sirkuit ditutup (n detik)` dengan user, untuk menelusuri keluhan "layar mati sendiri".
- **Tindakan bisnis** (call/visit/letter/pindah debitur/maintenance) sudah mencatat `Information` dengan user dan objek; kegagalannya `Error`.

## Menelusuri sebuah keluhan

1. Minta **kode** dari pengguna (mis. `3CE22383`).
2. Cari di berkas log: semua baris alur itu memiliki `"Ref":"3CE22383"`.

```bash
# Windows/Git Bash; log JSON satu baris per kejadian
grep '"Ref":"3CE22383"' logs/panin-*.log
```

Hasil nyata dari uji (pencarian debitur saat DB DATA tidak terjangkau):

```
[Error] Panin.Infrastructure.Data.QueryStats  user=LT0003 circuit=KA6yRn… trace=3ce22383…  Query gagal di "data:60" setelah 3031 ms (SQL error 258): SELECT TOP (@4) D.ACC_NO …  [+stack]
[Error] Panin.Web.Modules.Collection.Inquiry.DebiturSearch  user=LT0003 circuit=KA6yRn… trace=3ce22383…  Pencarian debitur gagal  [+stack]
```

Tanpa kode: cari `UserId` + rentang waktu, atau `CircuitId` untuk melihat seluruh riwayat satu tab.
Untuk galat di browser: cari `"SourceContext":"ClientLog"`; `pageTrace` cocok dengan `TraceId` request halaman itu.

## Konfigurasi

Bagian `Serilog` di `appsettings.json` (dapat ditimpa per lingkungan):

| Pengaturan | Default | Catatan |
|---|---|---|
| `WriteTo: File.path` | `logs/panin-.log` (relatif terhadap direktori kerja) | **Di produksi isi path absolut di luar folder aplikasi** agar tidak terhapus saat deploy dan bisa dibaca tim operasi. |
| `rollingInterval` | `Day` | satu berkas per hari |
| `retainedFileCountLimit` | `60` | ≈ 2 bulan; sesuaikan dengan kebijakan retensi |
| `fileSizeLimitBytes` | `100 MB` | berpindah berkas bila terlampaui (`rollOnFileSizeLimit`) |
| Format | JSON ringkas (`RenderedCompactJsonFormatter`) | satu objek per baris; dapat langsung dibaca Seq/ELK/Loki bila nanti ada |
| `MinimumLevel` | `Information`; `Microsoft`/`System` `Warning` | naikkan sementara ke `Debug` lewat konfigurasi untuk investigasi |

Berkas log **tidak masuk Git** (`logs/`).

## Mengirim log ke tempat lain (Seq, Aspire Dashboard, dll.)

Berkas JSON tetap menjadi catatan utama dan tidak bergantung pada layanan lain. Tujuan tambahan dipasang **lewat konfigurasi saja**
(tanpa mengubah kode) dengan menambah entri `Serilog:WriteTo`. Paket **Seq** dan **OpenTelemetry (OTLP)** sudah terpasang; sink Serilog lain
(Elasticsearch, Loki, MSSQL, Splunk, dst.) ditambahkan dengan satu paket NuGet + satu entri.

| Tujuan | Cara | Catatan |
|---|---|---|
| **Seq** | `{ "Name": "Seq", "Args": { "serverUrl": "http://seq:5341", "apiKey": "<kunci>" } }` | Cari dengan `Ref = 'XXXXXXXX'` atau `UserId`/`CircuitId`. Di server beri password admin dan pakai API key; `SEQ_FIRSTRUN_NOAUTHENTICATION` hanya untuk lokal. |
| **Aspire Dashboard** (atau kolektor OTLP lain: Grafana/Loki, Jaeger, Elastic APM) | `{ "Name": "OpenTelemetry", "Args": { "endpoint": "http://localhost:4317", "protocol": "Grpc", "resourceAttributes": { "service.name": "panin-collection" } } }` | Log muncul di halaman *Structured logs*; `TraceId`/`SpanId` ikut sebagai properti sehingga log dari satu interaksi bisa dikelompokkan. |

Contoh lewat variabel lingkungan (indeks 2 dst. karena 0 = Console dan 1 = File; array konfigurasi digabung per indeks):

```
Serilog__WriteTo__2__Name=Seq
Serilog__WriteTo__2__Args__serverUrl=http://seq:5341
Serilog__WriteTo__3__Name=OpenTelemetry
Serilog__WriteTo__3__Args__endpoint=http://aspire:4317
Serilog__WriteTo__3__Args__protocol=Grpc
Serilog__WriteTo__3__Args__resourceAttributes__service.name=panin-collection
```

Untuk mencoba di mesin pengembang: `docker compose -f docker/docker-compose.yml --profile observability up -d` (Seq di `http://localhost:5341`,
Aspire Dashboard di `http://localhost:18888`, OTLP di `localhost:4317`), lalu jalankan aplikasi dengan argumen `--Serilog:WriteTo:2:Name=Seq ...`.

Teruji (lokal): satu pencarian yang gagal menghasilkan kode yang sama di berkas, di Seq (`Ref`, `UserId`, `CircuitId` tersimpan sebagai properti),
dan di Aspire Dashboard (service `panin-collection`).

**Yang belum**: *trace* terdistribusi (diagram span request → sirkuit → query DB) belum diekspor. `Activity` sudah dibuat per request dan per interaksi
sirkuit (itu sumber `TraceId`), tetapi ekspornya butuh paket OpenTelemetry SDK + instrumentasi ASP.NET Core/SqlClient; ditambahkan bila tampilan
waterfall di Aspire/Jaeger memang dibutuhkan. Sink log (di atas) tidak membutuhkannya.

Pertimbangan: pengiriman ke Seq/OTLP bersifat tambahan dan tidak boleh menjadi satu-satunya tempat log (jaringan putus tidak boleh menghilangkan jejak);
karena itu berkas JSON tidak dimatikan.

## Aturan agar log aman

- Jangan mencatat password, token, nomor kartu, atau isi isian pengguna. Query dicatat tanpa nilai parameter.
- Pesan galat ke pengguna tidak berisi detail teknis; detailnya hanya di log, dihubungkan lewat kode.
- Laporan dari browser tidak dipercaya: panjang dibatasi dan dicatat sebagai properti terstruktur (bukan templat), jadi tidak bisa menyisipkan baris log palsu.

## Batas yang perlu diketahui

- Satu instance = satu direktori log. Bila nanti ada beberapa instance, arahkan semuanya ke penampung terpusat (tambahkan sink Seq/Elasticsearch/Loki di bagian `WriteTo`); `Machine` sudah dicatat untuk membedakannya.
- Event yang berjalan di luar aktivitas sirkuit dan bukan timer yang sudah dibungkus (mis. kode baru yang memakai `Task.Run`) tidak otomatis memiliki TraceId; bungkus dengan `Telemetry.Begin(...)` dan `CorrelationContext.Set(...)`.
- Pesan galat bawaan Blazor saat sirkuit mati ("Terjadi kesalahan. Muat ulang") belum memuat kode; kodenya tetap ada di log server pada `TraceId` yang sama.
