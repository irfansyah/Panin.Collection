# Rencana Migrasi Panin Collection → .NET 10

Status: **DRAFT v2 untuk review** (belum ada kode).

**Baseline source: branch `origin/multibrowser`** (commit `9078c82`, 2025-07-07, 14 commit di atas `main`). Revisi v1 dibuat dari `main` (`1252e8a`) yang ternyata sudah usang; inventaris di bawah sudah diperbarui dari branch ini.

## 1. Keputusan yang sudah diambil

| Topik | Keputusan |
|---|---|
| UI | Blazor Server (.NET 10) |
| Komponen grid/report/pivot | DevExpress versi terbaru (Blazor + Reporting) |
| Database | **Tetap sama** (SQL Server, skema/view/SP tidak diubah) |
| Source DLL `DMS.*` | Tidak tersedia → perilaku ditiru/di-decompile |
| Strategi | Bertahap: Login + Collection dulu, lalu MIS, MNT, service |
| Repo | `https://github.com/irfansyah/Panin.Collection` (baru); repo lama tidak disentuh; lihat bagian 2B |

## 2. Inventaris sistem lama

| Project | Peran | Skala | Target baru |
|---|---|---|---|
| LOSLOGIN | Portal login & penerbit token SSO | ~2 halaman | Area `Auth` |
| LOSCOLLECTION | Aplikasi utama + modul **Recovery/AYDA/Lelang** baru | ±130 aspx/ascx baru+lama, 58 report | Area `Collection` + `Recovery` |
| LOSMIS | Salinan hampir sama dgn LOSCOLLECTION (MIS/report) | 56 report, 6 halaman report | Area `Mis` (berbagi kode dgn Collection) |
| LOSMNT | Maintenance user/group/menu/parameter (maker–approver) | ~25 halaman | Area `Maintenance` |
| PANINSVC | Windows Service upload file → DB | ~840 baris | Worker Service |

Catatan koreksi: 58 report XtraReports (bukan 116; angka 116 file `.cs` termasuk file `.Designer.cs`). Banyak yang varian (Branch/Region/National × PGroup/PID/PType) sehingga bisa dikonsolidasi jadi report berparameter.

### Mekanisme penting di sistem lama
1. **SSO token**: LOSLOGIN memanggil `SU_USERLOGINGIN`, cek SHA1 password, simpan token (`ES_SAVETOKEN`). Aplikasi menerima `authenticate.aspx?tkn=`, baca `VW_ES_APPTOKEN`, hapus token (`ES_APPTOKEN_DELETE`), isi Session (UserID, GroupID, ConnString, dbTimeOut, area user dari `VW_AUTH_USERAREA`).
2. **Connection string dinamis per sesi**: DB aplikasi (IP, nama, uid, pwd) dibaca dari token, bukan dari config.
3. **Menu dinamis** dari `VW_GRPACCESSMENU*` / `RFMENUX` per group; switch antar modul via `VW_GRPACCESSMODULE`.
4. **Navigasi iframe** (`IFR_ISI`) dengan user control `Controls/*.ascx` (±50 control: Call, Visit, Recovery, Litigasi, PaymentSchedule, dll).
5. **Framework metadata-driven** (`clsFramework`, `clsListSys`, `clsReportSys`, `clsSearch`): list, filter, report dibangun dari tabel `FRAMEWORK_*` dan view `VW_*`, SQL disusun lewat string.
6. **Akses data**: `DMS.Tools.DbConnection` dengan parameter posisional `@1,@2`; mayoritas lewat view `VW_*` dan SP (`SP_*`, `SU_*`, `ES_*`).
7. **Surat/Template**: template HTML dengan editor WYSIWYG, bulk surat dari Excel, upload template `.xls`.
8. **Resource bahasa** `ind-lang.resx` / `control-list.resx` dipakai untuk label.

## 2A. Temuan dari branch `multibrowser` (baseline sebenarnya)

