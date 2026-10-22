# Kontrak DLL lama `DMS.*` (hasil decompile, hanya dibaca)

Sumber: DLL di `LOSCOLLECTION/classes/` pada branch `origin/multibrowser` repo lama (commit `9078c82`),
didecompile dengan `ilspycmd` untuk memahami **perilaku**. Kode hasil decompile **tidak** disalin ke repo ini;
yang dicatat di sini hanya kontrak dan keputusan porting. Nilai rahasia (kunci enkripsi, password) sengaja tidak dicatat.

## Peta DLL

| DLL | Isi | Nasib di .NET 10 |
|---|---|---|
| `DMSTools.dll` (namespace `DMS.Tools`) | **Versi terbaru** dari `DMS.Tools.dll`: `DbConnection`, `MyConverter`, `MyPage`, `MyChecker`, `MyLogWriter`, `MyOtherTools` | `DbConnection` → `IDb`/`SqlDb` (sudah dibuat); `MyConverter` → library format tanggal/uang/terbilang; `MyPage` → hilang (WebForms); `MyOtherTools` → dekripsi connection string |
| `DMS.Tools.dll` | Versi lama yang sama (tanpa mode parameterized) | Diabaikan |
| `DMS.DBConnection.dll` | `Connection` (versi lebih tua dari `DbConnection`); dipakai di `DMS.CuBESCore` | Diganti `IDb` |
| `DMS.Framework.dll` | `staticFramework` (binding kontrol↔DB, save/insert/delete generik, alur maker–approver `parametersystem_*`), `dynamicFramework` (formula/foreign key dinamis), `Reflection`, `ModuleSupport` (auth menu, enkripsi query string, hash, log error), `DMS.Engine` (`DecSystem`, `CMS`, `SearchSystem`), `DMS.Interface` (`iExcel`, `oExcel`, `iHtml`) | Dipecah ke service (lihat di bawah) |
| `DMS.CuBESCore.dll` | `Logic` (hak akses menu, hitung angsuran/instalment, `ValidateLogin`), `Tools`, `GlobalTools` (format tanggal, uang, kontrol tanggal) | `Logic` → service domain; sisanya hilang/utilitas |
| `DMS.CuBESCore.Maintenance.dll` | `UserMaintenance.ApproveUser/ApproveGroup` | → modul Maintenance (Fase 5) |
| `DMSControls.dll` / `DMSControls17.dll` | Kontrol WebForms: `CC_Date`, `CC_RefList`, `TXT_NUMBER/DECIMAL/CURRENCY`, `BTN_UPDATE` | → komponen Blazor |

## `DbConnection` — perilaku yang HARUS dipertahankan

1. **Parameter posisional** `@1..@n` pada teks SQL.
2. **Mode default (produksi) menyisipkan nilai ke teks SQL**, bukan parameter sungguhan. Mode `parameterized` (appSetting `dbQueryMode`) ada tetapi tidak diaktifkan di config mana pun di repo.
   Konsekuensi: perlindungan injection lama hanya mengganti `'` → `''`. `SqlDb` baru **selalu** memakai parameter SQL sungguhan.
3. `exec nama_sp` yang tidak mengandung `@` otomatis dilengkapi `@1, @2, ...` sesuai jumlah argumen. (`DbCommandPreparer.PrepareSql`)
4. `BlankToNull` default **true**: string kosong/spasi → `NULL`. (`NormalizeValue`)
5. `DateTime` **1900-01-01 → NULL** pada mode default (sentinel "tanpa tanggal" dari kontrol lama). Mode parameterized lama tidak melakukannya; kode baru mengikuti mode produksi.
6. Argumen berupa **kontrol UI** (`SelectedValue` / `Checked` / `Value` / `Text`) diekstrak otomatis → tidak relevan di Blazor; pemanggil harus mengirim nilai.
7. Error SQL dibungkus `ApplicationException` dengan teks `Last Query: ...` (berguna untuk log; baru: log terstruktur tanpa nilai sensitif).
8. API stateful: `ExecReader` + `hasRow()` + `GetFieldValue(idx|nama)` (nilai selalu `ToString()`), `GetNativeFieldValue` (DBNull → default per tipe: tanggal 1900-01-01, int 0, float 0.0, bit false, lainnya null), `GetDataTable/GetDataSet`, `ExecTran/Commit/Rollback` (transaksi per koneksi), `ExecProc` (parameter output).
   → di `IDb` baru: tanpa state reader; hasil berupa `DataTable`/`DataRow`. **Belum ada**: transaksi dan SP dengan parameter output (ditambahkan saat modul yang membutuhkannya dimigrasi).
