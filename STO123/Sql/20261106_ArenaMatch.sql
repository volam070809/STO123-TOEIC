-- Run once on an existing STO123 Azure SQL database. The root schema contains the same table and columns.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'dbo.ArenaMatch', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ArenaMatch(
        Id uniqueidentifier not null constraint PK_ArenaMatch primary key,
        Part int not null constraint CK_ArenaMatch_Part check (Part between 1 and 7),
        OwnerId int not null constraint FK_ArenaMatch_Owner foreign key references dbo.NguoiDung(MaNguoiDung),
        OpponentId int null constraint FK_ArenaMatch_Opponent foreign key references dbo.NguoiDung(MaNguoiDung),
        ForfeitById int null constraint FK_ArenaMatch_Forfeit foreign key references dbo.NguoiDung(MaNguoiDung),
        RoomCode varchar(12) null,
        Status varchar(16) not null constraint CK_ArenaMatch_Status check
            (Status in ('WAITING','READY','STARTING','ACTIVE','REVEAL','COMPLETED','SURRENDERED','ABANDONED')),
        OwnerReady bit not null constraint DF_ArenaMatch_OwnerReady default 0,
        OpponentReady bit not null constraint DF_ArenaMatch_OpponentReady default 0,
        QuestionIndex int not null constraint DF_ArenaMatch_QuestionIndex default 0,
        CreatedUtc datetime2(3) not null constraint DF_ArenaMatch_CreatedUtc default sysutcdatetime(),
        StartedUtc datetime2(3) null,
        CompletedUtc datetime2(3) null,
        DeadlineUtc datetime2(3) null,
        RevealUntilUtc datetime2(3) null,
        QuestionsJson nvarchar(max) null,
        AnswersJson nvarchar(max) not null constraint DF_ArenaMatch_AnswersJson default '[]',
        RowVersion rowversion not null,
        constraint CK_ArenaMatch_Players check (OpponentId is null or OpponentId <> OwnerId),
        constraint CK_ArenaMatch_Forfeit check (ForfeitById is null or ForfeitById = OwnerId or ForfeitById = OpponentId),
        constraint CK_ArenaMatch_QuestionsJson check (QuestionsJson is null or isjson(QuestionsJson) = 1),
        constraint CK_ArenaMatch_AnswersJson check (isjson(AnswersJson) = 1)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ArenaMatch') AND name = N'IX_ArenaMatch_StatusPartCreated')
    CREATE INDEX IX_ArenaMatch_StatusPartCreated ON dbo.ArenaMatch(Status, Part, CreatedUtc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ArenaMatch') AND name = N'IX_ArenaMatch_OwnerCompleted')
    CREATE INDEX IX_ArenaMatch_OwnerCompleted ON dbo.ArenaMatch(OwnerId, CompletedUtc DESC)
        INCLUDE (OpponentId, Part, Status);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ArenaMatch') AND name = N'IX_ArenaMatch_OpponentCompleted')
    CREATE INDEX IX_ArenaMatch_OpponentCompleted ON dbo.ArenaMatch(OpponentId, CompletedUtc DESC)
        INCLUDE (OwnerId, Part, Status) WHERE OpponentId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ArenaMatch') AND name = N'UX_ArenaMatch_RoomCode')
    CREATE UNIQUE INDEX UX_ArenaMatch_RoomCode ON dbo.ArenaMatch(RoomCode) WHERE RoomCode IS NOT NULL;
