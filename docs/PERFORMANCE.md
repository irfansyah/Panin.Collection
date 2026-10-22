# Performa: aplikasi baru harus lebih cepat dari yang lama

Aplikasi lama tidak kuat di atas ±300–400 user dan banyak halamannya lambat. Aplikasi baru harus **lebih cepat** dan melayani **> 700 user bersamaan**. Dokumen ini mencatat temuan nyata agar kesalahan yang sama tidak terulang, aturan yang wajib diikuti saat menulis akses data, dan cara melihat apakah aturan itu dipatuhi.

Daftar query/SP/view yang sudah diukur ada di [`QUERY-REGISTRY.md`](QUERY-REGISTRY.md). Hasil uji beban ada di [`LOAD-TESTING.md`](LOAD-TESTING.md).

## 1. Anggaran kecepatan (usulan, perlu disepakati)

| Jenis | Target | Batas keras |
|---|---|---|
| Aksi interaktif biasa (klik menu, buka halaman, filter) | p95 < 300 ms sisi server | 1 s |
| Pencarian (debitur, rekening) | p95 < 1 s untuk kriteria spesifik | 5 s, dengan batas baris |
| Login (700 sekaligus dalam ±5 menit) | p95 < 1 s | 3 s |
| Pekerjaan berat (laporan, ekspor besar) | di luar sirkuit (latar), dengan indikator progres | – |
| Query tunggal ke DB | rata-rata < 100 ms, tidak ada yang > 2 s di jalur interaktif | log peringatan > 1 s |

Pembanding: untuk setiap halaman yang dimigrasi, **ukur halaman lama yang setara** (waktu SP/query yang sama pada data yang sama) dan catat di registry. Halaman baru dianggap selesai bila tidak lebih lambat, dan idealnya jauh lebih cepat. Contoh pertama: Inquiry Status Debitur, lama 14–116 detik, baru 0,1–5 detik.

## 2. Temuan nyata dari sistem lama (jangan diulang)

