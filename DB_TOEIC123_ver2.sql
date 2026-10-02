-- STO123 - FULL SCHEMA DB_TOEIC123_ver2
-- Dung de tao CSDL moi / luu tren Git. KHONG dung de migrate DB dang co.

--CREATE DATABASE DB_TOEIC123_ver2;
--GO

USE DB_TOEIC123_ver2;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

-- 01. TAI KHOAN VA XAC THUC
CREATE TABLE dbo.NguoiDung(
    MaNguoiDung int primary key identity(1,1),
    HoTen nvarchar(64) not null,
    Email varchar(254) not null unique,
    SoDienThoai varchar(16) null,
    AnhDaiDien varchar(512) null,
    VaiTro varchar(32) not null default 'HOC_VIEN' check (VaiTro in ('HOC_VIEN','ADMIN_NOI_DUNG','ADMIN_QUAN_LY')),
    TrangThai varchar(32) not null default 'CHO_XAC_THUC' check (TrangThai in ('CHO_XAC_THUC','HOAT_DONG','BI_KHOA')),
    NgayTao datetime not null default getdate(),
    LanDangNhapCuoi datetime null,
    constraint CK_NguoiDung_ThongTinBatBuoc check (
        nullif(ltrim(rtrim(HoTen)), N'') is not null
        and nullif(ltrim(rtrim(Email)), '') is not null
    )
);
GO

CREATE TABLE dbo.XacThucDangNhap(
    MaXacThuc int primary key identity(1,1),
    MaNguoiDung int not null unique foreign key references NguoiDung(MaNguoiDung),
    LoaiXacThuc varchar(16) not null check (LoaiXacThuc in ('EMAIL','GOOGLE')),
    MatKhauMaHoa varchar(256) null,
    GoogleId varchar(255) null,
    TaoLuc datetime not null default getdate(),
    constraint CK_XacThucDangNhap_PhuongThuc check (
        (LoaiXacThuc = 'EMAIL' and nullif(ltrim(rtrim(MatKhauMaHoa)), '') is not null and GoogleId is null)
        or
        (LoaiXacThuc = 'GOOGLE' and nullif(ltrim(rtrim(GoogleId)), '') is not null and MatKhauMaHoa is null)
    )
);
GO

CREATE TABLE dbo.XacThucOTP(
    MaXacThuc int primary key identity(1,1),
    MaNguoiDung int not null foreign key references NguoiDung(MaNguoiDung),
    MaOTP varchar(8) not null,
    LoaiOTP varchar(30) not null constraint DF_XacThucOTP_LoaiOTP default 'XAC_THUC_EMAIL',
    ThoiGianHetHan datetime not null default dateadd(minute, 15, getdate()),
    TrangThai varchar(16) not null default 'CHUA_XAC_THUC' check (TrangThai in ('DA_XAC_THUC','CHUA_XAC_THUC','HET_HAN')),
    NgayTao datetime not null default getdate(),
    constraint CK_XacThucOTP_ThoiGian check (ThoiGianHetHan > NgayTao),
    constraint CK_XacThucOTP_MaOTP check (nullif(ltrim(rtrim(MaOTP)), '') is not null),
    constraint CK_XacThucOTP_LoaiOTP check (LoaiOTP in ('XAC_THUC_EMAIL','QUEN_MAT_KHAU'))
);
GO

-- 02. KHOA HOC VA GOI HOC
CREATE TABLE dbo.KhoaHoc(
    MaKhoaHoc int primary key identity(1,1),
    TenKhoaHoc nvarchar(128) not null,
    GiaiDoan tinyint not null unique check (GiaiDoan between 1 and 3),
    DiemMucTieuToiDa int not null check (DiemMucTieuToiDa between 10 and 990),
    MoTa nvarchar(512) null,
    DuongDanAnhDaiDien varchar(512) null,
    TrangThai varchar(32) not null default 'DANG_MO' check (TrangThai in ('DANG_MO','DA_DONG')),
    MaAdminNoiDung int null foreign key references NguoiDung(MaNguoiDung),
    NgayTao datetime not null default getdate()
);
GO