**Perubahan teknis**
- Upgrade **.NET Framework 3.5 → 4.7.2** dan **DevExpress 8.2 → 17.2** di semua project. Tujuan commit "multibrowser": aplikasi tidak lagi hanya jalan di IE. Konversi ke DevExpress terbaru kini bermula dari 17.2 (lebih dekat), tetapi tetap WebForms → Blazor, jadi penulisan ulang UI penuh.
- DLL internal bertambah: `DMSTools.dll`, `DMSControls17.dll` (build untuk DevExpress 17) dan namespace `DMS.Interface`, selain `DMS.Tools/DBConnection/CuBESCore/Framework`. Source tetap tidak ada. Pemakaian `using`: `DMS.Tools` 176×, `DMS.Framework` 58×, `DMS.CuBESCore` 11×, `DMS.DBConnection` 3×.
- **Office Interop** (`Interop.Excel`, plus referensi `Word/Office` yang tidak terlihat dipakai kode) dipakai di `Framework/clsExcel.cs` (Excel COM: buka template `.xls`, baca nama field dari *cell comment*, copy range, page break, print range) dan `Popup/UploadDataAYDA`. Butuh Office terpasang di server. **Keputusan: tidak ada dependensi Office di server; diganti NPOI** (lihat 2C).
- Upload file memakai `OleDb` (Jet/ACE) dan `iExcel.upload_excel` dari DLL DMS → diganti pembaca berbasis NPOI (xls/xlsx) dan CSV/teks biasa, tanpa driver OleDb.
- Folder `Script/` baru hanya berisi 1 SQL (`SP_RIWAYAT_BAYAR_LIHAT`, DB `LOSPANINCOLLECTION`, memakai `FNC_TANGGAL`). Artinya perubahan SP/view di server **tidak terlacak lengkap di repo**; skema harus di-dump dari server.

**Modul fungsional baru** (ada di branch, tidak ada di `main`):

| Modul | File / objek utama |
|---|---|
| Recovery (workflow) | `Controls/Recovery` (ditulis ulang besar), `Information_Recovery`, `Treatment_Recovery`, `RecoveryMonitoring`, `History_Recovery`, `Popup/Detail_Recovery`, `All_History_Recovery`, `Template/Recovery_Memo`; SP `SAVE_DATA_RECOVERY`, `SP_ALL_HISTORY_RECOVERY`, `GET_SCREEN_MENU_RECOVERY`, `SP_GET_TREATMENT_MENU_RECOVERY` |
| AYDA (agunan yang diambil alih) | `CariAYDA`, `TopTitleAYDA`, `Search_Data_AYDA`, `UploadDataAYDA`, `Report/AYDA`; SP `SP_SEARCH_AYDA`, `SP_PERHITUNGAN_AYDA`, `SP_UPDATE_DATA_AYDA`; `VW_SEARCH_PENGAJUAN_AYDA`, `VW_DETAIL_MONITORING_AYDA`, `RFKOLEKTIBILITY_AYDA` |
| Lelang | `BalaiLelang`, `History_Lelang`; SP `SAVE_DATA_LELANG` |
| Hapus Buku / Hapus Tagih / Restrukturisasi / SKPM | `CariHapusBuku/HapusTagih/Restrukturisasi/SKPM` + report `ReportHapusBuku/HapusTagih/Restrukturisasi`, `SKPM`; `VW_SEARCH_*`, `VW_REPORT_*` |
| Notes | `Controls/Notes`; SP `SP_GET_NOTES`, `SP_INSERT_NOTES` |
| Worklist distribusi | `SP_WORKLIST_DISTRIBUTION_INDEX/TEMPLATE`, `SP_WORKLIST_MONITORING_DEBITUR` |
| Surat & agunan baru | `SP_SAVE_SURAT_ROYA_JAMINAN`, `SP_SAVE_SURAT_PERNYATAAN`, `SP_CREATE_NO_SURAT`, `SP_SAVE_COLLATERAL`, `SP_VIEW_COLLATERAL`, `VW_COLLATERAL_*_ELO`, `VW_IMAGE_FILE` |
| Manual update & upload text | `Popup/ManualUpdate`, `UploadTextfile`; `VW_MANUAL_UPDATE` |
| Umum | `CommonFunction/UC_Date` (kontrol tanggal baru), `ind-lang.resx` bertambah ±3000 baris, `USP_VWDELINQHIST`, `VW_RFKCU`, `VW_SCUSER_MOVE`, `RFSTATUSAKUN`, `RFCOLLSTAT`, `RFCOLLCOND` |

**Perubahan lain**
- File contoh lama dihapus di branch (`Upload/Report`, `Upload/Surat`, sebagian `Visit`) → tidak perlu dimigrasi.
- `LOSLOGIN` dapat `login.css` baru; `LOSMNT` dapat `onepost.js` dan perubahan `UserMaker*`/`UserApprv` ("fix parameter user").
- `PANINSVC`: path masih hard-coded ke mesin developer → di versi baru wajib lewat konfigurasi.
- **Keamanan**: `Web.config` branch ini memuat host DB yang tampak produksi/eksternal (`182.16.172.124,1450`) beserta user/password (`dms`, `sa`, `usrcoll`). Semua credential tersebut harus dianggap bocor.

