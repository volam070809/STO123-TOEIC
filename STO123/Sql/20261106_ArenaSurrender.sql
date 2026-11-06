-- Compatible update for existing ArenaMatch rows. Run once before deploying the new backend.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.ArenaMatch', N'ForfeitById') IS NULL
BEGIN
    ALTER TABLE dbo.ArenaMatch ADD ForfeitById int NULL;
    EXEC(N'ALTER TABLE dbo.ArenaMatch ADD CONSTRAINT FK_ArenaMatch_Forfeit
        FOREIGN KEY (ForfeitById) REFERENCES dbo.NguoiDung(MaNguoiDung)');
    EXEC(N'ALTER TABLE dbo.ArenaMatch ADD CONSTRAINT CK_ArenaMatch_Forfeit
        CHECK (ForfeitById IS NULL OR ForfeitById = OwnerId OR ForfeitById = OpponentId)');
END;
IF EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ArenaMatch') AND name = N'CK_ArenaMatch_Status')
    ALTER TABLE dbo.ArenaMatch DROP CONSTRAINT CK_ArenaMatch_Status;
ALTER TABLE dbo.ArenaMatch ADD CONSTRAINT CK_ArenaMatch_Status CHECK
    (Status IN ('WAITING','READY','STARTING','ACTIVE','REVEAL','COMPLETED','SURRENDERED','ABANDONED'));
COMMIT TRANSACTION;