CREATE TABLE dbo.GoiHoc(
    MaGoiHoc int primary key identity(1,1),
    TenGoiHoc nvarchar(64) not null,
    Gia decimal(18,2) not null check (Gia >= 0),
    SoNgaySuDung int not null check (SoNgaySuDung > 0),
    MoTa nvarchar(512) null,
    TrangThai varchar(16) not null default 'DANG_MO' check (TrangThai in ('DANG_MO','DA_DONG')),
    NgayTao datetime not null default getdate()
);
GO

CREATE TABLE dbo.GoiHocKhoaHoc(
    MaGoiHoc int not null foreign key references GoiHoc(MaGoiHoc),
    MaKhoaHoc int not null foreign key references KhoaHoc(MaKhoaHoc),
    constraint PK_GoiHocKhoaHoc primary key (MaGoiHoc, MaKhoaHoc)
);
GO

CREATE TABLE dbo.DangKyGoiHoc(
    MaDangKy int primary key identity(1,1),
    MaNguoiDung int not null foreign key references NguoiDung(MaNguoiDung),
    MaGoiHoc int not null foreign key references GoiHoc(MaGoiHoc),
    GiaDangKy decimal(18,2) not null check (GiaDangKy >= 0),
    SoNgaySuDung int not null check (SoNgaySuDung > 0),
    NgayDangKy datetime not null default getdate(),
    NgayBatDau datetime null,
    NgayKetThuc datetime null,
    TrangThai varchar(16) not null default 'CHO_THANH_TOAN' check (TrangThai in ('CHO_THANH_TOAN','DANG_SU_DUNG','HET_HAN')),
    constraint CK_DangKyGoiHoc_ThoiHan check (
        (TrangThai = 'CHO_THANH_TOAN' and NgayBatDau is null and NgayKetThuc is null)
        or
        (TrangThai in ('DANG_SU_DUNG','HET_HAN') and NgayBatDau is not null and NgayKetThuc is not null and NgayKetThuc > NgayBatDau)
    )
);
GO

CREATE TABLE dbo.CongThanhToan(
    MaCongThanhToan int primary key identity(1,1),
    MaCong varchar(32) not null,
    TenCongThanhToan nvarchar(128) not null,
    constraint UQ_CongThanhToan_MaCong unique (MaCong),
    constraint CK_CongThanhToan_MaCong check (nullif(ltrim(rtrim(MaCong)), '') is not null),
    constraint CK_CongThanhToan_Ten check (nullif(ltrim(rtrim(TenCongThanhToan)), N'') is not null)
);
GO

CREATE TABLE dbo.GiaoDichThanhToan(
    MaGiaoDich int primary key identity(1,1),
    MaDangKy int not null foreign key references DangKyGoiHoc(MaDangKy),
    MaCongThanhToan int not null foreign key references CongThanhToan(MaCongThanhToan),
    SoTien decimal(18,2) not null check (SoTien >= 0),
    MaGiaoDichCongThanhToan varchar(128) null,
    TrangThai varchar(32) not null default 'CHO_XU_LY' check (TrangThai in ('CHO_XU_LY','THANH_CONG','THAT_BAI')),
    NgayTao datetime not null default getdate(),
    NgayThanhToan datetime null,
    constraint CK_GiaoDichThanhToan_ThanhCong check (
        TrangThai <> 'THANH_CONG'
        or (NgayThanhToan is not null and nullif(ltrim(rtrim(MaGiaoDichCongThanhToan)), '') is not null)
    )
);
GO

-- 03. NGAN HANG CAU HOI, DE THI VA KET QUA
CREATE TABLE dbo.PartTOEIC(
    MaPart int primary key identity(1,1),
    SoPart int not null unique check (SoPart between 1 and 7),
    SoCauChuan int not null check (SoCauChuan > 0)
);
GO

CREATE TABLE dbo.NguLieu(
    MaNguLieu int primary key identity(1,1),
    NoiDungNguLieu nvarchar(max) null,
    NoiDungDich nvarchar(max) null,
    DuongDanAudio varchar(512) null,
    DuongDanAnh varchar(512) null
);
GO

