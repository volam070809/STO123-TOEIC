-- Run separately against existing databases after reviewing index inventory.
-- No application startup applies this script automatically.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.GiaoDichThanhToan')
      AND name = N'IX_GiaoDichThanhToan_TrangThaiNgayTao')
    CREATE INDEX IX_GiaoDichThanhToan_TrangThaiNgayTao
        ON dbo.GiaoDichThanhToan(TrangThai, NgayTao) INCLUDE (MaDangKy);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.KetQuaLamBai')
      AND name = N'IX_KetQuaLamBai_HocVienLoaiNgay')
    CREATE INDEX IX_KetQuaLamBai_HocVienLoaiNgay
        ON dbo.KetQuaLamBai(MaHocVien, LoaiBaiLam, NgayLamBai DESC, MaKetQua DESC);
GO
