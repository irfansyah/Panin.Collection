# Registry query/SP/view

Setiap query, stored procedure, dan view yang dipanggil aplikasi baru dicatat di sini dengan hasil ukur. Aturannya ada di [`PERFORMANCE.md`](PERFORMANCE.md). Waktu diukur terhadap DB dev lama (data berukuran produksi: 760.909 rekening, `SCALLUSERLOG` ±1 juta baris) dari mesin pengembang, jadi nilainya sebagai pembanding relatif, bukan janji produksi.

Keputusan: **Pakai** (apa adanya), **Tulis ulang** (query setara yang lebih cepat/berbatas, divalidasi terhadap aslinya), **Tunda** (belum dipakai), **Hindari**.

## DB LOGIN

Angka login/logout di bawah diambil dari pengamat query aplikasi (`/_diag/queries`) pada uji 200 sirkuit ke salinan DB LOGIN di Docker, bukan tebakan.

| Objek | Dipakai oleh | Ukur | Keputusan | Catatan |
|---|---|---|---|---|
| `SU_USERLOGINGIN` | login | rata-rata 29 ms (maks 212 ms) pada 200 login | Pakai | |
| `SU_ALLUSERACTIVITY` | login, logout | login rata-rata 18 ms (maks 124 ms); logout memanggil `SP_LOGUSER` | Pakai + ulang saat deadlock | |
| `SU_LOGINSTARTED` | login | rata-rata 12 ms | Pakai | |
| `SP_LOGUSER` | login (insert), logout (update) | login (insert) rata-rata 15 ms; logout ±140 ms CPU: memindai `SCALLUSERLOG` (±1 juta baris); deadlock saat serentak | Pakai, dicatat di latar + `DeadlockRetry` | Indeks `IX_SCALLUSERLOG_USER_IP_LOGIN` (usulan) memangkas 700 logout dari ±60 s ke ±6 s. **Belum di DB lama.** |
| `SU_MONITORACTIVITY` | detak sesi tiap 60 s per sirkuit | rata-rata 16 ms (maks 33 ms), 400 panggilan |  Pakai | 700 sirkuit = ±12 panggilan/detik, stabil di uji 1.000 sirkuit |
| `SU_SCALLUSERPASSWORD`, `dbo.isPwdValid` | ganti password | belum diukur terpisah (jalur jarang) | Pakai | |
| `VW_SESSIONLOS` | data user | rata-rata 4 ms | Pakai | |
| `SELECT SU_REVOKE FROM scalluserflag ...` | cek terkunci saat login | rata-rata 11 ms | Pakai | |
| `VW_GRPACCESSMODULE` | tab modul | 1–38 ms; 3 panggilan untuk 200 login (single-flight, cache 5 menit) | Pakai | sebelum single-flight: 13 panggilan untuk 200 login |
| `RFMENUX` ⨝ `GRPMENUX` | menu modul | 1–18 ms; 3 panggilan untuk 200 login (single-flight, cache 5 menit) | Pakai | sebelum single-flight: 15 panggilan |
| `RFMODULE` | konfigurasi modul | jarang dipakai (hanya fallback koneksi), di-cache 5 menit | Pakai | berisi password DB DATA (plaintext) di sistem lama |

## DB DATA modul 60 (`LOSPANINCOLLECTION`)