## 2B. Repo GitHub baru
- Repo dibuat **bersih tanpa riwayat git lama** agar credential, binari DevExpress 8.2/17.2, `.dll` DMS, dan file Upload tidak terbawa. Repo lama (`dms-digital-new/Panin-Collection`) tetap menjadi arsip/referensi.
- Repo baru: **https://github.com/irfansyah/Panin.Collection** (kosong saat dicek; clone lokal di `Collections/Panin.Collection`, bersebelahan dengan repo lama). Seluruh source baru ditulis hanya di sini.
- Repo lama `dms-digital-new/Panin-Collection` **tidak diubah dan tidak ditambah apa pun** (hanya dibaca sebagai referensi).
- Isi awal: `.gitignore` .NET, `global.json` (SDK 10), `Directory.Build.props`, `.editorconfig`, `README`, `docs/` (rencana ini), GitHub Actions (build + test), Dependabot, secret scanning + push protection.
- `main` terproteksi; kerja via `feature/*` + pull request.
- DLL `DMS.*` tidak di-commit; yang masuk adalah reimplementasinya. DevExpress lewat feed NuGet privat dengan token di GitHub Secrets.
- Catatan skema DB hasil dump disimpan tanpa credential.

## 2C. Pengganti Office Interop (tanpa Office di server)