CREATE TABLE dbo.NguLieuTaiLieu(
    MaTaiLieu int primary key identity(1,1),
    MaNguLieu int not null foreign key references NguLieu(MaNguLieu),
    LoaiTaiLieu varchar(32) not null,
    NoiDung nvarchar(max) null,
    DuongDanAnh varchar(512) null,
    ThuTu int not null check (ThuTu > 0),
    constraint UQ_NguLieuTaiLieu_ThuTu unique (MaNguLieu, ThuTu)
);
GO

CREATE TABLE dbo.CauHoi(
    MaCauHoi int primary key identity(1,1),
    NoiDung nvarchar(max) null,
    PhuongAnA nvarchar(512) not null,
    PhuongAnB nvarchar(512) not null,
    PhuongAnC nvarchar(512) not null,
    PhuongAnD nvarchar(512) null,
    PhuongAnDung char(1) not null check (PhuongAnDung in ('A','B','C','D')),
    GiaiThich nvarchar(max) null,
    MaPart int not null foreign key references PartTOEIC(MaPart),
    DoKho tinyint not null check (DoKho between 1 and 3),
    TrangThai varchar(16) not null default 'NHAP' check (TrangThai in ('NHAP','XUAT_BAN','AN')),
    constraint CK_CauHoi_PhuongAn check (PhuongAnD is not null or PhuongAnDung in ('A','B','C'))
);
GO

CREATE TABLE dbo.NhomCauHoi(
    MaCauHoi int primary key foreign key references CauHoi(MaCauHoi),
    MaNguLieu int not null foreign key references NguLieu(MaNguLieu),
    ThuTu int not null check (ThuTu > 0),
    constraint UQ_NhomCauHoi_ThuTu unique (MaNguLieu, ThuTu)
);
GO

CREATE TABLE dbo.PhanLoaiCauHoi(
    MaPhanLoai int primary key identity(1,1),
    TenPhanLoai nvarchar(64) not null,
    MoTa nvarchar(128) not null
);
GO

CREATE TABLE dbo.ChiTietPhanLoai(
    MaCauHoi int not null foreign key references CauHoi(MaCauHoi),
    MaPhanLoai int not null foreign key references PhanLoaiCauHoi(MaPhanLoai),
    constraint PK_ChiTietPhanLoai primary key (MaCauHoi, MaPhanLoai)
);
GO

CREATE TABLE dbo.ThongTinSinhDe(
    MaSinhDe int primary key identity(1,1),
    NgayTao datetime not null default getdate(),
    TenDe nvarchar(64) not null
);
GO

CREATE TABLE dbo.ChiTietCauHinhDeThi(
    MaSinhDe int not null foreign key references ThongTinSinhDe(MaSinhDe),
    MaPart int not null foreign key references PartTOEIC(MaPart),
    SoCauDe int not null check (SoCauDe >= 0),
    SoCauTB int not null check (SoCauTB >= 0),
    SoCauKho int not null check (SoCauKho >= 0),
    constraint PK_ChiTietCauHinhDeThi primary key (MaSinhDe, MaPart),
    constraint CK_ChiTietCauHinhDeThi_Tong check (SoCauDe + SoCauTB + SoCauKho > 0)
);
GO

CREATE TABLE dbo.DeThi(
    MaDeThi int primary key identity(1,1),
    TenDe nvarchar(128) not null,
    LoaiDe varchar(32) not null check (LoaiDe in ('DE_THI','DE_THI_DAU_VAO','DE_LUYEN_TAP')),
    ThoiGianLamBai int not null check (ThoiGianLamBai > 0),
    NamETS smallint null,
    SoDeETS int null,
    MaNguoiSoan int null foreign key references NguoiDung(MaNguoiDung),
    MaNguoiDuocGiao int null foreign key references NguoiDung(MaNguoiDung),
    TrangThai varchar(32) not null default 'CLOSE' check (TrangThai in ('OPEN','CLOSE')),
    MaSinhDe int null foreign key references ThongTinSinhDe(MaSinhDe),
    constraint CK_DeThi_ETS check (
        (NamETS is null and SoDeETS is null)
        or (NamETS is not null and SoDeETS is not null and SoDeETS > 0 and MaSinhDe is null)
    )
);
GO