| Objek | Dipakai oleh | Ukur | Keputusan | Catatan |
|---|---|---|---|---|
| `SELECT max(AS_OF_DATE) FROM ACCOUNT_MASTER` | footer: kesegaran data | sendirian 66–107 ms; **175 panggilan bersamaan rata-rata 6,6 s** sebelum single-flight; sesudah: 1 panggilan (623 ms) untuk 200 sirkuit | Pakai + `SingleFlightCache` | bukti pentingnya single-flight |
| `VW_AUTH_USERAREA` | area user | 9–59 ms, di-cache 30 menit per user | Pakai | |
| `SP_VW_WARNINGCONTENT` | footer: peringatan | sendirian ±25 ms; **326 panggilan bersamaan rata-rata 8,3 s** sebelum perbaikan; sesudah (ditunda acak, maks 6 bersamaan, cache 5 menit): 200 panggilan rata-rata 57 ms | Pakai + gate + tunda | baca-saja (tidak ada INSERT/UPDATE/DELETE); mengembalikan HTML, diparsing di `WarningService` |
| `SP_SEARCH_DEBITUR` | Inquiry Status Debitur | nama pendek **56 s / 335 ribu baris**; tanpa kriteria **116 s / 760 ribu baris**; nama biasa ±14 s hingga baris pertama; no. rekening 0,3–0,7 s; prefiks 5 digit 6,5 s / 47 ribu baris | **Tulis ulang** (`DebiturSearchService`) | Query setara tanpa `LEFT JOIN TREATMENT_HISTORY` (kolomnya tidak dipakai SP) dan dengan `TOP 500`: 0,1–5 s. Divalidasi 24/24 identik terhadap SP (himpunan rekening dan nilai kolom). |
| `VW_SEARCH_DEBITUR_LEFT` | pencarian debitur | `TOP 200` nama: ±100 ms; `count(*)` nama: ±1 s; nama jarang (SUHARTO) ±5 s (memindai seluruh data) | Pakai dengan `TOP` | nama jarang tetap memindai; kandidat indeks/ pencarian teks bila perlu |
| `ACCOUNT_MASTER` (join status) | pencarian debitur | ikut dalam query di atas | Pakai | |
| `VW_ACCOUNT_SURAT` | Detail debitur (kredit, tunggakan) | `SELECT *` (74 kolom) **276 ms**; hanya 45 kolom yang dipakai **171 ms** (terbaik dari 3 di DB dev); panggilan pertama di aplikasi ±600 ms | Pakai, kolom dipilih satu per satu | Layar lama memanggil `SELECT *` dua kali berurutan (`VW_ACCOUNT_SURAT` dan `VW_CUSTOMER_SURAT` ±254 ms); halaman baru hanya satu view dan nama/cabang diambil dari kolomnya |
| `VW_CUSTOMER_PERSONALINFO` | tab Info Personal (dimuat saat dibuka) | ±263 ms (pertama di aplikasi ±480 ms) | Pakai (`SELECT *` perlu dipersempit) | |
| `SP_JOB_INFO`, `SP_SPOUSE_INFO`, `SP_SPOUSE_JOB` | tab Pekerjaan, Pasangan, Pekerjaan Pasangan | 21–34 ms | Pakai (baca-saja, diperiksa) | dimuat hanya saat tab dibuka |
| `SP_GET_CONTACT_INFO` | tab Kontak | ±26 ms | Pakai (baca-saja) | |
| `USP_VWDELINQHIST` | riwayat kolektibilitas | ±27 ms (pertama di aplikasi ±1 s) | Pakai (baca-saja) | |
| `LOAN_MASTER` ⨝ `RFPRODUCT` (APP_TYPE) | sorotan PRK | tidak diukur terpisah (pertama di aplikasi ±550 ms, 0 baris untuk rekening tanpa produk) | Pakai | |
| `SP_VIEW_OTHER_RELATION` | tab Relasi lain | 30 ms | **Tunda** | parameter `@FUNC` belum jelas artinya |
| `SP_ACCOUNT_MONITORING_TREE` | Monitoring Debitur: tim di bawah supervisor | **7,3 s** untuk 39 anggota (2,9 s untuk 17, 1,7 s untuk 10): subquery berkorelasi per anggota ke `LOAN_MASTER`/`LOAN_DETAIL` | **Tulis ulang** (`AccountMonitoringService.TeamSql`) | Gabungan sekali jalan: 0,18 s / 0,055 s / 0,052 s, angka identik (jumlah debitur, O/S, target, achievement) pada 3 supervisor. Menghapus HTML tautan dari SP. |
| `SP_ACCOUNT_MONITORING_DEBITUR` | Monitoring Debitur: daftar debitur (Pending/Touched) | pengguna biasa 0,03–0,6 s; supervisor 2.460 baris 0,4 s; pengguna teratas **13.249 baris 2,2 s** (Touched 1,4 s); dalam aplikasi 0,2–2 s | Pakai + batas 2.000 baris + cache 60 s + maks 6 bersamaan | memakai tabel sementara dan logika panjang; baca-saja (diperiksa). Hasil tak berbatas di SP aslinya. |
| `SP_GET_TARGET_ACHIEVMENT` | ringkasan target/pencapaian | 17–23 ms (dalam aplikasi hingga 0,5 s pertama) | Pakai, cache 60 s | baca-saja |
| `SP_GET_OS` | total outstanding | 127–161 ms (dalam aplikasi 0,45 s rata-rata) | Pakai, cache 60 s | baca-saja |
| `TEMP_DOWNLINE` / `SCUSER.UPLINER` | otorisasi: boleh melihat data bawahan | 32–45 ms | Pakai | layar lama mempercayai field tersembunyi di browser; baru memeriksa di server |

