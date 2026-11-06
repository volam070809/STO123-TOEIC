-- One current recommendation result per learner and Part; practice answer history remains unchanged.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'dbo.SmartLearningFollowUp', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SmartLearningFollowUp(
        MaHocVien int not null constraint FK_SmartLearningFollowUp_Learner
            foreign key references dbo.NguoiDung(MaNguoiDung),
        MaPart int not null constraint FK_SmartLearningFollowUp_Part
            foreign key references dbo.PartTOEIC(MaPart),
        MaLuotDangLuyen int null,
        TyLeTruocDangLuyen decimal(5,2) null,
        TyLeTruocGanNhat decimal(5,2) null,
        TyLeSauGanNhat decimal(5,2) null,
        SoCauSauGanNhat int null,
        HoanThanhLucUtc datetime2(3) null,
        CapNhatLucUtc datetime2(3) not null constraint DF_SmartLearningFollowUp_CapNhat default sysutcdatetime(),
        constraint PK_SmartLearningFollowUp primary key (MaHocVien, MaPart),
        constraint CK_SmartLearningFollowUp_Rates check (
            (TyLeTruocDangLuyen is null or TyLeTruocDangLuyen between 0 and 100) and
            (TyLeTruocGanNhat is null or TyLeTruocGanNhat between 0 and 100) and
            (TyLeSauGanNhat is null or TyLeSauGanNhat between 0 and 100) and
            (SoCauSauGanNhat is null or SoCauSauGanNhat >= 0)
        )
    );
END;