CREATE TABLE dbo.CauHoiDeThi(
    MaDeThi int not null foreign key references DeThi(MaDeThi),
    MaCauHoi int not null foreign key references CauHoi(MaCauHoi),
    ThuTu int not null check (ThuTu > 0),
    constraint PK_CauHoiDeThi primary key (MaDeThi, MaCauHoi),
    constraint UQ_CauHoiDeThi_ThuTu unique (MaDeThi, ThuTu)
);
GO

CREATE TABLE dbo.KetQuaLamBai(
    MaKetQua int primary key identity(1,1),
    MaDeThi int null foreign key references DeThi(MaDeThi),
    MaHocVien int not null foreign key references NguoiDung(MaNguoiDung),
    MaLuotLam uniqueidentifier not null default newid() unique,
    LoaiBaiLam varchar(16) not null check (LoaiBaiLam in ('PLACEMENT','MOCK','PRACTICE')),
    TrangThai varchar(16) not null default 'DANG_LAM' check (TrangThai in ('DANG_LAM','DA_NOP','BO_DO','HET_GIO')),
    DiemNghe int null check (DiemNghe between 5 and 495),
    DiemDoc int null check (DiemDoc between 5 and 495),
    DiemTong as (DiemNghe + DiemDoc),
    NgayLamBai datetime2(3) not null default sysutcdatetime(),
    HetHanLuc datetime2(3) null,
    NgayNopBai datetime2(3) null,
    ThoiGianLamBai int null check (ThoiGianLamBai >= 0),
    ThoiGianConLaiGiay int null,
    BatDauPhienLuc datetime2(3) null,
    GiaiDoanLucNop tinyint null,
    constraint CK_KetQuaLamBai_ThoiHan check (HetHanLuc is null or HetHanLuc > NgayLamBai),
    constraint CK_KetQuaLamBai_NopBai check (
        (TrangThai in ('DA_NOP','HET_GIO') and NgayNopBai is not null)
        or (TrangThai in ('DANG_LAM','BO_DO') and NgayNopBai is null)
    ),
    constraint CK_KetQuaLamBai_ThoiGianConLaiGiay check (ThoiGianConLaiGiay is null or ThoiGianConLaiGiay >= 0),
    constraint CK_KetQuaLamBai_BatDauPhienLuc_CoThoiGian check (BatDauPhienLuc is null or ThoiGianConLaiGiay is not null),
    constraint CK_KetQuaLamBai_GiaiDoanLucNop check (
        GiaiDoanLucNop is null
        or (LoaiBaiLam = 'PLACEMENT' and TrangThai in ('DA_NOP','HET_GIO') and GiaiDoanLucNop between 1 and 3)
    )
);
GO

CREATE TABLE dbo.NhomLuotLam(
    MaNhomLuotLam int primary key identity(1,1),
    MaKetQua int not null foreign key references KetQuaLamBai(MaKetQua),
    MaNguLieuGoc int null foreign key references NguLieu(MaNguLieu),
    MaPart int not null foreign key references PartTOEIC(MaPart),
    ThuTu int not null check (ThuTu > 0),
    NoiDungNguLieu nvarchar(max) null,
    NoiDungDich nvarchar(max) null,
    DuongDanAudio varchar(512) null,
    DuongDanAnh varchar(512) null,
    TaiLieuJson nvarchar(max) null,
    constraint UQ_NhomLuotLam_ThuTu unique (MaKetQua, ThuTu)
);
GO

CREATE TABLE dbo.CauHoiLuotLam(
    MaCauHoiLuotLam int primary key identity(1,1),
    MaKetQua int not null foreign key references KetQuaLamBai(MaKetQua),
    MaNhomLuotLam int null foreign key references NhomLuotLam(MaNhomLuotLam),
    MaCauHoiGoc int null foreign key references CauHoi(MaCauHoi),
    MaPart int not null foreign key references PartTOEIC(MaPart),
    ThuTu int not null check (ThuTu > 0),
    ThuTuTrongPart int not null check (ThuTuTrongPart > 0),
    NoiDung nvarchar(max) null,
    PhuongAnA nvarchar(512) not null,
    PhuongAnB nvarchar(512) not null,
    PhuongAnC nvarchar(512) not null,
    PhuongAnD nvarchar(512) null,
    PhuongAnDung char(1) not null check (PhuongAnDung in ('A','B','C','D')),
    GiaiThich nvarchar(max) null,
    constraint UQ_CauHoiLuotLam_ThuTu unique (MaKetQua, ThuTu),
    constraint UQ_CauHoiLuotLam_ThuTuTrongPart unique (MaKetQua, MaPart, ThuTuTrongPart),
    constraint CK_CauHoiLuotLam_PhuongAn check (PhuongAnD is not null or PhuongAnDung in ('A','B','C'))
);
GO

