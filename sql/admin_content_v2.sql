SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.DeThi', 'MaPhienBanHienHanh') IS NULL
    ALTER TABLE dbo.DeThi ADD MaPhienBanHienHanh int NULL;
GO

IF OBJECT_ID('dbo.PhienBanDeThi', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PhienBanDeThi(
        MaPhienBanDeThi int identity(1,1) NOT NULL PRIMARY KEY,
        MaDeThi int NOT NULL,
        SoPhienBan int NOT NULL,
        BlobContainer varchar(63) NOT NULL,
        BlobPath varchar(512) NOT NULL,
        ContentHash char(64) NOT NULL,
        SchemaVersion int NOT NULL,
        SoCau int NOT NULL,
        ThoiGianLamBai int NOT NULL,
        TenDe nvarchar(128) NOT NULL,
        NgayXuatBan datetime2(3) NOT NULL CONSTRAINT DF_PhienBanDeThi_NgayXuatBan DEFAULT sysutcdatetime(),
        MaNguoiXuatBan int NULL,
        KhoaYeuCau uniqueidentifier NOT NULL,
        CONSTRAINT CK_PhienBanDeThi_SoPhienBan CHECK (SoPhienBan > 0),
        CONSTRAINT CK_PhienBanDeThi_SchemaVersion CHECK (SchemaVersion > 0),
        CONSTRAINT CK_PhienBanDeThi_SoCau CHECK (SoCau = 200),
        CONSTRAINT CK_PhienBanDeThi_ThoiGian CHECK (ThoiGianLamBai = 120),
        CONSTRAINT FK_PhienBanDeThi_DeThi FOREIGN KEY (MaDeThi) REFERENCES dbo.DeThi(MaDeThi),
        CONSTRAINT FK_PhienBanDeThi_NguoiXuatBan FOREIGN KEY (MaNguoiXuatBan) REFERENCES dbo.NguoiDung(MaNguoiDung),
        CONSTRAINT UQ_PhienBanDeThi_SoPhienBan UNIQUE (MaDeThi, SoPhienBan),
        CONSTRAINT UQ_PhienBanDeThi_DeVaId UNIQUE (MaDeThi, MaPhienBanDeThi),
        CONSTRAINT UQ_PhienBanDeThi_Blob UNIQUE (BlobContainer, BlobPath),
        CONSTRAINT UQ_PhienBanDeThi_YeuCau UNIQUE (MaDeThi, KhoaYeuCau)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_DeThi_PhienBanHienHanh')
    ALTER TABLE dbo.DeThi ADD CONSTRAINT FK_DeThi_PhienBanHienHanh
        FOREIGN KEY (MaDeThi, MaPhienBanHienHanh)
        REFERENCES dbo.PhienBanDeThi(MaDeThi, MaPhienBanDeThi);

IF COL_LENGTH('dbo.KetQuaLamBai', 'MaPhienBanDeThi') IS NULL
    ALTER TABLE dbo.KetQuaLamBai ADD MaPhienBanDeThi int NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_KetQuaLamBai_PhienBanDeThi')
    ALTER TABLE dbo.KetQuaLamBai ADD CONSTRAINT FK_KetQuaLamBai_PhienBanDeThi
        FOREIGN KEY (MaDeThi, MaPhienBanDeThi)
        REFERENCES dbo.PhienBanDeThi(MaDeThi, MaPhienBanDeThi);

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_KetQuaLamBai_PhienBanCoDe')
    ALTER TABLE dbo.KetQuaLamBai ADD CONSTRAINT CK_KetQuaLamBai_PhienBanCoDe
        CHECK (MaPhienBanDeThi IS NULL OR MaDeThi IS NOT NULL);

COMMIT TRANSACTION;
