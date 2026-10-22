-- Indeks yang direkomendasikan untuk DB LOGIN (lihat docs/LOAD-TESTING.md).
-- SP_LOGUSER mencari baris log terakhir per USERID + IP_ADDR; tanpa indeks ini ia memindai seluruh tabel.
-- Sudah diterapkan di salinan lokal (Docker). Di DB lama: BELUM, butuh persetujuan.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SCALLUSERLOG_USER_IP_LOGIN' AND object_id = OBJECT_ID('dbo.SCALLUSERLOG'))
    CREATE INDEX IX_SCALLUSERLOG_USER_IP_LOGIN ON dbo.SCALLUSERLOG (USERID, IP_ADDR, LOGIN_TIME DESC);
