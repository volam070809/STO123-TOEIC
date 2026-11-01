SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID('dbo.ThongKeLuyenPart', 'U') IS NULL
        CREATE TABLE dbo.ThongKeLuyenPart(
            MaHocVien int not null foreign key references dbo.NguoiDung(MaNguoiDung),
            MaPart int not null foreign key references dbo.PartTOEIC(MaPart),
            SoLanLuyen int not null default 0 check (SoLanLuyen >= 0),
            TongCauDaLam int not null default 0 check (TongCauDaLam >= 0),
            TongCauDung int not null default 0 check (TongCauDung >= 0),
            SoCauTrongLanCuoi int not null default 0 check (SoCauTrongLanCuoi >= 0),
            SoCauLanCuoi int not null default 0 check (SoCauLanCuoi >= 0),
            SoDungLanCuoi int not null default 0 check (SoDungLanCuoi >= 0),
            MaKetQuaCuoi int null,
            LanLuyenGanNhat datetime2(3) null,
            constraint PK_ThongKeLuyenPart primary key (MaHocVien, MaPart),
            constraint CK_ThongKeLuyenPart_ThongNhat check (
                TongCauDung <= TongCauDaLam and SoDungLanCuoi <= SoCauLanCuoi and
                (SoLanLuyen = 0 and MaKetQuaCuoi is null and LanLuyenGanNhat is null)
                or (SoLanLuyen > 0 and MaKetQuaCuoi is not null and LanLuyenGanNhat is not null)
            )
        );

    SELECT MaKetQua, MaHocVien, NgayNopBai INTO #OldPractice
    FROM dbo.KetQuaLamBai WITH (UPDLOCK, HOLDLOCK)
    WHERE LoaiBaiLam = 'PRACTICE' AND TrangThai = 'DA_NOP';
    IF EXISTS (SELECT 1 FROM dbo.ThongKeLuyenPart) AND EXISTS (SELECT 1 FROM #OldPractice)
        THROW 51000, 'Existing statistics require a separate reconciliation before migration.', 1;
    IF EXISTS (SELECT 1 FROM dbo.TienDoBuocLoTrinh p JOIN #OldPractice o ON o.MaKetQua = p.MaKetQua)
        OR EXISTS (SELECT 1 FROM dbo.KetQuaPhanLopKNN k JOIN #OldPractice o ON o.MaKetQua = k.MaKetQua)
        THROW 51001, 'Completed practice has protected references.', 1;

    IF EXISTS (SELECT 1 FROM #OldPractice)
    BEGIN
    SELECT o.MaKetQua, o.MaHocVien, o.NgayNopBai, MIN(q.MaPart) AS MaPart,
        COUNT(*) AS SoCauTrongLan, COUNT(DISTINCT q.MaPart) AS SoPart,
        SUM(CASE WHEN d.DapAnChon IS NOT NULL THEN 1 ELSE 0 END) AS SoCauDaLam,
        SUM(CASE WHEN d.DapAnChon = q.PhuongAnDung THEN 1 ELSE 0 END) AS SoCauDung
    INTO #AttemptAgg
    FROM #OldPractice o
    JOIN dbo.CauHoiLuotLam q ON q.MaKetQua = o.MaKetQua
    LEFT JOIN dbo.ChiTietKetQua d ON d.MaCauHoiLuotLam = q.MaCauHoiLuotLam
    GROUP BY o.MaKetQua, o.MaHocVien, o.NgayNopBai;
    IF (SELECT COUNT(*) FROM #AttemptAgg) <> (SELECT COUNT(*) FROM #OldPractice)
        OR EXISTS (SELECT 1 FROM #AttemptAgg WHERE SoPart <> 1 OR MaPart NOT BETWEEN 1 AND 7 OR NgayNopBai IS NULL)
        THROW 51002, 'Practice source rows are incomplete or contain multiple Parts.', 1;

    ;WITH Ranked AS (
        SELECT *, ROW_NUMBER() OVER (PARTITION BY MaHocVien, MaPart ORDER BY NgayNopBai DESC, MaKetQua DESC) AS rn
        FROM #AttemptAgg
    )
    INSERT dbo.ThongKeLuyenPart(MaHocVien, MaPart, SoLanLuyen, TongCauDaLam, TongCauDung,
        SoCauTrongLanCuoi, SoCauLanCuoi, SoDungLanCuoi, MaKetQuaCuoi, LanLuyenGanNhat)
    SELECT MaHocVien, MaPart, COUNT(*), SUM(SoCauDaLam), SUM(SoCauDung),
        MAX(CASE WHEN rn = 1 THEN SoCauTrongLan END), MAX(CASE WHEN rn = 1 THEN SoCauDaLam END),
        MAX(CASE WHEN rn = 1 THEN SoCauDung END), MAX(CASE WHEN rn = 1 THEN MaKetQua END),
        MAX(CASE WHEN rn = 1 THEN NgayNopBai END)
    FROM Ranked GROUP BY MaHocVien, MaPart;

    IF (SELECT COALESCE(SUM(SoLanLuyen), 0) FROM dbo.ThongKeLuyenPart) <> (SELECT COUNT(*) FROM #OldPractice)
        OR (SELECT COALESCE(SUM(TongCauDaLam), 0) FROM dbo.ThongKeLuyenPart) <> (SELECT COALESCE(SUM(SoCauDaLam), 0) FROM #AttemptAgg)
        OR (SELECT COALESCE(SUM(TongCauDung), 0) FROM dbo.ThongKeLuyenPart) <> (SELECT COALESCE(SUM(SoCauDung), 0) FROM #AttemptAgg)
        THROW 51003, 'Migrated practice totals do not match source.', 1;

    DELETE d FROM dbo.ChiTietKetQua d
    JOIN dbo.CauHoiLuotLam q ON q.MaCauHoiLuotLam = d.MaCauHoiLuotLam
    JOIN #OldPractice o ON o.MaKetQua = q.MaKetQua;
    DELETE q FROM dbo.CauHoiLuotLam q JOIN #OldPractice o ON o.MaKetQua = q.MaKetQua;
    DELETE n FROM dbo.NhomLuotLam n JOIN #OldPractice o ON o.MaKetQua = n.MaKetQua;
    DELETE k FROM dbo.KetQuaLamBai k JOIN #OldPractice o ON o.MaKetQua = k.MaKetQua;
    IF EXISTS (SELECT 1 FROM dbo.KetQuaLamBai WHERE LoaiBaiLam = 'PRACTICE' AND TrangThai = 'DA_NOP')
        THROW 51004, 'Completed practice rows remain after migration.', 1;
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
SELECT COUNT(*) AS StatisticRows, COALESCE(SUM(SoLanLuyen), 0) AS CompletedSessions,
    COALESCE(SUM(TongCauDaLam), 0) AS AnsweredQuestions, COALESCE(SUM(TongCauDung), 0) AS CorrectAnswers
FROM dbo.ThongKeLuyenPart;
SELECT TrangThai, COUNT(*) AS PracticeAttempts FROM dbo.KetQuaLamBai
WHERE LoaiBaiLam = 'PRACTICE' GROUP BY TrangThai;
