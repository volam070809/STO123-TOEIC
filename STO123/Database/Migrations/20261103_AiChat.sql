SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.AiHoiThoai', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AiHoiThoai(
        MaHoiThoai bigint identity(1,1) not null constraint PK_AiHoiThoai primary key,
        MaHocVien int not null,
        TieuDe nvarchar(120) not null,
        TaoLuc datetime2(3) not null constraint DF_AiHoiThoai_TaoLuc default SYSUTCDATETIME(),
        CapNhatLuc datetime2(3) not null constraint DF_AiHoiThoai_CapNhatLuc default SYSUTCDATETIME(),
        constraint FK_AiHoiThoai_NguoiDung foreign key (MaHocVien) references dbo.NguoiDung(MaNguoiDung)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AiHoiThoai') AND name = N'IX_AiHoiThoai_HocVien_CapNhat')
    CREATE INDEX IX_AiHoiThoai_HocVien_CapNhat ON dbo.AiHoiThoai(MaHocVien, CapNhatLuc DESC, MaHoiThoai DESC);

IF OBJECT_ID(N'dbo.AiTinNhan', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AiTinNhan(
        MaTinNhan bigint identity(1,1) not null constraint PK_AiTinNhan primary key,
        MaHoiThoai bigint not null,
        ThuTu int not null,
        VaiTro varchar(8) not null,
        NoiDung nvarchar(max) not null,
        MaYeuCau uniqueidentifier null,
        TaoLuc datetime2(3) not null constraint DF_AiTinNhan_TaoLuc default SYSUTCDATETIME(),
        constraint FK_AiTinNhan_HoiThoai foreign key (MaHoiThoai) references dbo.AiHoiThoai(MaHoiThoai) on delete cascade,
        constraint CK_AiTinNhan_ThuTu check (ThuTu > 0),
        constraint CK_AiTinNhan_VaiTro check ((VaiTro = 'USER' and MaYeuCau is not null) or (VaiTro = 'MODEL' and MaYeuCau is null)),
        constraint CK_AiTinNhan_NoiDung check (len(ltrim(rtrim(NoiDung))) between 1 and 12000)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AiTinNhan') AND name = N'UX_AiTinNhan_HoiThoai_ThuTu')
    CREATE UNIQUE INDEX UX_AiTinNhan_HoiThoai_ThuTu ON dbo.AiTinNhan(MaHoiThoai, ThuTu);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.AiTinNhan') AND name = N'UX_AiTinNhan_HoiThoai_YeuCau')
    CREATE UNIQUE INDEX UX_AiTinNhan_HoiThoai_YeuCau ON dbo.AiTinNhan(MaHoiThoai, MaYeuCau) WHERE MaYeuCau IS NOT NULL;

COMMIT TRANSACTION;
