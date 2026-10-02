-- Stop the old backend before applying either reviewed SQL script. Apply both
-- scripts, regenerate/update Database-First EF, then deploy the new pause/resume
-- aware backend before accepting new exam traffic. The old backend must not
-- create attempts between schema migration and application rollout.
-- Current KetQuaPhanLopKNN is updated in place for the latest recommendation.
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.KetQuaLamBai', N'U') IS NULL
       OR OBJECT_ID(N'dbo.KetQuaPhanLopKNN', N'U') IS NULL
        THROW 51100, 'Expected Placement tables are missing.', 1;
    IF COL_LENGTH(N'dbo.KetQuaLamBai', N'GiaiDoanLucNop') IS NULL
        ALTER TABLE dbo.KetQuaLamBai ADD GiaiDoanLucNop tinyint NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.KetQuaLamBai')
               AND name = N'GiaiDoanLucNop' AND system_type_id <> 48)
        THROW 51101, 'Existing snapshot column is not tinyint.', 1;
    IF COL_LENGTH(N'dbo.KetQuaPhanLopKNN', N'MaKetQua') IS NULL
       OR COL_LENGTH(N'dbo.KetQuaPhanLopKNN', N'GiaiDoanDeXuat') IS NULL
        THROW 51102, 'Expected KNN columns are missing.', 1;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE parent_object_id = OBJECT_ID(N'dbo.KetQuaLamBai')
                     AND name = N'CK_KetQuaLamBai_GiaiDoanLucNop')
        EXEC(N'ALTER TABLE dbo.KetQuaLamBai WITH CHECK ADD CONSTRAINT CK_KetQuaLamBai_GiaiDoanLucNop
            CHECK (GiaiDoanLucNop IS NULL OR
                   CASE WHEN LoaiBaiLam = ''PLACEMENT''
                          AND TrangThai IN (''DA_NOP'', ''HET_GIO'')
                          AND GiaiDoanLucNop BETWEEN 1 AND 3
                        THEN 1 ELSE 0 END = 1)');

    -- Backfill only a surviving exact KNN -> MaKetQua link. Require exactly
    -- one row for that attempt, avoiding ambiguous legacy data.
    -- Earlier attempts whose KNN row was repointed retain NULL.
    EXEC(N'UPDATE a SET GiaiDoanLucNop = k.GiaiDoanDeXuat
    FROM dbo.KetQuaLamBai AS a
    JOIN dbo.KetQuaPhanLopKNN AS k ON k.MaKetQua = a.MaKetQua
    WHERE a.LoaiBaiLam = ''PLACEMENT''
      AND a.TrangThai IN (''DA_NOP'', ''HET_GIO'')
      AND a.GiaiDoanLucNop IS NULL
      AND k.GiaiDoanDeXuat BETWEEN 1 AND 3
      AND (SELECT COUNT(*) FROM dbo.KetQuaPhanLopKNN AS k2
           WHERE k2.MaKetQua = a.MaKetQua) = 1');

    DECLARE @InvalidSnapshots int;
    EXEC sys.sp_executesql N'SELECT @Count = COUNT(*) FROM dbo.KetQuaLamBai
        WHERE GiaiDoanLucNop IS NOT NULL
          AND (LoaiBaiLam <> ''PLACEMENT'' OR TrangThai NOT IN (''DA_NOP'', ''HET_GIO'')
               OR GiaiDoanLucNop NOT BETWEEN 1 AND 3)',
        N'@Count int OUTPUT', @Count = @InvalidSnapshots OUTPUT;
    IF @InvalidSnapshots > 0
        THROW 51103, 'A snapshot is present on an ineligible attempt.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Validation: only finalized Placement attempts may have a snapshot.
SELECT LoaiBaiLam, TrangThai, GiaiDoanLucNop, COUNT(*) AS AttemptCount
FROM dbo.KetQuaLamBai
GROUP BY LoaiBaiLam, TrangThai, GiaiDoanLucNop;
SELECT MaKetQua, MaHocVien, LoaiBaiLam, TrangThai, GiaiDoanLucNop
FROM dbo.KetQuaLamBai
WHERE GiaiDoanLucNop IS NOT NULL
  AND (LoaiBaiLam <> 'PLACEMENT' OR TrangThai NOT IN ('DA_NOP', 'HET_GIO')
       OR GiaiDoanLucNop NOT BETWEEN 1 AND 3);
GO

-- Future application contract: after grading finalizes a Placement and KNN
-- succeeds, write GiaiDoanLucNop on that exact attempt in the same transaction
-- as the current KNN recommendation update. Do not write on KNN failure,
-- unfinished attempts, Mock, or Practice. Historical NULL means unknown;
-- do not rerun today's KNN model to fabricate a past recommendation.
-- Rollback: before accepting new snapshot writes, restore a backup or manually
-- drop the constraint and column after preserving any snapshot values.
