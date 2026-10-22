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
- Connection string dinamis per user disimpan di sisi server (circuit/scoped service), **tidak** di-session client.
- Auth: cookie authentication + claims (UserID, GroupID, ModuleID, area). Alur token SSO dipertahankan agar bisa jalan paralel dengan aplikasi lama.
- Layout Blazor (nav menu dinamis, tab/halaman) menggantikan iframe.
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
- Login (replika `SU_USERLOGINGIN`, cek revoke/lock/false-password-count, password default/expired).
- Kompatibilitas SHA1 hash; upgrade ke hash kuat bertahap saat login (butuh persetujuan karena ubah data kolom).
- Penerbitan & konsumsi token SSO (`ES_SAVETOKEN`, `VW_ES_APPTOKEN`, `ES_APPTOKEN_DELETE`).
- Change password, logout, log aktivitas (`SU_ALLUSERACTIVITY`, `SP_LOGUSER`).
- Layout, menu dinamis per group, switch modul, halaman news (`VW_NEWS`), otorisasi menu (`ES_AUTHUSERMENU`).
- Kriteria selesai: user bisa login di aplikasi baru dan lama dengan akun yang sama.

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
| 13 | Office Interop (Excel/Word) dan OleDb upload butuh Office/Jet di server | Sedang | Diganti NPOI (lihat 2C); uji dengan file template/upload nyata (AYDA, Visit, Surat, Bulk Surat) dan bandingkan output dengan versi lama |
| 14 | Perubahan SP/view di server tidak ada di repo (`Script/` hanya 1 file) | Tinggi | Dump skema dari server; kunci versi skema |
| 15 | Modul baru (Recovery/AYDA/Lelang) baru lewat SIT/UAT dan masih berubah (commit terakhir 2025-07) | Sedang | Bekukan baseline commit, catat perubahan sesudahnya, koordinasi dengan pengembang aktif |
| 12 | Tanpa dokumentasi/test lama → regresi sulit terdeteksi | Tinggi | Test karakterisasi, UAT dgn user bisnis, jalan paralel sebelum cutover |

## 6. Pertanyaan terbuka
0. Apakah repo `irfansyah/Panin.Collection` perlu private, dan siapa saja yang diberi akses? (Repo lama dibiarkan apa adanya.)
0b. Apakah pengembangan di `multibrowser` masih berjalan (UAT/produksi)? Apakah branch itu sudah yang dipakai di produksi?
1. Apakah ada akses ke DB dev/UAT dan backup skema (agar bisa dump view/SP)?
2. Lisensi DevExpress yang dimiliki (Blazor + Reporting + Office File API)?
3. Apakah LOSMIS benar-benar masih dipakai terpisah, atau cukup menjadi modul?
4. Fungsi `CCScoringParm.ocx` dan folder `Simulasi` masih dipakai?
5. Hosting target (IIS/Windows Server, Linux/Docker) dan kebutuhan jumlah user bersamaan?
6. Apakah password SHA1 boleh di-upgrade (butuh perubahan kolom/format) atau DB benar-benar tidak boleh disentuh?
7. Perlu fitur baru/perubahan UX, atau paritas fungsi 1:1?
