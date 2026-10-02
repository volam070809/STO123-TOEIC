-- Stop the old backend before applying either reviewed SQL script. Apply both
-- scripts, regenerate/update Database-First EF, then deploy the new pause/resume
-- aware backend before accepting new exam traffic. The old backend must not
-- create attempts between schema migration and application rollout.
-- Do not run this script through EF migrations.
SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.KetQuaLamBai', N'U') IS NULL
        THROW 51000, 'dbo.KetQuaLamBai is missing.', 1;
    IF COL_LENGTH(N'dbo.KetQuaLamBai', N'HetHanLuc') IS NULL
       OR COL_LENGTH(N'dbo.KetQuaLamBai', N'TrangThai') IS NULL
       OR COL_LENGTH(N'dbo.KetQuaLamBai', N'LoaiBaiLam') IS NULL
        THROW 51001, 'Expected existing attempt columns are missing.', 1;
    IF (CASE WHEN COL_LENGTH(N'dbo.KetQuaLamBai', N'ThoiGianConLaiGiay') IS NULL THEN 0 ELSE 1 END)
       <> (CASE WHEN COL_LENGTH(N'dbo.KetQuaLamBai', N'BatDauPhienLuc') IS NULL THEN 0 ELSE 1 END)
        THROW 51002, 'Only one timer column exists; inspect before proceeding.', 1;
    DECLARE @IntroducingTimer bit = CASE
        WHEN COL_LENGTH(N'dbo.KetQuaLamBai', N'ThoiGianConLaiGiay') IS NULL THEN 1 ELSE 0 END;
    DECLARE @ReferenceUtc datetime2(3) = SYSUTCDATETIME();
    IF @IntroducingTimer = 1 AND EXISTS (SELECT 1 FROM dbo.KetQuaLamBai
               WHERE LoaiBaiLam IN ('PLACEMENT', 'MOCK')
                 AND TrangThai IN ('DANG_LAM', 'BO_DO') AND HetHanLuc IS NULL)
        THROW 51003, 'Legacy unfinished timed attempts with NULL HetHanLuc need manual review.', 1;
    IF @IntroducingTimer = 1 AND EXISTS (SELECT 1 FROM dbo.KetQuaLamBai
               WHERE LoaiBaiLam IN ('PLACEMENT', 'MOCK')
                 AND TrangThai IN ('DANG_LAM', 'BO_DO')
                 AND HetHanLuc > @ReferenceUtc
                 AND DATEDIFF_BIG(millisecond, @ReferenceUtc, HetHanLuc) / 1000 > 2147483647)
        THROW 51006, 'Legacy remaining time exceeds int range; review before migration.', 1;

    IF @IntroducingTimer = 1
    BEGIN
        ALTER TABLE dbo.KetQuaLamBai ADD
            ThoiGianConLaiGiay int NULL,
            BatDauPhienLuc datetime2(3) NULL;
    END;

    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.KetQuaLamBai')
                 AND name = N'ThoiGianConLaiGiay' AND system_type_id <> 56)
       OR EXISTS (SELECT 1 FROM sys.columns
                  WHERE object_id = OBJECT_ID(N'dbo.KetQuaLamBai')
                    AND name = N'BatDauPhienLuc' AND (system_type_id <> 42 OR scale <> 3))
        THROW 51004, 'Existing timer column types differ from this proposal.', 1;

    -- One reference instant for all rows. Old deadlines are interpreted as UTC,
    -- matching the current DateTime.UtcNow application behavior. Floor to whole
    -- seconds so migration cannot grant additional exam time.
    IF @IntroducingTimer = 1
        EXEC sys.sp_executesql N'UPDATE a
       SET ThoiGianConLaiGiay = CASE
             WHEN a.HetHanLuc <= @ReferenceUtc THEN 0
             ELSE CONVERT(int, DATEDIFF_BIG(millisecond, @ReferenceUtc, a.HetHanLuc) / 1000) END,
           BatDauPhienLuc = NULL
    FROM dbo.KetQuaLamBai AS a
    WHERE a.LoaiBaiLam IN (''PLACEMENT'', ''MOCK'')
      AND a.TrangThai IN (''DANG_LAM'', ''BO_DO'')
      AND a.ThoiGianConLaiGiay IS NULL AND a.BatDauPhienLuc IS NULL;',
      N'@ReferenceUtc datetime2(3)', @ReferenceUtc = @ReferenceUtc;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE parent_object_id = OBJECT_ID(N'dbo.KetQuaLamBai')
                     AND name = N'CK_KetQuaLamBai_ThoiGianConLaiGiay')
        EXEC(N'ALTER TABLE dbo.KetQuaLamBai WITH CHECK ADD CONSTRAINT CK_KetQuaLamBai_ThoiGianConLaiGiay
            CHECK (ThoiGianConLaiGiay IS NULL OR ThoiGianConLaiGiay >= 0)');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE parent_object_id = OBJECT_ID(N'dbo.KetQuaLamBai')
                     AND name = N'CK_KetQuaLamBai_BatDauPhienLuc_CoThoiGian')
        EXEC(N'ALTER TABLE dbo.KetQuaLamBai WITH CHECK ADD CONSTRAINT CK_KetQuaLamBai_BatDauPhienLuc_CoThoiGian
            CHECK (BatDauPhienLuc IS NULL OR ThoiGianConLaiGiay IS NOT NULL)');

    DECLARE @InvalidTimerRows int;
    IF @IntroducingTimer = 1
        EXEC sys.sp_executesql N'SELECT @Count = COUNT(*) FROM dbo.KetQuaLamBai
        WHERE LoaiBaiLam IN (''PLACEMENT'', ''MOCK'')
          AND TrangThai IN (''DANG_LAM'', ''BO_DO'')
          AND (ThoiGianConLaiGiay IS NULL OR BatDauPhienLuc IS NOT NULL)',
        N'@Count int OUTPUT', @Count = @InvalidTimerRows OUTPUT;
    IF @IntroducingTimer = 1 AND @InvalidTimerRows > 0
        THROW 51005, 'An unfinished timed attempt was not safely paused.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Validation: the second result set lists invariant violations only. On a
-- rerun, legitimate running attempts may have a non-NULL BatDauPhienLuc.
SELECT TrangThai, LoaiBaiLam, COUNT(*) AS AttemptCount,
       MIN(ThoiGianConLaiGiay) AS MinRemainingSeconds,
       MAX(ThoiGianConLaiGiay) AS MaxRemainingSeconds
FROM dbo.KetQuaLamBai
WHERE LoaiBaiLam IN ('PLACEMENT', 'MOCK')
GROUP BY TrangThai, LoaiBaiLam;
SELECT MaKetQua, TrangThai, HetHanLuc, ThoiGianConLaiGiay, BatDauPhienLuc
FROM dbo.KetQuaLamBai
WHERE LoaiBaiLam IN ('PLACEMENT', 'MOCK')
  AND (ThoiGianConLaiGiay < 0 OR
       (BatDauPhienLuc IS NOT NULL AND ThoiGianConLaiGiay IS NULL));
GO

-- Future application contract: ThoiGianConLaiGiay is the remaining time at
-- BatDauPhienLuc; NULL BatDauPhienLuc means paused. While running, remaining
-- is max(0, stored seconds - elapsed UTC seconds). Serialize transitions by
-- locking the attempt row. Zero-second attempts need timeout finalization,
-- never a fresh duration. HetHanLuc remains a legacy compatibility column,
-- not a second authority; replace all deadline reads before enabling resume.
-- A derived display deadline may be emitted from the authoritative state.
-- Heartbeat every 15 seconds, with a lease ending at the last successful
-- heartbeat plus 15 seconds. Pause explicitly on navigation. A missed lease
-- freezes at its boundary, giving at most about 15 seconds of excess charge
-- after an unexpected close, plus scheduling/network delay. Browser close
-- cannot be detected reliably. Implement the lease in application logic;
-- no additional persisted column is needed if the running timestamp is
-- advanced and remaining time deducted on each successful heartbeat.
-- Rollback: before accepting new timer writes, restore the database backup or
-- manually drop both constraints and both columns after preserving their data.