CREATE TABLE dbo.ChiTietKetQua(
    MaCauHoiLuotLam int primary key foreign key references CauHoiLuotLam(MaCauHoiLuotLam),
    DapAnChon char(1) null check (DapAnChon in ('A','B','C','D')),
    DanhDau bit not null default 0
);
GO

CREATE TABLE dbo.KetQuaPhanLopKNN(
    MaPhanLop int primary key identity(1,1),
    MaKetQua int not null foreign key references KetQuaLamBai(MaKetQua),
    GiaiDoanDeXuat tinyint not null check (GiaiDoanDeXuat between 1 and 3),
    DiemMucTieu int null check (DiemMucTieu between 10 and 990),
    PhienBanMoHinh varchar(64) not null,
    NgayPhanLop datetime not null default getdate(),
    constraint CK_KetQuaPhanLopKNN_PhienBan check (nullif(ltrim(rtrim(PhienBanMoHinh)), '') is not null)
);
GO

-- 04. NOI DUNG HOC TAP
CREATE TABLE dbo.DanhMucBaiGiang(
    MaDanhMuc int primary key identity(1,1),
    TenDanhMuc nvarchar(64) not null,
    MoTa nvarchar(512),
    TrangThai varchar(32) not null default 'DANG_MO' check (TrangThai in ('DANG_MO','DA_DONG'))
);
GO

CREATE TABLE dbo.BaiGiang(
    MaBaiGiang int primary key identity(1,1),
    MaDanhMuc int null foreign key references DanhMucBaiGiang(MaDanhMuc),
    CapDo int null check (CapDo between 1 and 3),
    TieuDe nvarchar(128) not null,
    MaPart int null foreign key references PartTOEIC(MaPart),
    NoiDungTomTat nvarchar(512) null,
    NoiDung nvarchar(max) null,
    MaAdminNoiDung int null foreign key references NguoiDung(MaNguoiDung),
    NgayTao datetime not null default getdate(),
    TrangThai varchar(32) not null default 'DANG_MO' check (TrangThai in ('DANG_MO','DA_DONG'))
);
GO

CREATE TABLE dbo.TaiNguyenBaiGiang(
    MaTaiNguyen int primary key identity(1,1),
    MaBaiGiang int not null foreign key references BaiGiang(MaBaiGiang),
    LoaiTaiNguyen varchar(32) not null check (LoaiTaiNguyen in ('VIDEO','AUDIO','PDF','WORD','POWERPOINT','HINH_ANH')),
    TenTaiNguyen nvarchar(128) not null,
    DuongDanTep varchar(512) not null,
    ThuTu int not null check (ThuTu > 0),
    constraint UQ_TaiNguyenBaiGiang_ThuTu unique (MaBaiGiang, ThuTu)
);
GO

CREATE TABLE dbo.ChuDe(
    MaChuDe int primary key identity(1,1),
    TenChuDe nvarchar(128) not null,
    MoTa nvarchar(256) null,
    TrangThai varchar(32) not null default 'DANG_MO' check (TrangThai in ('DANG_MO','DA_DONG'))
);
GO

CREATE TABLE dbo.TuVung(
    MaTuVung int primary key identity(1,1),
    TuVung nvarchar(128) not null,
    Nghia nvarchar(256) not null,
    PhienAm nvarchar(128) null,
    LoaiTu varchar(32) null,
    ViDu nvarchar(512) null,
    DichViDu nvarchar(512) null,
    DuongDanAudio varchar(512) null,
    DuongDanAudioViDu varchar(512) null
);
GO

CREATE TABLE dbo.TuVungChuDe(
    MaTuVung int foreign key references TuVung(MaTuVung),
    MaChuDe int foreign key references ChuDe(MaChuDe),
    constraint PK_TuVungChuDe primary key (MaChuDe, MaTuVung)
);
GO

