using Azure.Storage.Blobs;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using STO123.Models;
using STO123.Controllers;
using STO123.Services;
using STO123.Services.Admin;
using STO123.Services.Ai;

namespace STO123.Tests;

public sealed class AiKnowledgeRetrievalTests
{
    [Fact]
    public async Task RetrievesOnlyPublishedAuthorizedContentAndSeesLaterEdits()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE KhoaHoc (MaKhoaHoc INTEGER PRIMARY KEY, TenKhoaHoc TEXT NOT NULL,
                TrangThai TEXT NOT NULL, GiaiDoan INTEGER NOT NULL DEFAULT 1);
            CREATE TABLE NguoiDung (MaNguoiDung INTEGER PRIMARY KEY, HoTen TEXT NOT NULL,
                Email TEXT NOT NULL, SoDienThoai TEXT, VaiTro TEXT NOT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE UnitKhoaHoc (MaUnit INTEGER PRIMARY KEY, MaKhoaHoc INTEGER NOT NULL,
                TenUnit TEXT NOT NULL, ThuTu INTEGER NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE LessonKhoaHoc (MaLesson INTEGER PRIMARY KEY, MaUnit INTEGER NOT NULL,
                TenLesson TEXT NOT NULL, ThuTu INTEGER NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE BaiGiang (MaBaiGiang INTEGER PRIMARY KEY, TieuDe TEXT NOT NULL,
                NoiDungTomTat TEXT, NoiDung TEXT, TrangThai TEXT NOT NULL);
            CREATE TABLE TaiNguyenBaiGiang (MaTaiNguyen INTEGER PRIMARY KEY, MaBaiGiang INTEGER NOT NULL,
                TenTaiNguyen TEXT NOT NULL, LoaiTaiNguyen TEXT NOT NULL, ThuTu INTEGER NOT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE BuocLoTrinh (MaBuoc INTEGER PRIMARY KEY, MaLesson INTEGER NOT NULL,
                TieuDe TEXT NOT NULL, ThuTu INTEGER NOT NULL, IsArchived INTEGER NOT NULL DEFAULT 0,
                MaBaiGiang INTEGER, MaChuDe INTEGER, MaBaiTap INTEGER, MaDeThi INTEGER);
            CREATE TABLE DangKyGoiHoc (MaDangKy INTEGER PRIMARY KEY, MaNguoiDung INTEGER NOT NULL,
                MaGoiHoc INTEGER, TrangThai TEXT NOT NULL, NgayBatDau TEXT, NgayKetThuc TEXT);
            CREATE TABLE GiaoDichThanhToan (MaGiaoDich INTEGER PRIMARY KEY, MaDangKy INTEGER NOT NULL,
                TrangThai TEXT NOT NULL, NgayTao TEXT);
            CREATE TABLE ChiTietDangKyKhoaHoc (MaDangKy INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                PRIMARY KEY(MaDangKy, MaKhoaHoc));
            CREATE TABLE GoiHoc (MaGoiHoc INTEGER PRIMARY KEY);
            CREATE TABLE GoiHocKhoaHoc (MaGoiHoc INTEGER NOT NULL, MaKhoaHoc INTEGER NOT NULL,
                PRIMARY KEY(MaGoiHoc,MaKhoaHoc));
            CREATE TABLE ChuDe (MaChuDe INTEGER PRIMARY KEY, TenChuDe TEXT NOT NULL,
                MoTa TEXT, TrangThai TEXT NOT NULL);
            CREATE TABLE TuVung (MaTuVung INTEGER PRIMARY KEY, TuVung TEXT NOT NULL, Nghia TEXT NOT NULL,
                PhienAm TEXT, LoaiTu TEXT, ViDu TEXT, DichViDu TEXT);
            CREATE TABLE TuVungChuDe (MaTuVung INTEGER NOT NULL, MaChuDe INTEGER NOT NULL,
                PRIMARY KEY(MaTuVung, MaChuDe));
            CREATE TABLE PartTOEIC (MaPart INTEGER PRIMARY KEY, SoPart INTEGER NOT NULL);
            CREATE TABLE CauHoi (MaCauHoi INTEGER PRIMARY KEY, MaPart INTEGER NOT NULL,
                NoiDung TEXT, LoaiCauHoi TEXT NOT NULL, TrangThai TEXT NOT NULL,
                PhuongAnDung TEXT NOT NULL, GiaiThich TEXT);
            CREATE TABLE NguLieu (MaNguLieu INTEGER PRIMARY KEY, NoiDungNguLieu TEXT);
            CREATE TABLE NhomCauHoi (MaCauHoi INTEGER PRIMARY KEY, MaNguLieu INTEGER NOT NULL);
            CREATE TABLE NguLieuTaiLieu (MaTaiLieu INTEGER PRIMARY KEY, MaNguLieu INTEGER NOT NULL,
                LoaiTaiLieu TEXT NOT NULL, NoiDung TEXT, ThuTu INTEGER NOT NULL);
            CREATE TABLE DeThi (MaDeThi INTEGER PRIMARY KEY, TenDe TEXT NOT NULL,
                LoaiDe TEXT NOT NULL, TrangThai TEXT NOT NULL, ThoiGianLamBai INTEGER NOT NULL);
            CREATE TABLE KetQuaLamBai (MaKetQua INTEGER PRIMARY KEY, MaHocVien INTEGER NOT NULL,
                MaDeThi INTEGER, LoaiBaiLam TEXT NOT NULL, TrangThai TEXT NOT NULL,
                DiemTong INTEGER, NgayNopBai TEXT);
            CREATE TABLE NhomLuotLam (MaNhomLuotLam INTEGER PRIMARY KEY, NoiDungNguLieu TEXT);
            CREATE TABLE CauHoiLuotLam (MaCauHoiLuotLam INTEGER PRIMARY KEY, MaKetQua INTEGER NOT NULL,
                MaPart INTEGER NOT NULL, MaNhomLuotLam INTEGER, NoiDung TEXT,
                PhuongAnA TEXT, PhuongAnB TEXT, PhuongAnC TEXT, PhuongAnD TEXT,
                PhuongAnDung TEXT, GiaiThich TEXT);
            CREATE TABLE ChiTietKetQua (MaCauHoiLuotLam INTEGER PRIMARY KEY, DapAnChon TEXT);
            CREATE TABLE TienDoBuocLoTrinh (MaTienDoBLT INTEGER PRIMARY KEY,
                MaNguoiDung INTEGER NOT NULL, MaBuoc INTEGER NOT NULL, MaDangKy INTEGER NOT NULL,
                TrangThai TEXT NOT NULL);
            CREATE TABLE PhienBanBaiTapKhoaHoc (MaPhienBan INTEGER PRIMARY KEY,
                MaBaiTap INTEGER NOT NULL, TrangThai TEXT NOT NULL);
            CREATE TABLE KetQuaPhanLopKNN (MaKetQua INTEGER PRIMARY KEY,
                GiaiDoanDeXuat INTEGER, DiemMucTieu INTEGER, NgayPhanLop TEXT);
            CREATE TABLE ThongKeLuyenPart (MaHocVien INTEGER NOT NULL, MaPart INTEGER NOT NULL,
                SoLanLuyen INTEGER NOT NULL, TongCauDaLam INTEGER NOT NULL,
                TongCauDung INTEGER NOT NULL, SoCauLanCuoi INTEGER NOT NULL,
                SoDungLanCuoi INTEGER NOT NULL, LanLuyenGanNhat TEXT,
                PRIMARY KEY(MaHocVien,MaPart));
            CREATE TABLE LanLuyenTuVung (MaLanLuyen INTEGER PRIMARY KEY,
                MaNguoiDung INTEGER NOT NULL, MaChuDe INTEGER NOT NULL,
                TrangThai TEXT NOT NULL, NgayNopBai TEXT);
            CREATE TABLE ChiTietLuyenTuVung (MaLanLuyen INTEGER NOT NULL, MaChuDe INTEGER NOT NULL,
                MaTuVung INTEGER NOT NULL,
                LaDung INTEGER, PRIMARY KEY(MaLanLuyen,MaTuVung));
            CREATE TABLE AiHoiThoai (MaHoiThoai INTEGER PRIMARY KEY, MaHocVien INTEGER NOT NULL,
                TieuDe TEXT NOT NULL, TaoLuc TEXT NOT NULL, CapNhatLuc TEXT NOT NULL);
            CREATE TABLE AiTinNhan (MaTinNhan INTEGER PRIMARY KEY AUTOINCREMENT, MaHoiThoai INTEGER NOT NULL,
                ThuTu INTEGER NOT NULL, VaiTro TEXT NOT NULL, NoiDung TEXT NOT NULL,
                MaYeuCau TEXT, TaoLuc TEXT NOT NULL);
            INSERT INTO KhoaHoc VALUES (1,'Khóa có quyền','DANG_MO',1),
                (2,'Khóa chưa mua','DANG_MO',1);
            INSERT INTO NguoiDung VALUES (8,'Người học A','a@example.invalid',NULL,'HOC_VIEN','HOAT_DONG'),
                (9,'Người học B','b@example.invalid',NULL,'HOC_VIEN','HOAT_DONG');
            INSERT INTO UnitKhoaHoc VALUES (1,1,'Unit 1',1,0),(2,2,'Unit 2',1,0);
            INSERT INTO LessonKhoaHoc VALUES (1,1,'Present Perfect',1,0),
                (2,2,'Bài không có quyền',1,0),(3,1,'Bài nháp',2,0);
            INSERT INTO BaiGiang VALUES (1,'Present Perfect',NULL,'Present perfect is used for experience.','DANG_MO'),
                (2,'Present Perfect',NULL,'Private course secret.','DANG_MO'),
                (3,'Present Perfect',NULL,'Unpublished secret.','NHAP');
            INSERT INTO TaiNguyenBaiGiang VALUES (1,1,'Present Perfect handout','PDF',1,0),
                (2,2,'Private handout','PDF',1,0);
            INSERT INTO BuocLoTrinh VALUES (1,1,'Thì hiện tại hoàn thành',1,0,1,NULL,NULL,NULL),
                (2,2,'Bài học riêng',1,0,2,NULL,NULL,NULL),
                (3,3,'Bài chưa xuất bản',1,0,3,NULL,NULL,NULL);
            INSERT INTO DangKyGoiHoc VALUES (1,8,NULL,'DANG_SU_DUNG','2000-01-01','2100-01-01');
            INSERT INTO GiaoDichThanhToan VALUES (1,1,'THANH_CONG','2026-01-01');
            INSERT INTO DangKyGoiHoc VALUES (3,8,NULL,'THAT_BAI','2026-01-01','2026-01-01');
            INSERT INTO GiaoDichThanhToan VALUES (3,3,'THAT_BAI','2026-02-01');
            INSERT INTO ChiTietDangKyKhoaHoc VALUES (1,1);
            INSERT INTO ChuDe VALUES (1,'Business',NULL,'DANG_MO'),(2,'Private',NULL,'DONG');
            INSERT INTO TuVung VALUES (1,'acumen','sự nhạy bén',NULL,NULL,'business acumen',NULL),
                (2,'privateword','không mở',NULL,NULL,NULL,NULL);
            INSERT INTO TuVungChuDe VALUES (1,1),(2,2);
            INSERT INTO PartTOEIC VALUES (5,5);
            INSERT INTO CauHoi VALUES (1,5,'Choose the correct verb form','LUYEN_TAP','XUAT_BAN','B','The correct answer is B'),
                (2,5,'Draft-only question','LUYEN_TAP','NHAP','A','Draft answer');
            INSERT INTO DeThi VALUES (1,'ETS 2023 - Test 01','DE_THI','OPEN',120),
                (2,'Draft exam','DE_THI','DRAFT',120);
            INSERT INTO KetQuaLamBai(MaKetQua,MaHocVien,LoaiBaiLam,TrangThai) VALUES
                (100,8,'PRACTICE','DANG_LAM'),(101,9,'PRACTICE','DANG_LAM');
            INSERT INTO KetQuaLamBai(MaKetQua,MaHocVien,MaDeThi,LoaiBaiLam,TrangThai,DiemTong,NgayNopBai) VALUES
                (105,8,1,'MOCK','DA_NOP',650,'2026-01-01'),
                (106,9,1,'MOCK','DA_NOP',900,'2026-01-01'),
                (107,8,NULL,'PLACEMENT','DA_NOP',490,'2026-01-01');
            INSERT INTO KetQuaPhanLopKNN VALUES (107,2,700,'2026-01-01');
            INSERT INTO CauHoiLuotLam VALUES
                (10,100,5,NULL,'Unchecked snapshot','A1','B1','C1','D1','B','Original unchecked explanation'),
                (11,100,5,NULL,'Checked snapshot','A2','B2','C2','D2','C','Original checked explanation'),
                (12,101,5,NULL,'Other learner secret','A3','B3','C3','D3','A','Other learner explanation');
            INSERT INTO ChiTietKetQua VALUES (11,'B');
            INSERT INTO TienDoBuocLoTrinh VALUES (1,8,1,1,'HOAN_THANH'),
                (2,9,2,1,'HOAN_THANH');
            INSERT INTO ThongKeLuyenPart VALUES (8,7,3,30,9,10,2,'2026-01-01'),
                (9,7,2,20,20,10,10,'2026-01-01');
            INSERT INTO LanLuyenTuVung VALUES (1,8,1,'DA_NOP','2026-01-01'),
                (2,9,2,'DA_NOP','2026-01-01');
            INSERT INTO ChiTietLuyenTuVung VALUES (1,1,1,1),(2,2,2,0);
            INSERT INTO AiHoiThoai VALUES (1,8,'Cuộc trò chuyện mới','2026-01-01','2026-01-01'),
                (2,9,'Riêng tư','2026-01-01','2026-01-01');
            """);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["LectureResources:ContainerName"] = "isolated", ["ConnectionStrings:ToeicDb"] = "unused"
        }).Build();
        var storage = new LectureResourceStorage(
            new BlobServiceClient(new Uri("https://isolated.invalid.blob.core.windows.net")), config);
        var pdf = new AiPdfTextService(db, storage, new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, new ContentRevisionStore(config, TimeProvider.System),
            NullLogger<AiPdfTextService>.Instance);
        var service = new AiKnowledgeService(db, TimeProvider.System,
            new AiLearningContextService(db, TimeProvider.System), pdf);

        var lesson = await service.ResolveAsync(8, "Find present perfect lesson", null, default);
        Assert.Contains("Present perfect is used", lesson.Text);
        Assert.DoesNotContain("Private course secret", lesson.Text);
        Assert.DoesNotContain("Unpublished secret", lesson.Text);
        Assert.Contains(lesson.Sources, x => x.Url == "/courses/1/steps/1");
        Assert.DoesNotContain(lesson.Sources, x => x.Url == "/courses/2/steps/2");
        var followUp = await service.ResolveAsync(8, "Bài đó có tài liệu gì?", null, default,
            [new AiPromptMessage("USER", "Find present perfect lesson"),
             new AiPromptMessage("MODEL", "Bài phù hợp.\n- [Bài học](/courses/1/steps/1)")]);
        Assert.Contains("Present Perfect handout", followUp.Text);
        Assert.DoesNotContain("Private handout", followUp.Text);
        var inaccessibleFollowUp = await service.ResolveAsync(8, "Bài đó có tài liệu gì?", null,
            default, [new AiPromptMessage("MODEL", "- [Bài học](/courses/2/steps/2)")]);
        Assert.DoesNotContain("Private handout", inaccessibleFollowUp.Text);

        var vocabulary = await service.ResolveAsync(8, "Explain acumen", null, default);
        Assert.Contains("sự nhạy bén", vocabulary.Text);
        Assert.Contains(vocabulary.Sources, x => x.Url == "/vocabulary?topic=1");
        var topicCounts = await service.ResolveAsync(8, "Có bao nhiêu chủ đề từ vựng?", null, default);
        Assert.Contains("1 chủ đề từ vựng đang mở", topicCounts.Text);
        Assert.Contains("1 từ vựng riêng biệt", topicCounts.Text);
        Assert.Equal("Hiện có 1 từ vựng riêng biệt thuộc các chủ đề đang mở.",
            (await service.ResolveAsync(8, "Có mấy từ?", null, default)).ExactAnswer);
        Assert.DoesNotContain("Private: ", topicCounts.Text);
        var businessCount = await service.ResolveAsync(8, "Chủ đề Business có bao nhiêu từ?", null, default);
        Assert.Equal("Chủ đề Business hiện có 1 từ vựng.", businessCount.ExactAnswer);
        var practiceHub = await service.ResolveAsync(8, "Trang này có gì để luyện?", null,
            default, route: "/practice");
        Assert.Contains("Chọn hình thức luyện tập", practiceHub.Text);
        Assert.Contains("1 chủ đề từ vựng đang mở", practiceHub.Text);
        Assert.Contains("Part 7: 3 lần, 9/30 đúng", practiceHub.Text);
        Assert.DoesNotContain("20/20 đúng", practiceHub.Text);
        var vocabularyPractice = await service.ResolveAsync(8,
            "Kết quả luyện từ của tôi", null, default, route: "/practice/vocabulary");
        Assert.Contains("Lượt luyện từ vựng đã nộp của chính học viên: 1", vocabularyPractice.Text);
        Assert.Contains("Business: 1/1 đúng", vocabularyPractice.Text);
        Assert.DoesNotContain("Private: 0/1", vocabularyPractice.Text);
        var partGuide = await service.ResolveAsync(8, "Luyện Part 7 ở đâu?", null, default);
        Assert.Contains("Part 7: 3 lần, 9/30 đúng", partGuide.Text);
        Assert.Contains(partGuide.Sources, source => source.Url == "/practice/parts");
        var courseCounts = await service.ResolveAsync(8, "Danh sách khóa học đang mở", null, default);
        Assert.Contains("Khóa học đang mở theo SQL: 2", courseCounts.Text);
        var mockCatalog = await service.ResolveAsync(8, "Có đề ETS 2023 nào đang mở?", null, default);
        Assert.Contains("ETS 2023 - Test 01", mockCatalog.Text);
        Assert.Contains("kết quả gần nhất 650 điểm", mockCatalog.Text);
        Assert.DoesNotContain("Draft exam", mockCatalog.Text);
        Assert.DoesNotContain("900 điểm", mockCatalog.Text);
        var placementResult = await service.ResolveAsync(8, "KNN của tôi ra sao?", null, default);
        Assert.Contains("giai đoạn 2", placementResult.Text);
        Assert.Contains("490 điểm", placementResult.Text);
        var ownedCourses = await service.ResolveAsync(8,
            "Khóa đã mua của tôi còn hạn tới khi nào?", null, default);
        Assert.Contains("Khóa có quyền", ownedCourses.Text);
        Assert.Contains("hết hạn 01/01/2100", ownedCourses.Text);
        Assert.DoesNotContain("Khóa chưa mua (hết hạn", ownedCourses.Text);
        var paymentStatus = await service.ResolveAsync(8, "Thanh toán của tôi thế nào?", null, default);
        Assert.Contains("Giao dịch gần nhất #3: THAT_BAI", paymentStatus.Text);
        Assert.Contains("Thanh toán lại", paymentStatus.Text);
        var ownProfile = await service.ResolveAsync(8, "Tên và email của tôi là gì?", null, default);
        Assert.Contains("Người học A", ownProfile.Text);
        Assert.Contains("a@example.invalid", ownProfile.Text);
        Assert.DoesNotContain("Người học B", ownProfile.Text);
        var closed = await service.ResolveAsync(8, "Explain privateword", null, default);
        Assert.DoesNotContain("không mở", closed.Text);
        var practice = await service.ResolveAsync(8, "Show practice question Part 5", null, default);
        Assert.Contains("Choose the correct verb form", practice.Text);
        Assert.DoesNotContain("The correct answer is B", practice.Text);
        Assert.DoesNotContain("Draft-only question", practice.Text);

        await db.Database.ExecuteSqlRawAsync("UPDATE BaiGiang SET NoiDung='Present perfect updated by Admin.' WHERE MaBaiGiang=1");
        var updated = await service.ResolveAsync(8, "Find present perfect lesson", null, default);
        Assert.Contains("updated by Admin", updated.Text);
        Assert.DoesNotContain("used for experience", updated.Text);

        // Each retrieval reads current published SQL state: additions and revocations need no reindex.
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO KhoaHoc VALUES (3,'New published course','DANG_MO',2);
            INSERT INTO UnitKhoaHoc VALUES (4,3,'New unit',1,0);
            INSERT INTO LessonKhoaHoc VALUES (4,4,'Future grammar lesson',1,0);
            INSERT INTO BaiGiang VALUES (4,'Future grammar',NULL,'Future tense is used for plans.','DANG_MO');
            INSERT INTO BuocLoTrinh VALUES (4,4,'Future grammar',1,0,4,NULL,NULL,NULL);
            INSERT INTO DangKyGoiHoc VALUES (2,8,NULL,'DANG_SU_DUNG','2000-01-01','2100-01-01');
            INSERT INTO GiaoDichThanhToan VALUES (2,2,'THANH_CONG','2026-01-02');
            INSERT INTO ChiTietDangKyKhoaHoc VALUES (2,3);
            INSERT INTO TuVung VALUES (3,'itinerary','lịch trình',NULL,NULL,NULL,NULL);
            INSERT INTO TuVungChuDe VALUES (3,1);
            INSERT INTO CauHoi VALUES (3,5,'New published question','LUYEN_TAP','XUAT_BAN','D','Do not reveal');
            INSERT INTO PartTOEIC VALUES (7,7);
            INSERT INTO CauHoi VALUES (4,7,'What is the itinerary?','LUYEN_TAP','XUAT_BAN','A','Do not reveal Part 7');
            INSERT INTO NguLieu VALUES (10,'Travel documents');
            INSERT INTO NhomCauHoi VALUES (4,10);
            INSERT INTO NguLieuTaiLieu VALUES (10,10,'TEXT','The train departs at 8 a.m.',1);
            """);
        var newLesson = await service.ResolveAsync(8, "Find future grammar", null, default);
        Assert.Contains("Future tense is used", newLesson.Text);
        Assert.Contains(newLesson.Sources, x => x.Url == "/courses/3/steps/4");
        Assert.Contains("lịch trình", (await service.ResolveAsync(8, "Explain itinerary", null, default)).Text);
        Assert.Contains("2 từ vựng riêng biệt", (await service.ResolveAsync(8,
            "Hệ thống có bao nhiêu topic từ vựng?", null, default)).Text);
        var newQuestion = await service.ResolveAsync(8, "Show practice question Part 5", null, default);
        Assert.Contains("New published question", newQuestion.Text);
        Assert.DoesNotContain("Do not reveal", newQuestion.Text);
        var readableDocument = await service.ResolveAsync(8, "Show practice question Part 7", null, default);
        Assert.Contains("The train departs at 8 a.m.", readableDocument.Text);
        Assert.DoesNotContain("Do not reveal Part 7", readableDocument.Text);
        Assert.Contains("The train departs at 8 a.m.",
            (await service.ResolveAsync(8, "Find Part 7 train departs document", null, default)).Text);
        await db.Database.ExecuteSqlRawAsync("UPDATE DangKyGoiHoc SET TrangThai='HET_HAN' WHERE MaDangKy=2");
        Assert.DoesNotContain("Future tense is used",
            (await service.ResolveAsync(8, "Find future grammar", null, default)).Text);
        await db.Database.ExecuteSqlRawAsync("UPDATE ChuDe SET TrangThai='DONG' WHERE MaChuDe=1");
        Assert.DoesNotContain("lịch trình",
            (await service.ResolveAsync(8, "Explain itinerary", null, default)).Text);
        await db.Database.ExecuteSqlRawAsync("UPDATE ChuDe SET TrangThai='DANG_MO' WHERE MaChuDe=1");

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO LessonKhoaHoc VALUES (5,1,'Part 7 Reading',3,0);
            INSERT INTO BaiGiang VALUES (5,'Part 7 Reading',NULL,'Read long workplace emails carefully.','DANG_MO');
            INSERT INTO BuocLoTrinh VALUES (5,5,'Part 7 Reading',3,0,5,NULL,NULL,NULL);
            INSERT INTO TaiNguyenBaiGiang VALUES (5,5,'Part 7 email handout','PDF',1,0);
            """);
        var combined = await service.ResolveAsync(8,
            "Tôi yếu Part 7, trong khóa đã mua có bài nào giúp cải thiện?", null, default);
        Assert.Contains("Part 7: 3 lần, 9/30 đúng", combined.Text);
        Assert.Contains("Part 7 Reading", combined.Text);
        Assert.DoesNotContain("Private course secret", combined.Text);
        Assert.DoesNotContain("20/20 đúng", combined.Text);
        var progressSummary = await service.ResolveAsync(8, "Tiến độ của tôi đến đâu?", null, default);
        Assert.Contains("1/2 hoạt động hoàn thành", progressSummary.Text);
        Assert.Contains("Part 7 Reading", progressSummary.Text);
        var plannerHandler = new StaticPlanGemini(db);
        var plannerConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Gemini:ApiKey"] = "isolated-test-key", ["Gemini:Model"] = "gemini-3.5-flash-lite"
        }).Build();
        var semantic = new AiKnowledgeService(db, TimeProvider.System,
            new AiLearningContextService(db, TimeProvider.System), pdf,
            intentModel: new AiToeicService(new HttpClient(plannerHandler), plannerConfig));
        var unseenWording = await semantic.ResolveAsync(8,
            "Tôi cần cải thiện khả năng đọc thư dài ở chỗ làm", null, default);
        Assert.Contains("Part 7 Reading", unseenWording.Text);
        Assert.Equal(1, plannerHandler.Requests);
        var nextQuestion = await service.ResolveAsync(8, "Bài đó có tài liệu gì?", null, default,
            [new AiPromptMessage("USER", "Tôi yếu Part 7, trong khóa đã mua có bài nào giúp cải thiện?"),
             new AiPromptMessage("MODEL", "Đọc Part 7 Reading.\n- [Bài học](/courses/1/steps/5)")]);
        Assert.Contains("Part 7 email handout", nextQuestion.Text);
        Assert.DoesNotContain("Private handout", nextQuestion.Text);
        var differentLearner = await service.ResolveAsync(9,
            "Tôi yếu Part 7, trong khóa đã mua có bài nào giúp cải thiện?", null, default);
        Assert.DoesNotContain("Part 7 Reading", differentLearner.Text);
        Assert.DoesNotContain("9/30 đúng", differentLearner.Text);
        Assert.Contains("lịch trình",
            (await service.ResolveAsync(8, "Explain itinerary", null, default)).Text);

        long publicRevision = 1;
        using var publicCache = new MemoryCache(new MemoryCacheOptions());
        var cachedService = new AiKnowledgeService(db, TimeProvider.System,
            new AiLearningContextService(db, TimeProvider.System), pdf,
            publicCache, null, null, _ => Task.FromResult(publicRevision));
        Assert.Contains("lịch trình", (await cachedService.ResolveAsync(8, "Explain itinerary", null, default)).Text);
        await db.Database.ExecuteSqlRawAsync("UPDATE TuVung SET Nghia='hành trình mới' WHERE MaTuVung=3");
        Assert.DoesNotContain("hành trình mới", (await cachedService.ResolveAsync(8, "Explain itinerary", null, default)).Text);
        publicRevision++;
        Assert.Contains("hành trình mới", (await cachedService.ResolveAsync(8, "Explain itinerary", null, default)).Text);
        await db.Database.ExecuteSqlRawAsync("UPDATE ChuDe SET TrangThai='DONG' WHERE MaChuDe=1");
        publicRevision++;
        Assert.DoesNotContain("hành trình mới", (await cachedService.ResolveAsync(8, "Explain itinerary", null, default)).Text);
        await db.Database.ExecuteSqlRawAsync("UPDATE ChuDe SET TrangThai='DANG_MO' WHERE MaChuDe=1");

        var learning = new AiLearningContextService(db, TimeProvider.System);
        var uncheckedQuestion = await learning.ResolveAsync(8,
            new AiLearningContext("PRACTICE", null, null, null, 100, 10), default);
        Assert.True(uncheckedQuestion.UncheckedPractice);
        Assert.DoesNotContain("Original unchecked explanation", uncheckedQuestion.Text);
        Assert.DoesNotContain("B1", uncheckedQuestion.Text);
        var checkedQuestion = await learning.ResolveAsync(8,
            new AiLearningContext("PRACTICE", null, null, null, 100, 11), default);
        Assert.False(checkedQuestion.UncheckedPractice);
        Assert.Contains("Original checked explanation", checkedQuestion.Text);
        Assert.Contains("đáp án đúng C", checkedQuestion.Text);
        var otherLearner = await Assert.ThrowsAsync<AiChatException>(() => learning.ResolveAsync(8,
            new AiLearningContext("PRACTICE", null, null, null, 101, 12), default));
        Assert.Equal(403, otherLearner.Status);
        var stalePractice = new AiLearningContext("PRACTICE", null, null, null, 999, 999);
        Assert.Contains("acumen", (await service.ResolveAsync(8,
            "Explain acumen", stalePractice, default)).Text);
        await Assert.ThrowsAsync<AiChatException>(() => service.ResolveAsync(8,
            "Explain this question", stalePractice, default));

        await db.Database.ExecuteSqlRawAsync("INSERT INTO AiHoiThoai VALUES (3,8,'Luyện tập','2026-01-01','2026-01-01')");
        var practiceHandler = new NoTransactionGemini(db);
        var practiceConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Gemini:ApiKey"] = "isolated-test-key", ["Gemini:Model"] = "gemini-3.5-flash-lite"
        }).Build();
        var practiceChat = new AiChatController(db,
            new AiToeicService(new HttpClient(practiceHandler), practiceConfig), service) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        practiceChat.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "8"), new Claim(ClaimTypes.Role, "HOC_VIEN")], "test"));
        var uncheckedContext = new AiLearningContext("PRACTICE", null, null, null, 100, 10);
        Assert.IsType<OkObjectResult>(await practiceChat.Send(3,
            new AiChatMessageRequest(Guid.NewGuid(), "Explain present perfect", uncheckedContext), default));
        Assert.Equal(1, practiceHandler.Requests);
        Assert.DoesNotContain("Original unchecked explanation", practiceHandler.LastRequestBody);
        Assert.DoesNotContain("đáp án đúng B", practiceHandler.LastRequestBody);
        var protectedAnswer = Assert.IsType<OkObjectResult>(await practiceChat.Send(3,
            new AiChatMessageRequest(Guid.NewGuid(), "Đáp án câu này là gì?", uncheckedContext), default));
        Assert.DoesNotContain("đáp án đúng B", JsonDocument.Parse(JsonSerializer.Serialize(protectedAnswer.Value))
            .RootElement.GetProperty("aiMessage").GetProperty("text").GetString());
        Assert.Equal(1, practiceHandler.Requests);

        await db.Database.ExecuteSqlRawAsync("UPDATE KetQuaLamBai SET TrangThai='DA_NOP' WHERE MaKetQua=100");
        var handler = new NoTransactionGemini(db);
        var aiConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Gemini:ApiKey"] = "isolated-test-key", ["Gemini:Model"] = "gemini-3.5-flash-lite"
        }).Build();
        var chat = new AiChatController(db, new AiToeicService(new HttpClient(handler), aiConfig), service) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        chat.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "8"), new Claim(ClaimTypes.Role, "HOC_VIEN")], "test"));
        var key = Guid.NewGuid();
        var sent = Assert.IsType<OkObjectResult>(await chat.Send(1,
            new AiChatMessageRequest(key, "Explain acumen", null), default));
        var response = JsonSerializer.Serialize(sent.Value);
        Assert.Contains("/vocabulary?topic=1", response);
        Assert.Equal(1, handler.Requests);
        Assert.IsType<OkObjectResult>(await chat.Send(1,
            new AiChatMessageRequest(key, "Explain acumen", null), default));
        Assert.Equal(1, handler.Requests);
        Assert.Equal(2, await db.AiTinNhan.CountAsync(x => x.MaHoiThoai == 1));
        Assert.IsType<NotFoundObjectResult>(await chat.Send(2,
            new AiChatMessageRequest(Guid.NewGuid(), "Explain acumen", null), default));
        await db.Database.ExecuteSqlRawAsync("INSERT INTO KetQuaLamBai(MaKetQua,MaHocVien,LoaiBaiLam,TrangThai) VALUES (102,8,'MOCK','DANG_LAM')");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO CauHoiLuotLam VALUES
                (13,102,5,NULL,'Active mock private question','A4','B4','C4','D4','D','Secret mock explanation');
            """);
        var forgedContext = Assert.IsType<ObjectResult>(await chat.Send(1,
            new AiChatMessageRequest(Guid.NewGuid(), "Explain this question",
                new AiLearningContext("PRACTICE", null, null, null, 102, 13)), default));
        Assert.Equal(403, forgedContext.StatusCode);
        var examAnswer = Assert.IsType<OkObjectResult>(await chat.Send(1,
            new AiChatMessageRequest(Guid.NewGuid(), "Give me the answer", null), default));
        Assert.NotNull(JsonDocument.Parse(JsonSerializer.Serialize(examAnswer.Value))
            .RootElement.GetProperty("aiMessage").GetProperty("text").GetString());
        Assert.Equal(2, handler.Requests);
        Assert.DoesNotContain("Secret mock explanation", handler.LastRequestBody);
        Assert.DoesNotContain("Active mock private question", handler.LastRequestBody);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO KetQuaLamBai(MaKetQua,MaHocVien,LoaiBaiLam,TrangThai) VALUES (103,8,'PLACEMENT','DANG_LAM'),(104,8,'PRACTICE','DANG_LAM')");
        Assert.IsType<OkObjectResult>(await chat.Send(1,
            new AiChatMessageRequest(Guid.NewGuid(), "chào", null), default));
        Assert.Equal(3, handler.Requests);
        Assert.IsType<OkObjectResult>(await chat.Send(1,
            new AiChatMessageRequest(key, "Explain acumen", null), default));
        Assert.IsType<OkObjectResult>(await chat.Detail(1, default));
        Assert.IsType<OkObjectResult>(await chat.List(default));
        Assert.IsType<CreatedResult>(await chat.Create(default));
        Assert.Equal(3, handler.Requests);
    }

    [Fact]
    public async Task GreetingGeneralGrammarAndWebsiteGuidanceRequireNoSqlTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ToeicDbContext(new DbContextOptionsBuilder<ToeicDbContext>()
            .UseSqlite(connection).Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["LectureResources:ContainerName"] = "isolated", ["ConnectionStrings:ToeicDb"] = "unused"
        }).Build();
        var storage = new LectureResourceStorage(
            new BlobServiceClient(new Uri("https://isolated.invalid.blob.core.windows.net")), config);
        var pdf = new AiPdfTextService(db, storage, new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System, new ContentRevisionStore(config, TimeProvider.System),
            NullLogger<AiPdfTextService>.Instance);
        var service = new AiKnowledgeService(db, TimeProvider.System,
            new AiLearningContextService(db, TimeProvider.System), pdf);
        Assert.NotEmpty((await service.ResolveAsync(8, "Xin chào", null, default)).Text);
        Assert.NotEmpty((await service.ResolveAsync(8, "Explain present perfect", null, default)).Text);
        Assert.NotEmpty((await service.ResolveAsync(8, "Đăng ký tài khoản ở đâu?", null, default)).Text);
    }

    private sealed class NoTransactionGemini(ToeicDbContext db) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public string LastRequestBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(db.Database.CurrentTransaction);
            Requests++;
            LastRequestBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(
                    """{"candidates":[{"content":{"parts":[{"text":"Acumen là sự nhạy bén."}]}}]}""",
                    Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class StaticPlanGemini(ToeicDbContext db) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(db.Database.CurrentTransaction);
            Requests++;
            var body = JsonSerializer.Serialize(new { candidates = new[] { new { content = new {
                parts = new[] { new { text = """{"domains":["COURSES","LESSONS"],"terms":["Part 7"]}""" } }
            } } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