| # | Temuan | Bukti terukur | Dampak | Penanganan di aplikasi baru |
|---|---|---|---|---|
| 1 | **Hasil query tidak dibatasi.** `SP_SEARCH_DEBITUR` mengembalikan semua baris yang cocok | nama pendek: 335 ribu baris, ±56 s; tanpa kriteria: 760 ribu baris, 116 s | memori server dan waktu tunggu user; tidak mungkin untuk 700 user | selalu `TOP`/paging; batas baris + pesan "hasil dibatasi"; validasi kriteria (minimal huruf) |
| 2 | **Join yang kolomnya tidak dipakai.** SP yang sama menggabung `TREATMENT_HISTORY` tanpa memilih satu kolompun dari tabel itu | ±14 s hingga baris pertama untuk pencarian nama, padahal tanpa join 0,1–5 s | lambat dan bisa menggandakan baris | tulis ulang sebagai query setara tanpa join itu; validasi hasil identik terhadap SP |
| 3 | **Pencarian `LIKE '%x%'` tanpa batas** pada ratusan ribu baris | lihat #1 | beban DB berulang untuk tiap user | kriteria minimal, `TOP`, batas 8 pencarian berat bersamaan |
| 4 | **Tabel log besar tanpa indeks yang dibutuhkan.** `SP_LOGUSER` memindai `SCALLUSERLOG` (±1 juta baris, hanya PK) pada setiap logout | 140 ms CPU per logout; logout serentak menumpuk dan **deadlock (error 1205)**; 700 logout butuh ±60 s | logout/login lambat, pencatatan hilang | logout dicatat di latar (antrean 4 worker), operasi tulis diulang bila deadlock; indeks `IX_SCALLUSERLOG_USER_IP_LOGIN` mengurangi ke ±6 s (belum diterapkan di DB lama, butuh persetujuan; `docker/sql/recommended-indexes.sql`) |
| 5 | **SP mengembalikan HTML/JavaScript** (tautan `PopupPage(...)` di dalam kolom data) | `SP_SEARCH_DEBITUR`, `SP_VW_WARNINGCONTENT`, `VW_SEARCH_DEBITUR` | data tidak bisa dipakai langsung, ukuran hasil membengkak | ambil kolom data mentah; bila terpaksa memakai SP, bersihkan HTML dan ekstrak nilai (`WarningService`) |
| 6 | **Nilai disisipkan ke teks SQL** (bukan parameter) oleh `DbConnection` lama | dibaca dari hasil decompile | rencana eksekusi tidak dipakai ulang, rawan injeksi | parameter SQL sungguhan lewat Dapper |
| 7 | **Timeout 600/3600 detik** menutupi query yang salah | `dbTimeOut=600`, `dbBigTimeOut=3600` di config lama | satu query macet menahan sesi dan koneksi lama | default 30 s untuk interaksi; panjang hanya untuk pekerjaan latar yang eksplisit |
| 8 | **State sesi besar di memori proses** (InProc) dan halaman memuat semua data sekaligus | sistem lama tidak kuat di atas ±300–400 user | batas kapasitas | state per sirkuit kecil, paging di server |
| 9 | **View rusak di sumber** (`VW_SCALLGROUP` merujuk kolom yang tidak ada) | gagal dibuat ulang | pekerjaan salah arah | diverifikasi ke sumber sebelum disalin; dicatat |
| 10 | **Data dev tertinggal** (`ACCOUNT_MASTER` terakhir 7 Mei 2025) dan token tidak dibersihkan (`ESAPPTOKEN` ±4.800 baris) | | peringatan "data lama" selalu muncul di dev | bukan masalah aplikasi; diketahui saat membaca hasil uji |
| 11 | **Cache stampede dan kerja ganda saat login massal** (ditemukan oleh pengamat query kita sendiri pada uji 200 sirkuit). `IMemoryCache` biasa tidak mencegah banyak pemanggil mengisi kunci kosong bersamaan, dan komponen shell memuat status di prarender **dan** di sirkuit | 175 query kesegaran data bersamaan: rata-rata **6,6 s** (sekali jalan 66 ms); 326 panggilan `SP_VW_WARNINGCONTENT`: rata-rata **8,3 s** (sekali jalan 25 ms); menu/modul diquery 13–15 kali padahal 3 group | lonjakan login 700 user akan melumpuhkan DB DATA lewat fitur sekunder (footer) | `SingleFlightCache` (satu query per kunci, sisanya menunggu), status footer dimuat hanya di sirkuit interaktif dan ditunda acak 1–8 s, `SP_VW_WARNINGCONTENT` dibatasi 6 bersamaan. Setelah perbaikan: 200 panggilan peringatan rata-rata **57 ms**, kesegaran data **1** query, menu/modul **3** query (turun ±99% total waktu DB) |
| 12 | **Library yang diam-diam butuh pustaka native.** `AutoSizeColumn` NPOI memakai SkiaSharp | gagal saat ekspor Excel di server | ekspor gagal di server tanpa font/SkiaSharp | lebar kolom dihitung sendiri; uji ekspor di unit test |

## 3. Aturan akses data