**Pilihan: [NPOI](https://github.com/nissl-lab/npoi)** (Apache-2.0, mendukung .NET 8/10, tanpa dependensi native).
Alasan: aplikasi lama sangat bergantung pada template **`.xls` (BIFF)** (`Upload/Template/*.xls`, Visit, Surat, Slip Setoran) dan NPOI membaca/menulis **`.xls` (HSSF) dan `.xlsx` (XSSF)** dengan satu API. Fitur yang dipakai `clsExcel.cs` tersedia: cell comment, copy/clone row-range, merge, page break, print area, formula, style.

| Kebutuhan lama | Pengganti |
|---|---|
| `clsExcel` (buka template, isi sel berdasarkan *comment* sebagai nama field, replikasi blok, page break) | `ExcelTemplateWriter` berbasis NPOI (`HSSFWorkbook`/`XSSFWorkbook`), perilaku yang sama |
| Upload Excel via OleDb / `iExcel.upload_excel` | `ExcelReader` NPOI → `DataTable`/DTO → panggil SP yang sama (mis. `SP_UPLOAD_DATA_AYDA`) |
| Export grid ke Excel | Export bawaan DevExpress Blazor Grid / NPOI |
| Word (Interop, tidak terlihat dipakai) | Tidak dibawa; bila perlu, `DocumentFormat.OpenXml` atau DevExpress Office File API |
| Surat dari template HTML | Dirender HTML → PDF oleh DevExpress Reporting / QuestPDF (diputuskan di Fase 3) |

Catatan: NPOI **tidak menghitung ulang formula kompleks atau merender/print** seperti Excel; bila ada template yang bergantung pada itu, hasil dibandingkan per template di UAT. Konversi `.xls` → `.xlsx` boleh dilakukan bertahap untuk template baru, tetapi `.xls` lama tetap didukung.

## 2D. Model host tunggal, modul sebagai tab, dan dua database

Dibaca dari `LOSLOGIN/Login.aspx.cs`, `LOSCOLLECTION/{authenticate,Body,Footer,Logout}.aspx.cs`, `main.html`, `warning.html`, dan `Web.config` semua project (branch `multibrowser`).

### Yang sebenarnya terjadi di sistem lama

Sistem lama terlihat seperti satu aplikasi, tetapi sebenarnya **beberapa web app terpisah** yang disambung oleh SSO berbasis token:

| ModuleID | App | Isi |
|---|---|---|
| `00` | LOSLOGIN | Portal login |
| `60` | LOSCOLLECTION | Collection / Recovery |
| `70` | LOSMIS | MIS / report |
| `99` | LOSMNT | Maintenance user, group, menu, parameter (`MaintainedModuleIDs` = `60;99`) |

**Dua jenis database**
1. **DB LOGIN** (`LOSPANINLOGIN`; dev: `..._DEV`). Setiap app membukanya lewat `eSecurityConnectString`. Isinya: user (`SCUSER`, `SCALLUSER`, `SCALLUSERFLAG`), group (`SCALLGROUP`), **daftar modul `RFMODULE`**, hak akses modul per group (`VW_GRPACCESSMODULE`), **menu per modul dan group (`RFMENUX` + `GRPMENUX`)**, token SSO (`ES_APPTOKEN`, `VW_ES_APPTOKEN`), log aktivitas (`SU_ALLUSERACTIVITY`, `SU_MONITORACTIVITY`), berita (`VW_NEWS`), area user (`VW_AUTH_USERAREA`).
2. **DB DATA** per modul (mis. `LOSPANINCOLLECTION` untuk modul 60; SP dan view bisnis seperti `ACCOUNT_MASTER`, `VW_RECOVERY`, `SP_*`). Alamat DB DATA **tidak ada di config aplikasi**: tersimpan di baris `RFMODULE` pada DB LOGIN (`LOGIN_SCR`, `DB_IP`, `DB_NAMA`, `DB_LOGINID`, `DB_LOGINPWD`).

**Alur login dan pindah modul**
1. User login di LOSLOGIN → `exec SU_USERLOGINGIN user, ip` (SHA1, revoke/lock, `SU_LOGON`, password default/expired, hitung salah password) → `SU_LOGINSTARTED` → `ES_SAVETOKEN(token, user)` mengembalikan `LOGIN_SCR` (URL modul pertama) → redirect `URL?tkn=<guid>&urllogin=1`.
2. Modul menerima `authenticate.aspx?tkn=`: baca `VW_ES_APPTOKEN` (USERID, GROUPID, MODULEID, DB_IP, DB_NAMA, DB_LOGINID, DB_LOGINPWD), **hapus token (sekali pakai)**, rakit connection string DATA dari kolom `DB_*`, isi Session (`UserID`, `FullName`, `GroupID`, `ModuleID`, `ConnString` (= DATA), `dbTimeOut`, `LoginTime`) dan area user (`AreaID`, `CityID`, `BranchID` dari `VW_AUTH_USERAREA` di DB DATA).
3. `Body.aspx` memuat **tab modul** (`VW_GRPACCESSMODULE` untuk group user; modul aktif tidak bisa diklik; bila hanya 1 modul, deretan tab disembunyikan) dan **menu modul aktif** dari DB LOGIN (`RFMENUX` join `GRPMENUX` menurut `TYPEID = ModuleID` dan `GROUPID`, hierarki lewat `MENUPARENT`/`MENUPOSITION`, ditambah item `Logout`). Menu kosong → logout dengan pesan "Menu Access Not Yet Defined".
4. Klik tab modul lain → `ES_SAVETOKEN(token, user, moduleId)` → redirect ke `LOGIN_SCR` modul itu dengan token baru; **Session lama dibuang**. Jadi pindah modul = keluar dari app lama dan masuk app lain (state hilang).
5. Logout: `SU_ALLUSERACTIVITY` (catat logout) → bersihkan Session → redirect ke `LOGIN_SCR` modul 00.

**Kerangka halaman** (`main.html`): frameset 3 baris: header (logo), `Body.aspx` (menu + tab modul + isi dalam iframe), footer. Footer menampilkan nama lengkap, nama group, user ID, jam, serta menjalankan:
- **Heartbeat/monitor sesi** → `SU_MONITORACTIVITY(user, ip)`; bila hasil `0` → paksa logout (mengakhiri sesi ganda/dicabut admin).
- **Peringatan idle**: dialog "idle 20 menit, sesi ditutup otomatis dalam N menit" (`warning.html`), tertutup bila user bergerak; lalu `Logout`.
- **Cek kesegaran data**: `max(AS_OF_DATE)` di `ACCOUNT_MASTER` (DB DATA); bila tertinggal → alert "Tanggal update data terakhir …".
- **Cek warning**: `SP_VW_WARNINGCONTENT(user)` di DB DATA; bila ada baris → tampilkan pop-up warning.
- `onepost.js`: pencegah double-submit.

**Aturan bisnis login yang harus dipertahankan**
- Hanya satu login aktif per user (`SU_LOGON=1` → "User is currently logged in!"); karena itu logout/timeout/heartbeat wajib menurunkan flag.
- Password default → paksa `ChangePassword?initial`; kedaluwarsa → `ChangePassword?expired`.
- Revoke/lock setelah N salah password (`SU_FALSEPWDCOUNT`, `app_parameter.loginmaxattempt`), pesan berbeda untuk "terkunci" dan "baru saja terkunci".
- Login memakai SHA1 tanpa salt (`SU_PWD`); tidak diubah di Fase 1.

### Desain di sistem baru

**Satu host Blazor Server**; modul menjadi bagian dalam host yang sama, **tanpa redirect antar-app dan tanpa token untuk pindah modul**.

```
Panin.Web (satu proses)
 ├─ Auth             login, ganti password, logout, timeout idle, heartbeat
 ├─ Shell            header, tab modul, menu modul aktif, area konten, footer
 └─ Modul (area)     60 Collection, 70 MIS, 99 Maintenance  (terdaftar lewat IModule)
```

Komponen kunci (sebagian di `Panin.Application`, implementasi di `Panin.Infrastructure`):

| Komponen | Tugas |
|---|---|
| `ILoginDb` (`IDb` terdaftar dengan nama "Login") | Akses DB LOGIN; connection string dari secrets (`ConnectionStrings:Login`), bukan dari baris DB |
| `IModuleRegistry` | Baca `RFMODULE` dari DB LOGIN (cache); modul yang dikenal host dipasangkan dengan `IModule` di kode (`ModuleId`, rute dasar, nama) |
| `IModuleDbFactory` | `GetDataDb(moduleId)` → `IDb` ke DB DATA modul itu. Prioritas: (1) override dari config/secrets `ConnectionStrings:Data:<moduleId>`, (2) baris `RFMODULE` (`DB_*`). Opsi (2) hanya untuk paritas dengan sistem lama |
| `IUserSession` (scoped per sirkuit Blazor) | `UserId`, `FullName`, `GroupId`, `GroupName`, `Area/City/Branch`, `LoginTime`, daftar modul diizinkan, modul aktif, per-modul state |
| `IMenuService` | Menu modul dari `RFMENUX`/`GRPMENUX` (tree) + `Logout`; cache per (modul, group); pemetaan `MENUURL` lama → rute Blazor |
| `ISessionMonitor` | Heartbeat `SU_MONITORACTIVITY`, hitung idle, dialog peringatan, logout otomatis, panggil `SU_ALLUSERACTIVITY` saat keluar |

**Tab modul**: tampil bila group punya lebih dari satu modul (sama seperti lama). Tab menyimpan state modulnya masing-masing selama sesi (beda dari lama yang kehilangan state saat pindah) dan modul dimuat malas saat pertama dibuka. Setiap modul memakai `IDb` DATA-nya sendiri; hak akses menu tetap dari DB LOGIN.

**Menu URL**: `RFMENUX.MENUURL` berisi path halaman WebForms (mis. `~/Main/Main.aspx?...`). Migrasi butuh tabel pemetaan `url lama → rute baru` di kode (bukan mengubah DB). Menu yang belum dimigrasi tampil nonaktif atau disembunyikan lewat konfigurasi. Selama jalan paralel, menu bisa mengarah ke app lama dengan token.

**Token SSO**: tidak dipakai di dalam host. Tetap didukung (opsional) untuk dua hal: (a) menerima login dari LOSLOGIN lama selama masa paralel, (b) mengarahkan menu yang belum dimigrasi ke app lama. Kedua-duanya memakai `ES_SAVETOKEN`/`VW_ES_APPTOKEN`/`ES_APPTOKEN_DELETE` apa adanya.

**Kredensial DB DATA di `RFMODULE`**: sistem lama menyimpan dan memakai `DB_LOGINPWD` apa adanya (plaintext) lalu menyambungkannya ke connection string. Host baru memakai override dari secrets sebagai jalur utama dan menjadikan `RFMODULE` hanya fallback; kolom itu sebaiknya dikosongkan setelah cutover.

### Dampak ke fase

- **Fase 1** (Auth & shell) mencakup login, ganti password, logout, heartbeat/idle, tab modul + menu, dan `IModuleDbFactory` (lihat di bawah).
- **Fase 5** (Maintenance, modul 99) memakai DB LOGIN sebagai data utamanya (mengelola user/group/menu/parameter untuk modul 60 dan 99); `MaintainedModuleIDs` menjadi konfigurasi modul.
- Aplikasi MIS (70) dan Maintenance (99) menjadi area dalam host yang sama, bukan project terpisah.

## 3. Arsitektur target

```
PaninCollection.sln
├─ src/
│  ├─ Panin.Domain            (entity/DTO, enum; tanpa dependensi)
│  ├─ Panin.Application       (use case, interface repository, validasi)
│  ├─ Panin.Infrastructure    (Dapper + SqlClient, repo; wrapper SP/view; DbConnection pengganti DMS)
│  ├─ Panin.Web               (Blazor Server; Areas/Pages: Auth, Collection, Mis, Maintenance)
│  ├─ Panin.Reporting         (report DevExpress hasil konversi)
│  └─ Panin.Worker            (pengganti PANINSVC)
└─ tests/
   ├─ Panin.UnitTests
   └─ Panin.IntegrationTests  (terhadap salinan DB dev)
```

Prinsip:
- **Dapper, bukan EF Core**: skema tidak boleh berubah, logika ada di view/SP; Dapper memanggil mereka apa adanya dan lebih mudah dipetakan 1:1 dari SQL lama. EF hanya jika ada kebutuhan khusus.
- **Semua SQL berparameter**; framework dinamis disusun ulang dengan whitelist nama tabel/kolom (bukan konkatenasi input).
- **Satu codebase untuk Collection + MIS** dibedakan oleh konfigurasi/modul, menghapus duplikasi.
- **Satu host, dua jenis database** (bagian 2D): DB LOGIN lewat secrets; DB DATA per modul lewat `IModuleDbFactory` (override secrets, fallback `RFMODULE`). Connection string tidak pernah dikirim ke klien.
- Auth: cookie authentication + claims (UserID, GroupID, area). Pindah modul tidak memakai token; token SSO hanya untuk masa paralel dengan aplikasi lama.
- Layout Blazor (header, tab modul, menu modul aktif, area konten, footer) menggantikan frameset/iframe.
- Konfigurasi rahasia via User Secrets / environment variable / vault.

## 4. Fase

### Fase 0 — Fondasi & penemuan (±1–2 minggu)
- Inspeksi/decompile `DMS.Tools`, `DMS.DBConnection`, `DMS.Framework`, `DMS.CuBESCore(.Maintenance)`, `DMSControls` → dokumentasikan kontrak (ExecReader, GetDataTable, hasRow, GetFieldValue, timeout, konvensi `@n`).
- Dump skema DB dev (tabel, view, SP) → dokumen data dictionary; daftar SP/view yang dipakai per halaman.
- Buat solution skeleton, CI build, konvensi kode, logging (Serilog), error log (`USP_ERRORLOG_SAVE`).
- Siapkan lisensi & feed DevExpress; PoC grid + 1 report pada Blazor Server.
- Pindahkan semua kredensial ke secrets; **rotasi password `sa`** yang sudah ada di git.
- Buat repo GitHub baru (bagian 2B) dengan CI dan secret scanning.
- Dump skema **dari server** (view/SP/FNC) dan cocokkan dengan objek yang dipanggil kode branch `multibrowser`.
- Keluaran: `Panin.Infrastructure` dengan `IDb` pengganti `DbConnection`, test terhadap DB dev.

### Fase 1 — Auth & shell (±2 minggu)
- **Akses dua database**: `ILoginDb`, `IModuleRegistry` (baca `RFMODULE`), `IModuleDbFactory` (DB DATA per modul), cache modul.
- Login di host yang sama (replika `SU_USERLOGINGIN` → `SU_LOGINSTARTED`; revoke/lock, `SU_LOGON` login tunggal, false-password-count, password default/expired → ganti password).
- Kompatibilitas SHA1 hash; upgrade ke hash kuat bertahap saat login (butuh persetujuan karena ubah data kolom).
- `IUserSession` (scoped per sirkuit): user, group, area/kota/cabang (`VW_AUTH_USERAREA` di DB DATA), modul diizinkan (`VW_GRPACCESSMODULE`), modul aktif.
- Shell: header, **tab modul** (disembunyikan bila hanya 1 modul), **menu modul aktif** (`RFMENUX` + `GRPMENUX`, hierarki, item Logout, pemetaan `MENUURL` lama → rute baru), area konten, footer (nama, group, jam).
- `ISessionMonitor`: heartbeat `SU_MONITORACTIVITY` (paksa logout bila 0), peringatan idle 20 menit + logout otomatis, `SU_ALLUSERACTIVITY` saat logout/timeout, penurunan flag `SU_LOGON` saat sirkuit terputus.
- Footer: cek kesegaran data (`ACCOUNT_MASTER.AS_OF_DATE`), cek warning (`SP_VW_WARNINGCONTENT`), halaman news (`VW_NEWS`), otorisasi menu (`ES_AUTHUSERMENU`).
- Token SSO (`ES_SAVETOKEN`, `VW_ES_APPTOKEN`, `ES_APPTOKEN_DELETE`) hanya sebagai jembatan ke aplikasi lama selama paralel.
- Kriteria selesai: user login di host baru dengan akun yang sama, melihat tab modul dan menu sesuai group, dan sesi/heartbeat/logout berperilaku seperti lama.

### Fase 2 — Inti Collection (±6–8 minggu)
Urutan berdasarkan ketergantungan:
1. Komponen umum: pemilih tanggal/mata uang, filter cascade, lookup/popup search, list generic (pengganti `clsListSys` + `List.aspx`), export Excel.
2. Pencarian & info debitur: `Search_Debitur`, `CustContactInfo`, `personalinfo/job`, `spouse*`, `RelationInfo`, `AgunanKredit`, `Asuransi*`.
3. Worklist & monitoring: `WorklistMonitoring`, `AccountSearchMonitoring`, `Collection`, `Treatment`, `DetailTunggakan`.
4. Aktivitas: `Call`, `Visit`, `Meeting`, `CallVisitResult`, `Call_Rec`, `visit_rec`, `Email`, `SkipTracing`, upload visit.
5. Pembayaran: `PaymentSchedule`, `RiwayatPembayaran`, `PayOff`, `PCHI`, `Payment`/`PaymentHistory`.
6. Calendar/PTP: `Calendar`, `SP_PTP_CHANGELIST`, `Search_by_ptp`.

### Fase 3 — Recovery, AYDA, Lelang, Litigasi, Simulasi, Surat (±8–10 minggu)
- Modul baru dari `multibrowser`: workflow Recovery, AYDA (cari/upload/hitung/report), Balai Lelang, Hapus Buku/Tagih, SKPM, Restrukturisasi, Notes, distribusi worklist, collateral (lihat 2A).
- `Recovery/*` (RecoveryList, ListDetail, CredRestru, ChangeInterest, InstSched, PSJT, ProcJatuhTempo), `Litigasi`.
- `Simulasi/*` (MySimulasi, PSJT, Restrukturisasi) + `simulasi_restru`.
- Surat: template, `LetterTemplate`, `InputSurat`, `BulkSurat`, `SP_SAVE_SURAT_TEGURAN`, memo Call/Visit, SMS template, warning. Ganti openwysiwyg/ASPxHtmlEditor dengan DevExpress Rich Text/HTML Editor Blazor.
- Facilities: `Atribut_user`, `Maintenance_Userid`, `Problem_Handlin`.
- `History`, `All_history`, upload manual data (`UploadData/CollectionProcess`).

### Fase 4 — Report & MIS (±6–8 minggu, bisa paralel dengan Fase 3)
- Konversi 58 report XtraReports ke DevExpress Reporting terbaru; konsolidasi varian jadi report berparameter (level: Branch/Region/National × grouping PGroup/PID/PType).
- Ad-hoc report, report designer, pivot, report set/custom (`ReportAdhock/Custom/Designer/Pivot/Set`), SP `SP_VW_REPORT_*`.
- Area `Mis`: pakai kode bersama Collection, beda menu/modul.
- Validasi angka: bandingkan output report lama vs baru pada data yang sama.

### Fase 5 — Maintenance (LOSMNT) (±3–4 minggu)
- User/Group maintenance + alur maker–approver (`UserMaker/Apprv`, `GroupMaker/Apprv`, `ParamMaker/Apprv`, `ParamSet`), akses menu per group, `RefCh/RefSearch`.
- SP: `SU_SCALLUSER_*`, `SU_SCALLGROUP_*`, `SU_SCUSERPASSWORD`, `VW_*PENDING`.

### Fase 6 — Worker service (±1–2 minggu)
- Port `PANINSVC` ke `BackgroundService`: scan folder, upload file `AOC;CAB;PRD;REL;CUS;ACC;HIS;COL;DUE;MIS`, pindah ke Done/Fail, panggil `SP_UPLOAD_CUSTACC`, jendela waktu & interval dari config.

### Fase 7 — Hardening & cutover (±3–4 minggu)
- UAT per modul, uji performa (sesi besar, list ribuan baris), uji keamanan (OWASP), pen-test ringan.
- Rencana jalan paralel (lama & baru pada DB yang sama), cutover per kelompok user, rollback.
- Dokumentasi & pelatihan, pensiunkan aplikasi lama.

Total estimasi kasar (dinaikkan karena modul baru): **±9–11 bulan** untuk 1 tim kecil (3–4 dev); sangat bergantung pada jumlah user control yang punya logika kompleks dan ketersediaan DB dev/UAT.

## 5. Risiko dan mitigasi

| # | Risiko | Dampak | Mitigasi |
|---|---|---|---|
| 1 | Source `DMS.*` hilang; perilaku tersembunyi | Tinggi | Decompile Fase 0, test karakterisasi terhadap DB dev, bandingkan hasil lama vs baru |
| 2 | Logika bisnis ada di view/SP yang tidak terdokumentasi | Tinggi | Dump skema, petakan SP/view per halaman, jangan ubah DB; test integrasi |
| 3 | Framework dinamis berbasis string SQL (`FRAMEWORK_*`) sulit dipetakan & rawan injeksi | Tinggi | Re-implementasi dengan whitelist & parameter; mulai dari list/filter yang paling dipakai |
| 4 | Kredensial `sa`/`dms` dan host DB tampak produksi (`182.16.172.124,1450`) ada di git; "enkripsi" hanya geser −2 | Tinggi (keamanan) | Rotasi semua password sekarang, repo baru tanpa riwayat lama, secrets, akun DB least-privilege, batasi akses jaringan DB |
| 5 | Password SHA1 tanpa salt | Sedang | Tetap kompatibel dulu; upgrade bertahap setelah persetujuan, rate-limit & lockout |
| 6 | Konversi report DevExpress 8.2 → terbaru tidak otomatis | Tinggi | Pilot 3–5 report dulu, konsolidasi varian, validasi angka |
| 7 | ActiveX `CCScoringParm.ocx`, JS lama (`openwysiwyg`, `date_picker`) | Sedang | Cari tahu fungsinya; ganti dgn komponen Blazor/JS modern |
| 8 | Session & state kompleks (BackURL, LIST, ConnString) | Sedang | Scoped service per circuit; hindari state besar di memori; persist yang perlu |
| 9 | Blazor Server: state in-memory per user, koneksi SignalR (jaringan internal/proxy) | Sedang | Uji ke load balancer/reverse proxy, sticky session, batasi payload grid (paging server-side) |
| 10 | Duplikasi Collection vs MIS, kode mati | Rendah | Satu codebase; buang `.bak` & kode komentar |
| 11 | Lisensi/eligibility DevExpress terbaru | Sedang | Konfirmasi lisensi sebelum Fase 0 selesai |
| 16 | `SU_LOGON` login tunggal: sirkuit Blazor yang terputus/ditutup tanpa logout membuat user "masih login" dan terkunci | Tinggi | Heartbeat + timeout sisi server, turunkan flag saat sirkuit disposed/idle, jalur admin untuk reset; uji tab ditutup, jaringan putus, restart server |
| 17 | `MENUURL` di `RFMENUX` menunjuk halaman WebForms lama; menu tak bisa diubah (DB tidak boleh disentuh) | Sedang | Tabel pemetaan di kode; menu belum termigrasi disembunyikan/dinonaktifkan atau mengarah ke app lama via token selama paralel |
| 18 | DB DATA ditentukan dari `RFMODULE` (password plaintext di DB LOGIN) | Sedang | Override via secrets sebagai jalur utama; `RFMODULE` hanya fallback; kosongkan kolom setelah cutover |
| 19 | Satu host berarti satu titik gagal untuk semua modul dan memori sirkuit bertambah dengan banyak tab | Sedang | Modul dimuat malas, batasi state per tab, pantau memori, siapkan skala (sticky session) |
| 13 | Office Interop (Excel/Word) dan OleDb upload butuh Office/Jet di server | Sedang | Diganti NPOI (lihat 2C); uji dengan file template/upload nyata (AYDA, Visit, Surat, Bulk Surat) dan bandingkan output dengan versi lama |
| 14 | Perubahan SP/view di server tidak ada di repo (`Script/` hanya 1 file) | Tinggi | Dump skema dari server; kunci versi skema |
| 15 | Modul baru (Recovery/AYDA/Lelang) baru lewat SIT/UAT dan masih berubah (commit terakhir 2025-07) | Sedang | Bekukan baseline commit, catat perubahan sesudahnya, koordinasi dengan pengembang aktif |
| 12 | Tanpa dokumentasi/test lama → regresi sulit terdeteksi | Tinggi | Test karakterisasi, UAT dgn user bisnis, jalan paralel sebelum cutover |

## 6. Pertanyaan terbuka
0. Apakah repo `irfansyah/Panin.Collection` perlu private, dan siapa saja yang diberi akses? (Repo lama dibiarkan apa adanya.)
0a. Tab modul: state tiap modul dipertahankan selama sesi (usulan) atau dimulai ulang saat berpindah seperti sistem lama?
0c. DB DATA modul 99 (Maintenance): apakah sama dengan DB LOGIN? Berapa DB DATA yang ada selain `LOSPANINCOLLECTION`, dan apakah modul 70 (MIS) memakai DB yang sama dengan 60?
0d. Apakah user boleh login di lebih dari satu perangkat/tab (aturan `SU_LOGON` lama melarangnya)?
0b. Apakah pengembangan di `multibrowser` masih berjalan (UAT/produksi)? Apakah branch itu sudah yang dipakai di produksi?
1. Apakah ada akses ke DB dev/UAT dan backup skema (agar bisa dump view/SP)?
2. Lisensi DevExpress yang dimiliki (Blazor + Reporting + Office File API)?
3. Apakah LOSMIS benar-benar masih dipakai terpisah, atau cukup menjadi modul?
4. Fungsi `CCScoringParm.ocx` dan folder `Simulasi` masih dipakai?
5. Hosting target (IIS/Windows Server, Linux/Docker) dan kebutuhan jumlah user bersamaan?
6. Apakah password SHA1 boleh di-upgrade (butuh perubahan kolom/format) atau DB benar-benar tidak boleh disentuh?
7. Perlu fitur baru/perubahan UX, atau paritas fungsi 1:1?
