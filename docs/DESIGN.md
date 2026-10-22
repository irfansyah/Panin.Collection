# Sistem desain Panin Collection

Dokumen ini menjelaskan tampilan baru dan cara memakainya. Tujuan: modern, responsif (desktop, tablet, ponsel), tetap cepat. Tanpa Bootstrap dan tanpa pustaka UI pihak ketiga; semuanya Tailwind v4 (`src/Panin.Web/Styles/tailwind.css`).

## Prinsip

- **Satu sumber token.** Warna, font, bayangan didefinisikan di blok `@theme` pada `tailwind.css`. Palet `sky-*` dipetakan ke biru merek, jadi halaman lama yang memakai `sky-*` ikut berganti tanpa diubah satu per satu. Mengganti warna merek = mengubah 11 baris itu.
- **Font sistem.** Tumpukan `Inter, Segoe UI Variable, Segoe UI, system-ui`. Tidak ada unduhan font dari internet (aplikasi berjalan di intranet dan harus cepat).
- **Sudut membulat dan bayangan tipis.** Kartu `rounded-xl` + `shadow-card`; kolom isian dan tombol `rounded-lg`.
- **Fokus terlihat** (`:focus-visible`) untuk pemakai keyboard; gerak dimatikan bila sistem meminta `prefers-reduced-motion`.
- **Tanpa JavaScript tambahan.** Drawer menu, tab, dan tabel memakai state Blazor dan CSS.

## Yang berubah

| Area | Sebelum | Sesudah |
|---|---|---|
| Shell (`Components/Pages/Home.razor`) | Sidebar tetap 256 px di semua ukuran layar; di ponsel isi halaman terjepit | Di bawah 1024 px sidebar menjadi **drawer** dengan tombol menu dan tirai gelap; tab modul bisa digulir mendatar; tinggi memakai `dvh` agar tidak terpotong bilah alamat ponsel |
| Header | Teks "Logout" | Logo, nama pengguna dan grup (sembunyi di ponsel), tombol logout |
| Footer | Satu baris yang patah di ponsel | Membungkus rapi; info pengguna disembunyikan di ponsel karena sudah ada di header |
| Menu (`MenuTree.razor`) | Tanda `+`/`−` | Ikon panah berputar, item aktif diberi latar dan cincin, ruang sentuh lebih besar |
| Login (`AuthLayout.razor`, `Login.razor`) | Kartu putih di latar gelap | Dua panel di desktop (merek di kiri, formulir di kanan), satu kolom di ponsel |
| Kartu (`Card.razor`) | Garis tipis | Bayangan lembut, judul dengan pemisah halus |
| Tabel (`DataGrid.razor`) | Header ikut tergulir | **Header menempel** (sticky), baris selang-seling, sorot saat hover, tinggi maksimum 70% layar dengan gulir sendiri, kotak filter selebar penuh di ponsel |

## Cara memakai di halaman baru

- Pembungkus konten: `<Card Title="...">`. Tabel data: `<DataGrid>`; jangan membuat `<table>` baru.
- Tombol utama: `rounded-lg bg-sky-700 px-4 py-2 text-sm font-semibold text-white hover:bg-sky-800`. Tombol sekunder: `rounded-lg border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50`.
- Isian: `rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-sky-500 focus:ring-2 focus:ring-sky-200`.
- Pesan: galat `border-red-200 bg-red-50 text-red-700` (`role="alert"`), sukses `border-green-200 bg-green-50 text-green-700` (`role="status"`), peringatan `border-amber-300 bg-amber-50 text-amber-800`.
- Daerah gulir: tambahkan kelas `scroll-thin`.
- Jangan memakai lebar tetap; pakai `w-full` dan batas `sm:`/`lg:`. Titik patah: `sm` 640, `lg` 1024.

## Belum dikerjakan (lihat juga `docs/SISA-PEKERJAAN.md`, bagian C)

- Halaman fitur yang sudah ada (Distribusi, Monitoring, Report, Memo, dst.) baru memperoleh gaya baru lewat token, kartu, dan tabel bersama. Tiap halaman perlu **ditinjau di ponsel** satu per satu (formulir filter dengan banyak kolom, pivot lebar); pivot dan tabel lebar tetap digulir mendatar.
- Keadaan memuat (skeleton), keadaan kosong bergambar, dan notifikasi toast belum ada; sekarang berupa teks.
- Mode gelap, logo/warna resmi, dan umpan balik 3–5 pengguna perlu keputusan dari pemilik produk.
- Uji aksesibilitas (pembaca layar, kontras) belum dilakukan secara formal.