1. **Ukur sebelum dipakai.** Setiap query/SP/view baru yang dipanggil dari aplikasi dicatat di `QUERY-REGISTRY.md` dengan waktu (p50/terburuk), jumlah baris, dan keputusan: *pakai apa adanya*, *tulis ulang*, atau *tunda*. Ukur pada data berukuran produksi (DB dev lama punya 760 ribu rekening).
2. **Selalu berbatas.** Tidak ada query interaktif tanpa `TOP`/paging. Hasil maksimum masuk memori sirkuit (500 baris default). Untuk data lebih besar: paging di DB (`OFFSET/FETCH`, keyset) atau pekerjaan latar.
3. **Pilih kolom yang dipakai.** Jangan `SELECT *` dari view lebar; jangan memakai join yang kolomnya tidak dipilih.
4. **Prefiks lebih baik dari "mengandung".** `LIKE 'x%'` bisa memakai indeks; `LIKE '%x%'` memindai semuanya, jadi butuh panjang minimum dan `TOP`.
5. **Batasi pekerjaan berat bersamaan** (`SemaphoreSlim`) dan beri tahu user bila antre.
6. **Cache data bersama dengan `SingleFlightCache`** (referensi, menu, area, status) dan kedaluwarsa; jangan memakai `IMemoryCache.GetOrCreateAsync` polos untuk data yang diminta ratusan user sekaligus (stampede). Jangan menyimpan hasil per user dalam jumlah besar.
6a. **Jangan ke DB DATA di jalur login/prarender** untuk hal sekunder. Muat di sirkuit interaktif (`OnAfterRenderAsync`), tunda acak, dan jangan sampai lonjakan login menjadi lonjakan query. Ingat `OnInitializedAsync` berjalan dua kali (prarender dan sirkuit).
7. **Tidak ada N+1.** Satu query untuk daftar, satu untuk detail; gabungkan di DB atau di memori.
8. **Tulis: ulangi saat deadlock** (`DeadlockRetry`) dan jangan membuat user menunggu pencatatan log (antrean latar).
9. **Timeout eksplisit**, pendek untuk interaksi (default 30 s). Menaikkannya harus beralasan dan ditulis.
10. **SP yang mengembalikan HTML**: ambil datanya, bukan markup-nya.
11. **Perubahan DB lama** (indeks, view) hanya dengan persetujuan; catat di `docker/sql/` dan uji di salinan lokal dulu.

## 4. Cara melihat apakah aturan dipatuhi

- **Statistik query di server:** setiap panggilan `IDb` diukur. Dengan `Diagnostics:Enabled=true`:
  - `GET /_diag/queries?top=20` menampilkan query dengan **total waktu** terbesar (jumlah panggilan, rata-rata, maksimum, baris, jumlah lambat/gagal). Urutan total waktu lebih berguna daripada "paling lambat sekali" karena query murah yang sangat sering bisa lebih mahal.
  - `GET /_diag` memberi jumlah sirkuit, sesi, dan memori; `?gc=true` memaksa GC untuk pengukuran bersih.
- **Log peringatan** untuk query lebih lambat dari `Diagnostics:SlowQueryMs` (default 1000 ms) berisi DB, durasi, jumlah baris, dan teks SQL (tanpa nilai parameter).
- **Uji beban** (`tools/Panin.LoadTest`): login massal dan sirkuit idle; jalankan ulang setiap kali modul besar masuk dan bandingkan.
- **Ulasan kode:** setiap PR yang menambah query mencantumkan baris registry dan hasil ukur.

## 5. Daftar periksa PR yang menyentuh data

- [ ] Query/SP/view tercatat di `QUERY-REGISTRY.md` dengan hasil ukur pada data berukuran produksi.
- [ ] Hasil berbatas (`TOP`/paging) dan ada pesan bila terpotong.
- [ ] Hanya kolom yang dipakai; tidak ada join tak terpakai; tidak ada HTML dari SP.
- [ ] Pencarian "mengandung" punya panjang minimum dan masuk batas konkurensi.
- [ ] Parameter SQL (`@1..@n`), tidak ada nilai yang disambung ke teks SQL.
- [ ] Timeout wajar; tulis memakai `DeadlockRetry` bila di jalur login/logout atau tabel panas.
- [ ] Halaman lama yang setara sudah diukur; halaman baru tidak lebih lambat.
- [ ] `/_diag/queries` setelah mencoba halaman: tidak ada query yang tidak terduga banyak atau lambat.

## 6. Pertanyaan terbuka

- Anggaran di bagian 1 perlu disepakati (terutama pencarian dan login).
- Pengukuran aplikasi lama sebagai pembanding resmi (waktu halaman nyata dari browser, bukan hanya SP) belum dilakukan; saat ini pembanding adalah waktu SP/query pada DB yang sama.
- Persetujuan untuk indeks DB lama (`SCALLUSERLOG`, dan kandidat lain yang ditemukan lewat registry).