CREATE TABLE dbo.UnitKhoaHoc(
    MaUnit int primary key identity(1,1),
    MaKhoaHoc int not null foreign key references KhoaHoc(MaKhoaHoc),
    TenUnit nvarchar(128) not null,
    MoTa nvarchar(512) null,
    ThuTu int not null check (ThuTu > 0),
    constraint UQ_UnitKhoaHoc_ThuTu unique (MaKhoaHoc, ThuTu)
);
GO

CREATE TABLE dbo.LessonKhoaHoc(
    MaLesson int primary key identity(1,1),
    MaUnit int not null foreign key references UnitKhoaHoc(MaUnit),
    TenLesson nvarchar(128) not null,
    MoTa nvarchar(512) null,
    ThuTu int not null check (ThuTu > 0),
    constraint UQ_LessonKhoaHoc_ThuTu unique (MaUnit, ThuTu)
);
GO

CREATE TABLE dbo.BuocLoTrinh(
    MaBuoc int primary key identity(1,1),
    MaLesson int not null foreign key references LessonKhoaHoc(MaLesson),
    TieuDe nvarchar(128) not null,
    ThuTu int not null check (ThuTu > 0),
    MaBaiGiang int null foreign key references BaiGiang(MaBaiGiang),
    MaDeThi int null foreign key references DeThi(MaDeThi),
    MaChuDe int null foreign key references ChuDe(MaChuDe),
    constraint UQ_BuocLoTrinh_ThuTu unique (MaLesson, ThuTu),
    constraint CK_BuocLoTrinh_MotHoatDong check (
        (case when MaBaiGiang is null then 0 else 1 end)
        + (case when MaDeThi is null then 0 else 1 end)
        + (case when MaChuDe is null then 0 else 1 end) = 1
    )
);
GO

-- 05. TIEN DO VA LUYEN TU VUNG
CREATE TABLE dbo.TienDoBuocLoTrinh(
    MaTienDoBLT int primary key identity(1,1),
    MaNguoiDung int not null foreign key references NguoiDung(MaNguoiDung),
    MaBuoc int not null foreign key references BuocLoTrinh(MaBuoc),
    TrangThai varchar(16) not null default 'CHUA_LAM' check (TrangThai in ('CHUA_LAM','DANG_LAM','HOAN_THANH')),
    TyLeHoanThanh tinyint null check (TyLeHoanThanh between 0 and 100),
    MaKetQua int null foreign key references KetQuaLamBai(MaKetQua),
    CapNhatLuc datetime not null default getdate(),
    constraint UQ_TienDoBuocLoTrinh unique (MaNguoiDung, MaBuoc)
);
GO

CREATE TABLE dbo.TienDoTuVung(
    MaNguoiDung int not null foreign key references NguoiDung(MaNguoiDung),
    MaTuVung int not null foreign key references TuVung(MaTuVung),
    TrangThai varchar(32) not null default 'DANG_HOC' check (TrangThai in ('DANG_HOC','DA_THUOC')),
    CapNhatLuc datetime not null default getdate(),
    constraint PK_TienDoTuVung primary key (MaNguoiDung, MaTuVung)
);
GO

CREATE TABLE dbo.LanLuyenTuVung(
    MaLanLuyen int primary key identity(1,1),
    MaNguoiDung int not null foreign key references NguoiDung(MaNguoiDung),
    MaChuDe int not null foreign key references ChuDe(MaChuDe),
    TrangThai varchar(16) not null default 'DANG_LAM' check (TrangThai in ('DANG_LAM','DA_NOP','BO_DO')),
    NgayBatDau datetime not null default getdate(),
    NgayNopBai datetime null,
    constraint UQ_LanLuyenTuVung_ChuDe unique (MaLanLuyen, MaChuDe),
    constraint CK_LanLuyenTuVung_NopBai check (
        (TrangThai = 'DA_NOP' and NgayNopBai is not null and NgayNopBai >= NgayBatDau)
        or (TrangThai in ('DANG_LAM','BO_DO') and NgayNopBai is null)
    )
);
GO

