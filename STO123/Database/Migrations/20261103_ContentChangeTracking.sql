SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_databases WHERE database_id=DB_ID())
    EXEC(N'ALTER DATABASE CURRENT SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 2 DAYS, AUTO_CLEANUP = ON)');

DECLARE @tracked TABLE (TenBang sysname not null primary key);
INSERT INTO @tracked VALUES
    ('KhoaHoc'),('GoiHoc'),('GoiHocKhoaHoc'),('DangKyGoiHoc'),('ChiTietDangKyKhoaHoc'),
    ('UnitKhoaHoc'),('LessonKhoaHoc'),('BuocLoTrinh'),
    ('DanhMucBaiGiang'),('BaiGiang'),('TaiNguyenBaiGiang'),
    ('ChuDe'),('TuVung'),('TuVungChuDe'),
    ('CauHoi'),('NguLieu'),('NguLieuTaiLieu'),('NhomCauHoi'),
    ('BaiTapKhoaHoc'),('PhienBanBaiTapKhoaHoc'),('CauHoiBaiTapKhoaHoc'),
    ('DeThi'),('PhienBanDeThi'),('CauHoiDeThi');

IF EXISTS(SELECT 1 FROM @tracked t WHERE NOT EXISTS(
    SELECT 1 FROM sys.key_constraints k WHERE k.parent_object_id=OBJECT_ID(N'dbo.'+t.TenBang)
        AND k.type='PK'))
    THROW 51002, 'Tracked content table is missing a primary key.', 1;

DECLARE @table sysname, @sql nvarchar(max);
DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT TenBang FROM @tracked;
OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @table;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS(SELECT 1 FROM sys.change_tracking_tables WHERE object_id=OBJECT_ID(N'dbo.'+@table))
    BEGIN
        SET @sql=N'ALTER TABLE dbo.'+QUOTENAME(@table)+
            N' ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = OFF)';
        EXEC sys.sp_executesql @sql;
    END;
    FETCH NEXT FROM table_cursor INTO @table;
END;
CLOSE table_cursor;
DEALLOCATE table_cursor;