## Penulisan: Call ("Place A Call")

Diukur pada salinan lokal DB DATA (Docker), bukan server dev bersama; aplikasi tidak menulis ke DB bersama (penjaga baca-saja menolak).

| Objek | Dipakai oleh | Ukur | Keputusan | Catatan |
|---|---|---|---|---|
| `SAVE_CALL_HISTORY` (40 parameter) | simpan call | ±42 ms rata-rata (maks 74 ms) di salinan lokal | Pakai, **di dalam transaksi** (`SET XACT_ABORT ON; BEGIN TRAN; exec ...; COMMIT`) | SP lama tidak bertransaksi dan menulis ke 6 tabel: `CALL_HISTORY` (+ foto kondisi rekening dari `VW_SEARCH_DEBITUR`), `PAYMENT_PLAN_HISTORY`, bila ada rekomendasi `LOAN_MASTER` (rekening pindah ke atasan), `NEXT_ACTION_HISTORY`, `MEMO`, `LOAN_HANDLING_HISTORY`; juga `CUST_CONTACT`/`CUSTOMER`/`SURAT_HISTORY` bila 23 kolom "Hasil Call" diisi. Layar baru mengisi 23 kolom itu lewat `ContactUpdate` (bersama Visit): deskripsi pertama "Tetap"/kosong = NULL, teks baru hanya dikirim bila deskripsinya memintanya. Perilaku diverifikasi: owner rekening pindah ke `UPLINER`, NAH/MEMO/LHH +1. |
| `SP_PTP_CHANGELIST` (@FUNC, @RELATIONSHIP) | daftar hasil call | cepat, di-cache 10 menit per hubungan | Pakai (baca-saja) | "Info Restrukturisasi" hanya untuk hubungan 00000 (debitur sendiri) |
| `SP_GET_REASON`, `SP_GET_ACTION`, `GET_RELATIONSHIP_TYPE`, `GET_PAYMENT_TYPE`, `RFPURPOSE` | pilihan dropdown | cepat, di-cache 10 menit bersama | Pakai (baca-saja) | |
| `SP_GET_CONTACT` (@ACC_NO, `COMB`/`NAMA`) | no. yang dihubungi, orang yang akan setor | per rekening, tidak di-cache | Pakai (baca-saja) | |
| `SP_TREATMENTRESULT_MAXDAY` | batas hari tanggal janji | cepat | Pakai (baca-saja) | |
| `SP_GET_TREATMENT_MENU` (@USERID) | tombol tindakan (Call/Letter/Visitation) sesuai hak user | cepat, di-cache 10 menit per user | Pakai (baca-saja) | grup Admin KCU hanya punya Letter: tombol Place A Call tidak tampil |
| `SAVE_VISIT_HISTORY` (37 parameter) | simpan kunjungan | ±beberapa puluh ms di salinan lokal | Pakai, **di dalam transaksi** (`SET XACT_ABORT ON; BEGIN TRAN; exec ...; COMMIT`) | Menulis `VISIT_HISTORY` (+ perubahan kontak/jaminan dari 23 kolom Hasil) dan, bila ada rekomendasi, memindahkan rekening ke atasan seperti Call. SP lama tanpa transaksi dan tanpa otorisasi; di sini hanya pemilik rekening/atasannya (`TEMP_DOWNLINE`). Catatan, alamat dan identitas di-HTML-encode demi paritas dengan layar lama (`&#39;` untuk `'`); riwayat didekode saat tampil. |
| `SP_GET_FIELD_OFFICER` (@USER), `SP_GET_RESULT` (`Visitation`/`Letter`), `SP_GET_ACTION` (`Visitation`), `SP_GET_CITY` | dropdown Visit dan bagian Hasil | cepat; di-cache 10 menit (petugas per user, lainnya bersama; surat/kota 30 menit) | Pakai (baca-saja, ditambahkan ke allow-list) | `SP_GET_CITY` 328 baris |
| `VISIT_HISTORY` (TOP 30 per rekening) | riwayat kunjungan | clustered index pada ACC_NO (sudah ada, tanpa indeks baru) | Pakai | selalu berbatas; diperiksa di salinan lokal |
| `SP_WORKLIST_MONITORING_TREE` (@USERID) | Distribusi Debitur: tim bawahan | DB dev ukuran produksi: 0,77 s (111 bawahan) | **Ditulis ulang** tanpa subquery berkorelasi dan tanpa HTML: 0,03 s, angka identik; di-cache 60 s | |
| `SP_WORKLIST_MONITORING_DEBITUR` (@USERID) → `VW_SEARCH_DISTRIBUSI_DEBITUR` | Distribusi Debitur: daftar debitur | user teratas 19.344 baris: 1,3 s, semua baris berisi tautan HTML | **Ditulis ulang**: TOP 2.000 terbaru (0,7 s) + `COUNT` total (70 ms), tanpa HTML, cache 60 s, maks 6 bersamaan | pesan "menampilkan N dari M" bila terpotong |
| `SP_REASIGN` (6 parameter) | pindah debitur | beberapa puluh ms untuk beberapa rekening | Pakai, **dalam transaksi + otorisasi server** | SP lama tanpa otorisasi (layar lama hanya memeriksa user tujuan, daftar rekening berasal dari browser) dan `@ACCOUNT` `varchar(4000)` memotong diam-diam daftar >±330 rekening. Di sini tujuan, user terpilih, dan tiap rekening harus diri sendiri/bawahan; daftar dipecah 250 rekening per panggilan dalam satu transaksi; menulis `LOAN_HANDLING_HISTORY`, `LOAN_MASTER`, dan menghitung ulang target pemindah dan penerima. Memindahkan user terpilih = seluruh debitur beserta bawahannya (perilaku lama). |
| `SP_SAVE_SURAT_MANUAL` (4 parameter) | simpan permintaan surat (Letter) | beberapa ms di salinan lokal | Pakai, **dalam transaksi + otorisasi server** | SP lama tanpa transaksi dan tanpa otorisasi. Menulis `SURAT_MANUAL` (jenis yang belum punya nomor), memindahkan rekening kembali ke pemilik semula bila rekomendasinya Surat, dan mengubah `TREATMENT` ke 00005. Bila jenis yang sama masih menunggu nomor, SP hanya menulis `MEMO` (`REC_LETTER`); layar baru memberi tahu user. Jenis surat divalidasi terhadap `SP_SURAT_TYPE` rekening itu (layar lama mempercayai dropdown dari browser). |
| `SP_SURAT_TYPE` (@ACC_NO) | dropdown jenis surat | cepat (join produk) | Pakai (baca-saja, allow-list) | tidak di-cache: bergantung pada produk rekening |
| `SURAT_MANUAL` (TOP 30 per rekening) | riwayat permintaan surat | indeks `(ACC_NO, SUMAN_TYPE)` sudah ada | Pakai | ±7,6 ribu baris di salinan lokal |
| `CALL_HISTORY` (TOP 30 per rekening) | riwayat call | berindeks ACC_NO | Pakai | tabel ±8 juta baris di produksi; selalu berbatas |
| kepemilikan rekening (`LOAN_MASTER` + `TEMP_DOWNLINE`) | otorisasi simpan | beberapa ms | Pakai | layar lama tidak memeriksa |

## Belum diukur / menunggu halaman

`SP_ALL_HISTORY`, `SP_ALL_HISTORY_RECOVERY`, `SP_GET_RESULT`, `SP_GET_CONTACT`, `SP_GET_TREATMENT_MENU`, `SP_WORKLIST_*`, `SP_PTP_CHANGELIST`, `SP_VW_REPORT_*`, `SP_SAVE_*`, `VW_*` lain: ukur saat halaman terkait dikerjakan dan tambahkan baris di atas **sebelum** dipakai.