CREATE TABLE dbo.ChiTietLuyenTuVung(
    MaLanLuyen int not null,
    MaChuDe int not null,
    MaTuVung int not null,
    ThuTu int not null check (ThuTu > 0),
    NoiDungCauHoi nvarchar(512) not null,
    PhuongAnA nvarchar(256) not null,
    PhuongAnB nvarchar(256) not null,
    PhuongAnC nvarchar(256) not null,
    PhuongAnD nvarchar(256) not null,
    PhuongAnDung char(1) not null check (PhuongAnDung in ('A','B','C','D')),
    DapAnChon char(1) null check (DapAnChon in ('A','B','C','D')),
    LaDung bit null,
    constraint PK_ChiTietLuyenTuVung primary key (MaLanLuyen, MaTuVung),
    constraint UQ_ChiTietLuyenTuVung_ThuTu unique (MaLanLuyen, ThuTu),
    constraint FK_ChiTietLuyenTuVung_Lan foreign key (MaLanLuyen, MaChuDe) references LanLuyenTuVung(MaLanLuyen, MaChuDe),
    constraint FK_ChiTietLuyenTuVung_Tu foreign key (MaChuDe, MaTuVung) references TuVungChuDe(MaChuDe, MaTuVung),
    constraint CK_ChiTietLuyenTuVung_ChamDiem check (
        LaDung is null
        or (LaDung = 1 and DapAnChon is not null and DapAnChon = PhuongAnDung)
        or (LaDung = 0 and (DapAnChon is null or DapAnChon <> PhuongAnDung))
    )
);
GO

-- INDEX
CREATE UNIQUE INDEX UX_XacThucDangNhap_GoogleId
ON dbo.XacThucDangNhap(GoogleId) WHERE GoogleId IS NOT NULL;
GO

CREATE UNIQUE INDEX UX_GiaoDichThanhToan_MaCong
ON dbo.GiaoDichThanhToan(MaCongThanhToan, MaGiaoDichCongThanhToan)
WHERE MaGiaoDichCongThanhToan IS NOT NULL;
GO

CREATE INDEX IX_KetQuaLamBai_HocVienTrangThai
ON dbo.KetQuaLamBai(MaHocVien, TrangThai, NgayLamBai DESC);
GO

CREATE INDEX IX_CauHoi_PartTrangThai
ON dbo.CauHoi(MaPart, TrangThai);
GO

CREATE INDEX IX_KetQuaPhanLopKNN_MaKetQua
ON dbo.KetQuaPhanLopKNN(MaKetQua);
GO

-- DU LIEU 7 PART TOEIC
INSERT INTO dbo.PartTOEIC(SoPart, SoCauChuan)
VALUES (1,6),(2,25),(3,39),(4,30),(5,30),(6,16),(7,54);
GO

-- =========================================================================
-- EXAM CORE CONTRACT (BACKEND)
-- =========================================================================
-- 1) Part 1/2: tao NhomLuotLam de snapshot media.
-- 2) Part 3/4/6/7: generator chon nguyen group, khong tach cau.
-- 3) Part 7: source document doc tu NguLieuTaiLieu; snapshot vao TaiLieuJson.
-- 4) Khong regenerate cau hoi sau khi attempt da duoc tao.
-- 5) Dung/sai/bo trong, Listening/Reading/7 Part va KNN percentage tinh o backend.
-- 6) MOCK khong chay KNN; PLACEMENT moi chay KNN.
-- 7) Timer authority = ThoiGianConLaiGiay + BatDauPhienLuc.
-- 8) BatDauPhienLuc NULL = PAUSED; NOT NULL = RUNNING.
-- 9) Paused attempt khong bi tru thoi gian theo wall-clock.
-- 10) DANG_LAM/BO_DO la unfinished; DA_NOP/HET_GIO la finalized.
-- 11) HetHanLuc chi giu compatibility, khong con la timer authority.
-- 12) Placement KNN thanh cong thi snapshot Stage vao GiaiDoanLucNop.
-- 13) GiaiDoanLucNop khong thay doi khi doi target hoac lam Placement moi.
-- 14) DiemMucTieu khong phai feature KNN; doi target khong chay lai KNN.
-- 15) Ownership, autosave, pause/resume, heartbeat, submit va timeout xu ly backend.
-- 16) Blob exam media nen dung ten/path immutable hoac versioned.