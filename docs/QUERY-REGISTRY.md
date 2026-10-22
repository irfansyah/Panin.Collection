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

## Belum diukur / menunggu halaman

`SP_ALL_HISTORY`, `SP_ALL_HISTORY_RECOVERY`, `SP_GET_RESULT`, `SP_GET_CONTACT`, `SP_GET_TREATMENT_MENU`, `SP_WORKLIST_*`, `SP_PTP_CHANGELIST`, `SP_VW_REPORT_*`, `SP_SAVE_*`, `VW_*` lain: ukur saat halaman terkait dikerjakan dan tambahkan baris di atas **sebelum** dipakai.