9. Timeout dikirim per panggilan; aplikasi memakai `dbTimeOut=600` dan `dbBigTimeOut=3600`.

## Dekripsi connection string

- `DMS.Tools.MyOtherTools.decryptConnStr` mendekripsi hanya nilai `pwd=`/`password=` dengan algoritma simetris bawaan .NET Framework (Rijndael/AES-CBC), kunci dan IV **hard-coded** dalam DLL (dapat ditimpa lewat appSettings `rgbKey`/`rgbIV`).
- Aplikasi lama punya **dua** skema: `authenticate.decryptConnStr` (geser karakter −2, setelah 2 karakter pertama) dan `MyOtherTools` (AES). Keduanya bukan perlindungan nyata.
- Keputusan: aplikasi baru **tidak** memakai keduanya. Connection string diambil dari secrets/env; connection string dinamis dari token SSO (`VW_ES_APPTOKEN`) hanya dipakai bila sistem lama masih berjalan paralel, dan harus didekripsi dengan kunci yang dikelola tim (butuh keputusan, lihat "Tindak lanjut").

## `iExcel` / `oExcel` (Interop → NPOI)

- `generate_excel(...)`, `generate_excel_report(...)` (DataSet / DataTable / DataView): buka template `.xls`; **nama field diambil dari *cell comment*** pada sel template; `ds.Tables` dipasangkan genap/ganjil: tabel `2n` = baris `[sel awal, sel akhir]` area berulang, tabel `2n+1` = data; area disalin per baris data, dihapus comment, ditambahkan page break manual, lalu sheet/workbook dilindungi password bila `pwd` diisi, disimpan sebagai file baru.
- `upload_excel(file, savequery, conn, rowstart, colstart, colend, pwd)`: buka workbook (opsional unprotect), sheet pertama, loop baris sampai kolom awal kosong, tiap baris dikirim sebagai argumen ke `savequery` (mis. `SP_UPLOAD_DATA_AYDA @1..@25, user`). Sel dengan data-validation **list** → ambil teks sebelum `" - "`; validation **date** → `FromOADate`. Mengembalikan teks error atau `""`.
- Porting: `ExcelTemplateWriter` dan `ExcelReader` berbasis NPOI (lihat `MIGRATION-PLAN.md` bagian 2C). Wajib diuji dengan template nyata (`Upload/Template/*.xls`).

## `iHtml.generateHtml`

Template HTML dari tabel `TEMPLATEHTML` (`TMPLVAL`) dengan blok `<%loop:N%> ... <%loop:N%>` untuk data berulang dan placeholder `@n` per kolom; hasil akhir dilewatkan ke `SP_GENERATE_DOCUMENT_HTML`. Dipakai oleh modul Surat. Port sebagai `HtmlTemplateRenderer`.

## `staticFramework` / `dynamicFramework` / `ModuleSupport` (peta porting)

| Fungsi lama | Rencana |
|---|---|
| `reff`, `retrieve*`, `fillRefList*` (isi dropdown dari query referensi) | `IReferenceService` yang mengembalikan daftar `(kode, deskripsi)` untuk komponen Blazor |
| `save`, `insert`, `Delete`, `updatestatus` (CRUD generik berbasis `NameValueCollection`) | Repository eksplisit per entitas / per SP; **tidak** membawa CRUD generik berbasis string |
| `parametersystem_*` (pending → approve, maker–approver) | Service Maintenance (Fase 5) |
| `dynamicFramework` (formula `@[FieldId]`, `FRAMEWORK_*`) | Re-implementasi dengan whitelist tabel/kolom (Fase 2, bersama list generik) |
| `ModuleSupport.AuthUserMenuAccess` / `GenMenuData` / `RequestQueryString` | `IMenuAccessService`, otorisasi berbasis policy; enkripsi query string tidak dibawa |
| `CalcSHA*` / `Encrypt` / `Decrypt` | Hash password SHA1 tetap didukung **hanya untuk verifikasi login** sampai ada keputusan upgrade |
| `Logic.hitungInstalment*` (anuitas, flat, in-advance/arrears) | `InstallmentCalculator` + unit test; perlu verifikasi angka terhadap sistem lama |
| `MyConverter.Terbilang`, `MoneyFormat`, nama bulan/hari Indonesia | Library `IndonesianFormat` + unit test |

## Tindak lanjut

1. Putuskan sumber dan kunci dekripsi untuk connection string dinamis dari token SSO (atau hentikan model itu saat tidak ada lagi aplikasi lama paralel).
2. Tambah `IDb` transaksi dan SP parameter output ketika dibutuhkan modul pertama.
3. Tulis test karakterisasi `Terbilang`/`MoneyFormat`/`hitungInstalment*` dengan nilai dari sistem lama.
