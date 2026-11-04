using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Services.Ai;

public sealed class AiKnowledgeService(ToeicDbContext db, TimeProvider clock,
    AiLearningContextService learning, AiPdfTextService pdf)
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "where", "how", "why", "find", "show", "explain", "about", "lesson", "course",
        "the", "for", "and", "with", "from", "toi", "ban", "hay", "cho", "biet", "giup", "tim",
        "bai", "hoc", "khoa", "cau", "hoi", "giai", "thich", "trong", "ve", "nay", "cua", "duoc",
        "practice", "question", "part", "example", "luyen", "tap", "soan", "de"
    };

    public async Task<AiGrounding> ResolveAsync(int learnerId, string question,
        AiLearningContext? page, CancellationToken ct)
    {
        var lines = new List<string>();
        var sources = new List<AiSource>();
        var (website, websiteSources) = AiWebsiteGuide.Resolve(question);
        if (website.Length > 0) { lines.Add(website); sources.AddRange(websiteSources); }
        var pageContext = await learning.ResolveAsync(learnerId, page, ct);
        if (pageContext.Text.Length > 0) lines.Add("Ngữ cảnh trang hiện tại đã xác minh: " + pageContext.Text);
        if (pageContext.Source is not null) sources.Add(pageContext.Source);
        if (pageContext.UncheckedPractice)
            return new AiGrounding(string.Join("\n", lines), sources, true, pageContext.Part);

        var normalized = AiWebsiteGuide.Normalize(question);
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var personal = Has(normalized, "toi", "cua minh", "my ", "tiep theo", "tien do", "trang thai",
            "da mua", "da hoc", "ket qua", "phan lop", "knn", "thanh toan", "payment", "access",
            "recommend", "goi y", "luyen tap cua", "diem", "what should i study");
        if (personal)
            await AddPersonalAsync(learnerId, normalized, now, lines, sources, ct);

        var searchContent = website.Length == 0 || Has(normalized, "tim", "find", "ngu phap",
            "present", "perfect", "tu vung", "vocabulary", "nghia", "giai thich", "explain",
            "bai hoc", "bai giang", "lesson", "tai lieu", "document", "pdf");
        if (searchContent && !pageContext.UncheckedPractice)
        {
            var terms = Terms(question);
            if (terms.Length > 0)
            {
                await AddLessonsAsync(learnerId, now, terms, lines, sources, ct);
                await AddVocabularyAsync(terms, lines, sources, ct);
            }
        }
        if (page?.Type != "PRACTICE" && Has(normalized, "part", "cau hoi luyen", "practice question"))
            await AddPracticeExamplesAsync(normalized, lines, sources, ct);
        if (Has(normalized, "pdf", "tai lieu", "document") && page?.Type == "LESSON" &&
            page.CourseId is > 0 && page.StepId is > 0)
        {
            var document = await pdf.ReadFromAuthorizedStepAsync(learnerId, page.CourseId.Value,
                page.StepId.Value, ct);
            if (document is not null) lines.Add("Văn bản PDF của bài đang mở (chỉ các trang trích được): " + document);
            else lines.Add("Không trích được văn bản từ PDF của bài này; video, audio và PDF dạng ảnh chưa được AI đọc nội dung.");
        }
        if (lines.Count == 0)
            lines.Add("Không tìm thấy dữ liệu STO123 phù hợp đã xác minh. Có thể giải thích kiến thức TOEIC chung, nhưng không được khẳng định đó là nội dung khóa học.");
        return new AiGrounding(string.Join("\n", lines).Length > 6500 ?
            string.Join("\n", lines)[..6500] : string.Join("\n", lines),
            sources.DistinctBy(x => x.Url).Take(6).ToArray());
    }

    private async Task AddLessonsAsync(int learnerId, DateTime now, string[] terms,
        List<string> lines, List<AiSource> sources, CancellationToken ct)
    {
        var t1 = terms[0]; var t2 = terms.Length > 1 ? terms[1] : t1;
        var rows = await db.BuocLoTrinh.AsNoTracking().Where(s => !s.IsArchived &&
            s.MaBaiGiang != null && s.MaBaiGiangNavigation.TrangThai == "DANG_MO" &&
            !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO" &&
            CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                .Contains(s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc) &&
            (s.TieuDe.Contains(t1) || s.MaBaiGiangNavigation.TieuDe.Contains(t1) ||
             s.MaBaiGiangNavigation.NoiDungTomTat.Contains(t1) ||
             s.MaBaiGiangNavigation.NoiDung.Contains(t1) ||
             s.TieuDe.Contains(t2) || s.MaBaiGiangNavigation.TieuDe.Contains(t2) ||
             s.MaBaiGiangNavigation.NoiDungTomTat.Contains(t2) ||
             s.MaBaiGiangNavigation.NoiDung.Contains(t2)))
            .OrderBy(s => s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc)
            .ThenBy(s => s.MaLessonNavigation.MaUnitNavigation.ThuTu)
            .ThenBy(s => s.MaLessonNavigation.ThuTu).ThenBy(s => s.ThuTu)
            .Take(12).Select(s => new { s.MaBuoc, s.TieuDe,
                courseId = s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc,
                course = s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TenKhoaHoc,
                lesson = s.MaLessonNavigation.TenLesson, summary = s.MaBaiGiangNavigation.NoiDungTomTat,
                content = s.MaBaiGiangNavigation.NoiDung == null ? null :
                    s.MaBaiGiangNavigation.NoiDung.Substring(0, 1600),
                resources = s.MaBaiGiangNavigation.TaiNguyenBaiGiang.Where(r => !r.IsArchived)
                    .OrderBy(r => r.ThuTu).Take(4)
                    .Select(r => new { r.TenTaiNguyen, r.LoaiTaiNguyen }).ToList() }).ToListAsync(ct);
        foreach (var row in rows.OrderByDescending(r => Score(r.TieuDe + " " + r.lesson + " " + r.content, terms)).Take(3))
        {
            var excerpt = row.content ?? row.summary ?? "";
            lines.Add($"Bài đã xuất bản và học viên có quyền: {row.course} / {row.lesson} / {row.TieuDe}. " +
                $"Tóm tắt: {row.summary}. Nội dung: {excerpt[..Math.Min(1100, excerpt.Length)]}. " +
                "Tài nguyên: " + (row.resources.Count == 0 ? "không có" :
                    string.Join(", ", row.resources.Select(r => $"{r.TenTaiNguyen} ({r.LoaiTaiNguyen})"))) +
                ". Video, audio và ảnh chưa được trích nội dung tự động.");
            sources.Add(new AiSource($"{row.course} · {row.lesson}",
                $"/courses/{row.courseId}/steps/{row.MaBuoc}"));
        }
    }

    private async Task AddVocabularyAsync(string[] terms, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var t1 = terms[0]; var t2 = terms.Length > 1 ? terms[1] : t1;
        var rows = await db.TuVung.AsNoTracking().Where(w =>
            w.TuVungChuDe.Any(link => link.MaChuDeNavigation.TrangThai == "DANG_MO") &&
            (w.TuVung1.Contains(t1) || w.Nghia.Contains(t1) ||
             w.TuVung1.Contains(t2) || w.Nghia.Contains(t2)))
            .OrderBy(w => w.MaTuVung).Take(10)
            .Select(w => new { w.TuVung1, w.Nghia, w.PhienAm, w.LoaiTu, w.ViDu, w.DichViDu,
                topic = w.TuVungChuDe.Where(link => link.MaChuDeNavigation.TrangThai == "DANG_MO")
                    .OrderBy(link => link.MaChuDe).Select(link => new {
                        link.MaChuDe, link.MaChuDeNavigation.TenChuDe }).FirstOrDefault() }).ToListAsync(ct);
        foreach (var row in rows.OrderByDescending(r => Score(r.TuVung1 + " " + r.Nghia, terms)).Take(3))
        {
            if (row.topic is null) continue;
            lines.Add($"Từ vựng đã mở: {row.TuVung1}; nghĩa {row.Nghia}; phiên âm {row.PhienAm}; " +
                $"loại từ {row.LoaiTu}; ví dụ {row.ViDu}; dịch {row.DichViDu}. Chủ đề: {row.topic.TenChuDe}.");
            sources.Add(new AiSource($"Từ vựng · {row.topic.TenChuDe}",
                $"/vocabulary?topic={row.topic.MaChuDe}"));
        }
    }

    private async Task AddPracticeExamplesAsync(string question, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var match = Regex.Match(question, @"\bpart\s*([1-7])\b");
        if (!match.Success) return;
        var selectedPart = int.Parse(match.Groups[1].Value);
        var query = db.CauHoi.AsNoTracking().Where(q =>
            q.LoaiCauHoi == PracticeQuestionBank.Discriminator &&
            q.TrangThai == ExamCore.Published && q.MaPartNavigation.SoPart == selectedPart);
        var terms = Terms(question);
        var selected = query;
        if (terms.Length > 0)
        {
            var t1 = terms[0]; var t2 = terms.Length > 1 ? terms[1] : t1;
            selected = query.Where(q => q.NoiDung.Contains(t1) || q.NoiDung.Contains(t2) ||
                q.NhomCauHoi != null && (q.NhomCauHoi.MaNguLieuNavigation.NoiDungNguLieu.Contains(t1) ||
                    q.NhomCauHoi.MaNguLieuNavigation.NoiDungNguLieu.Contains(t2) ||
                    q.NhomCauHoi.MaNguLieuNavigation.NguLieuTaiLieu.Any(d =>
                        d.NoiDung.Contains(t1) || d.NoiDung.Contains(t2))));
        }
        async Task<List<(string text, int? resourceId, string? passage)>> ReadExamplesAsync(
            IQueryable<CauHoi> source) => (await source.OrderByDescending(q => q.MaCauHoi).Take(2)
            .Select(q => new { q.NoiDung,
                resourceId = q.NhomCauHoi == null ? (int?)null : q.NhomCauHoi.MaNguLieu,
                passage = q.NhomCauHoi == null ? null : q.NhomCauHoi.MaNguLieuNavigation.NoiDungNguLieu })
            .ToListAsync(ct)).Select(q => (q.NoiDung, q.resourceId, q.passage)).ToList();
        var examples = await ReadExamplesAsync(selected);
        if (examples.Count == 0 && terms.Length > 0) examples = await ReadExamplesAsync(query);
        var resourceIds = examples.Where(x => x.resourceId.HasValue)
            .Select(x => x.resourceId!.Value).Distinct().ToArray();
        var documents = await db.NguLieuTaiLieu.AsNoTracking()
            .Where(d => resourceIds.Contains(d.MaNguLieu) && d.LoaiTaiLieu != "IMAGE")
            .OrderBy(d => d.ThuTu).Take(3)
            .Select(d => new { d.MaNguLieu, d.LoaiTaiLieu, d.NoiDung }).ToArrayAsync(ct);
        foreach (var example in examples)
        {
            var passage = example.passage ?? "";
            var text = example.text ?? "";
            var documentText = example.resourceId is null ? "" : string.Join(" ", documents
                .Where(d => d.MaNguLieu == example.resourceId)
                .Select(d => ReadableDocument(d.LoaiTaiLieu, d.NoiDung)));
            lines.Add($"Ví dụ câu luyện Part {selectedPart} đã xuất bản, chưa kèm đáp án: " +
                $"{text[..Math.Min(300, text.Length)]}. Ngữ liệu nhóm: {passage[..Math.Min(500, passage.Length)]}. " +
                $"Tài liệu đọc: {documentText[..Math.Min(900, documentText.Length)]}.");
        }
        if (examples.Count > 0) sources.Add(new AiSource($"Luyện Part {selectedPart}", "/practice/parts"));
    }

    private async Task AddPersonalAsync(int learnerId, string question, DateTime now,
        List<string> lines, List<AiSource> sources, CancellationToken ct)
    {
        if (Has(question, "tiep theo", "tien do", "hoc gi", "study", "lesson", "lo trinh",
            "recommend", "goi y", "dau vao", "knn", "phan lop"))
        {
            var stage = await db.KetQuaPhanLopKNN.AsNoTracking()
                .Where(x => x.MaKetQuaNavigation.MaHocVien == learnerId)
                .OrderByDescending(x => x.NgayPhanLop)
                .Select(x => new { x.GiaiDoanDeXuat, x.DiemMucTieu }).FirstOrDefaultAsync(ct);
            lines.Add(stage is null ? "Học viên chưa có kết quả phân lớp KNN." :
                $"Phân lớp KNN gần nhất: giai đoạn {stage.GiaiDoanDeXuat}; mục tiêu {stage.DiemMucTieu?.ToString() ?? "chưa đặt"}.");
            if (stage is not null)
            {
                var openStages = await db.KhoaHoc.AsNoTracking().Where(c => c.TrangThai == "DANG_MO")
                    .Select(c => c.GiaiDoan).Distinct().ToArrayAsync(ct);
                var recommended = CourseRecommendation.RecommendedStages(openStages.Select(number =>
                    new KhoaHoc { GiaiDoan = number, TrangThai = "DANG_MO" }),
                    stage.GiaiDoanDeXuat, stage.DiemMucTieu);
                if (recommended.Count > 0)
                    lines.Add("Giai đoạn khóa học đề xuất hiện có: " + string.Join(", ", recommended) + ".");
                sources.Add(new AiSource("Lộ trình khóa học", "/courses"));
            }
            if (Has(question, "dau vao", "knn", "phan lop", "ket qua"))
            {
                var placement = await db.KetQuaLamBai.AsNoTracking().Where(a =>
                    a.MaHocVien == learnerId && a.LoaiBaiLam == ExamCore.Placement &&
                    (a.TrangThai == ExamCore.Submitted || a.TrangThai == ExamCore.Expired))
                    .OrderByDescending(a => a.NgayNopBai).ThenByDescending(a => a.MaKetQua)
                    .Select(a => new { a.DiemTong, a.NgayNopBai }).FirstOrDefaultAsync(ct);
                if (placement is not null)
                    lines.Add($"Bài đầu vào hoàn thành gần nhất: {placement.DiemTong?.ToString() ?? "chưa có điểm"} điểm; " +
                        $"nộp lúc {placement.NgayNopBai:dd/MM/yyyy HH:mm}.");
                sources.Add(new AiSource("Kiểm tra đầu vào", "/placement"));
            }
            if (Has(question, "tiep theo", "tien do", "hoc gi", "study", "lesson", "lo trinh",
                "recommend", "goi y"))
            {
            var next = await db.BuocLoTrinh.AsNoTracking().Where(s => !s.IsArchived &&
                !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
                s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO" &&
                CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                    .Contains(s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc) &&
                (s.MaBaiGiang != null && s.MaBaiGiangNavigation.TrangThai == "DANG_MO" ||
                 s.MaChuDe != null && s.MaChuDeNavigation.TrangThai == "DANG_MO" ||
                 s.MaBaiTap != null && db.PhienBanBaiTapKhoaHoc.Any(v =>
                     v.MaBaiTap == s.MaBaiTap && v.TrangThai == "XUAT_BAN")) &&
                !db.TienDoBuocLoTrinh.Any(p => p.MaNguoiDung == learnerId &&
                    p.MaBuoc == s.MaBuoc && p.TrangThai == "HOAN_THANH"))
                .OrderBy(s => s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.GiaiDoan)
                .ThenBy(s => s.MaLessonNavigation.MaUnitNavigation.ThuTu)
                .ThenBy(s => s.MaLessonNavigation.ThuTu).ThenBy(s => s.ThuTu)
                .Select(s => new { s.MaBuoc, s.TieuDe,
                    courseId = s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc,
                    course = s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TenKhoaHoc,
                    lesson = s.MaLessonNavigation.TenLesson }).FirstOrDefaultAsync(ct);
            if (next is null) lines.Add("Chưa xác định được hoạt động kế tiếp trong khóa đang có quyền.");
            else
            {
                lines.Add($"Hoạt động chưa hoàn thành tiếp theo trong khóa đang có quyền: {next.course} / {next.lesson} / {next.TieuDe}.");
                sources.Add(new AiSource($"Học tiếp · {next.lesson}",
                    $"/courses/{next.courseId}/steps/{next.MaBuoc}"));
            }
            }
        }
        if (Has(question, "truy cap", "da mua", "khoa hoc", "course", "access", "gia han", "het han"))
        {
            var activeIds = await CourseEntitlement.ActiveCourseIds(db, learnerId, now).ToArrayAsync(ct);
            var courses = await db.KhoaHoc.AsNoTracking().Where(c => activeIds.Contains(c.MaKhoaHoc))
                .OrderBy(c => c.MaKhoaHoc).Take(8)
                .Select(c => new { c.MaKhoaHoc, c.TenKhoaHoc }).ToListAsync(ct);
            lines.Add(courses.Count == 0 ? "Hiện chưa có khóa học đang có quyền truy cập." :
                "Khóa đang có quyền truy cập: " + string.Join("; ", courses.Select(c => c.TenKhoaHoc)) + ".");
            foreach (var course in courses.Take(3))
                sources.Add(new AiSource(course.TenKhoaHoc, $"/courses/{course.MaKhoaHoc}"));
        }
        if (Has(question, "thanh toan", "payment", "vnpay", "tra lai", "thu lai"))
        {
            var payment = await db.GiaoDichThanhToan.AsNoTracking()
                .Where(p => p.MaDangKyNavigation.MaNguoiDung == learnerId)
                .OrderByDescending(p => p.NgayTao).ThenByDescending(p => p.MaGiaoDich)
                .Select(p => new { p.MaGiaoDich, p.TrangThai, p.NgayTao,
                    enrollment = p.MaDangKyNavigation.TrangThai }).FirstOrDefaultAsync(ct);
            if (payment is null) lines.Add("Học viên chưa có giao dịch thanh toán được ghi nhận.");
            else
            {
                var retryAt = new DateTimeOffset(DateTime.SpecifyKind(payment.NgayTao,
                    DateTimeKind.Unspecified), TimeSpan.FromHours(7))
                    .AddMinutes(Payment.PaymentExpiry.PendingMinutes);
                lines.Add($"Giao dịch gần nhất #{payment.MaGiaoDich}: {payment.TrangThai}; " +
                    $"đăng ký: {payment.enrollment}. Nếu còn CHO_XU_LY, trạng thái tự cập nhật; " +
                    $"có thể thử lại sau {retryAt:dd/MM/yyyy HH:mm} khi giao dịch cũ đã quá hạn và chưa thanh toán. " +
                    "Nếu đã bị trừ tiền mà chưa có quyền học, liên hệ hỗ trợ để đối soát.");
                sources.Add(new AiSource($"Thanh toán #{payment.MaGiaoDich}",
                    $"/courses?paymentId={payment.MaGiaoDich}"));
            }
        }
        if (Has(question, "tu vung", "vocabulary", "tien do tu", "da hoc tu"))
        {
            var progress = await db.TienDoTuVung.AsNoTracking().Where(p => p.MaNguoiDung == learnerId)
                .GroupBy(p => p.TrangThai).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);
            lines.Add("Tiến độ từ vựng: " + (progress.Count == 0 ? "chưa có từ được ghi nhận" :
                string.Join("; ", progress.Select(p => $"{p.status}: {p.count}"))) + ".");
            sources.Add(new AiSource("Học từ vựng", "/vocabulary"));
        }
        if (Has(question, "luyen part", "thong ke", "do chinh xac", "accuracy"))
        {
            var stats = await db.ThongKeLuyenPart.AsNoTracking().Where(p => p.MaHocVien == learnerId)
                .OrderBy(p => p.MaPart).Select(p => new { p.MaPart, p.SoLanLuyen,
                    p.TongCauDaLam, p.TongCauDung, p.SoCauLanCuoi, p.SoDungLanCuoi }).ToListAsync(ct);
            lines.Add("Thống kê luyện Part: " + (stats.Count == 0 ? "chưa có" : string.Join("; ",
                stats.Select(p => $"Part {p.MaPart}: {p.SoLanLuyen} lần, {p.TongCauDung}/{p.TongCauDaLam} đúng tích lũy, lần gần nhất {p.SoDungLanCuoi}/{p.SoCauLanCuoi}"))));
            sources.Add(new AiSource("Luyện tập theo Part", "/practice/parts"));
        }
        if (Has(question, "thi thu", "mock", "diem thi", "ket qua thi"))
        {
            var results = await db.KetQuaLamBai.AsNoTracking().Where(a => a.MaHocVien == learnerId &&
                a.LoaiBaiLam == ExamCore.Mock &&
                (a.TrangThai == ExamCore.Submitted || a.TrangThai == ExamCore.Expired))
                .OrderByDescending(a => a.NgayNopBai).ThenByDescending(a => a.MaKetQua)
                .Take(20).Select(a => new { a.MaDeThi, a.MaKetQua, a.DiemTong,
                    name = a.MaDeThiNavigation.TenDe }).ToListAsync(ct);
            var latest = results.DistinctBy(r => r.MaDeThi).Take(3).ToArray();
            lines.Add(latest.Length == 0 ? "Chưa có kết quả thi thử hoàn thành." :
                "Kết quả thi thử gần nhất từng đề: " + string.Join("; ", latest.Select(r =>
                    $"{r.name}: {r.DiemTong?.ToString() ?? "chưa có điểm"} điểm")) + ".");
            sources.Add(new AiSource("Lịch sử thi thử", "/mock-test/history"));
        }
    }

    private static string[] Terms(string question) => Regex.Matches(question, @"[\p{L}\p{N}]{2,}")
        .Select(m => m.Value).Where(word => word.Length >= 3 &&
            !StopWords.Contains(AiWebsiteGuide.Normalize(word)))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length).Take(2).ToArray();
    private static int Score(string text, string[] terms) => terms.Count(t =>
        text.Contains(t, StringComparison.OrdinalIgnoreCase));
    private static bool Has(string text, params string[] values) => values.Any(text.Contains);

    private static string ReadableDocument(string type, string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return "";
        if (type == "TEXT") return content;
        if (type is not ("EMAIL" or "TABLE" or "CHAT" or "FORM")) return "";
        try
        {
            using var parsed = JsonDocument.Parse(content);
            var words = new List<string>();
            static void Collect(JsonElement element, List<string> values)
            {
                if (values.Count >= 30) return;
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
                }
                else if (element.ValueKind == JsonValueKind.Array)
                    foreach (var child in element.EnumerateArray()) Collect(child, values);
                else if (element.ValueKind == JsonValueKind.Object)
                    foreach (var property in element.EnumerateObject())
                        if (property.Name is not ("answer" or "correct" or "explanation"))
                            Collect(property.Value, values);
            }
            Collect(parsed.RootElement, words);
            return string.Join(" · ", words);
        }
        catch (JsonException) { return ""; }
    }
}
