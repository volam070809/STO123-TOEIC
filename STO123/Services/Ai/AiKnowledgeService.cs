using System.Text.RegularExpressions;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using STO123.Models;
using STO123.Services;
using STO123.Services.Exam;

namespace STO123.Services.Ai;

public sealed class AiKnowledgeService(ToeicDbContext db, TimeProvider clock,
    AiLearningContextService learning, AiPdfTextService pdf,
    IMemoryCache? cache = null, ContentRevisionStore? revisions = null,
    ILogger<AiKnowledgeService>? logger = null,
    Func<CancellationToken, Task<long>>? revisionVersion = null,
    AiToeicService? intentModel = null, PracticePartService? practiceService = null)
{
    private sealed record PublicGrounding(string[] Lines, AiSource[] Sources);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "where", "how", "why", "find", "show", "explain", "about", "lesson", "course",
        "the", "for", "and", "with", "from", "toi", "ban", "hay", "cho", "biet", "giup", "tim",
        "bai", "hoc", "khoa", "cau", "hoi", "giai", "thich", "trong", "ve", "nay", "cua", "duoc",
        "practice", "question", "part", "example", "luyen", "tap", "soan", "de",
        "vocabulary", "vung", "nghia", "meaning", "word", "danh", "sach", "liet",
        "nhung", "cac"
    };

    public async Task<AiGrounding> ResolveAsync(int learnerId, string question,
        AiLearningContext? page, CancellationToken ct,
        IReadOnlyList<AiPromptMessage>? history = null, string? route = null,
        AiRetrievalPlan? forcedPlan = null, bool skipSemanticPlan = false)
    {
        var started = Stopwatch.GetTimestamp();
        var contextMs = 0.0; var personalMs = 0.0; var lessonsMs = 0.0;
        var vocabularyMs = 0.0; var practiceMs = 0.0; var pdfMs = 0.0;
        var lines = new List<string>();
        var sources = new List<AiSource>();
        var (website, websiteSources) = AiWebsiteGuide.Resolve(question);
        if (website.Length > 0) { lines.Add(website); sources.AddRange(websiteSources); }
        var currentFeature = AiWebsiteGuide.ForRoute(route);
        if (currentFeature is not null)
            lines.Add($"Route trình duyệt báo thuộc chức năng {currentFeature.Label}: {currentFeature.Workflow}. " +
                "Route này không chứng minh học viên có quyền xem dữ liệu của trang; chỉ dữ liệu SQL đã kiểm tra quyền mới được dùng làm sự thật cá nhân.");
        var websiteNavigation = AiQuestionIntent.IsWebsiteNavigation(question);
        if (AiQuestionIntent.WantsWebsiteCatalog(question) ||
            websiteNavigation && website.Length == 0)
            lines.Add("Chức năng website đã xác minh (chỉ hướng dẫn chức năng có trong danh sách):\n" +
                AiWebsiteGuide.VerifiedCatalog(question));
        var priorQuestion = AiQuestionIntent.IsFollowUp(question) ?
            history?.LastOrDefault(message => message.Role == "USER")?.Text : null;
        var effectiveQuestion = priorQuestion is null ? question : priorQuestion + " " + question;
        var phase = Stopwatch.GetTimestamp();
        AiContextResolution pageContext;
        try { pageContext = await learning.ResolveAsync(learnerId, page, ct); }
        catch (AiChatException error) when (error.Code == "AI_CONTEXT_UNAVAILABLE" &&
            page?.Type is "PRACTICE" or "COURSE_EXERCISE" &&
            !AiQuestionIntent.ReferencesCurrentQuestion(question))
        {
            // The page may still hold an old question after a check, navigation, or expiry.
            // An unrelated chat question uses no question data and remains available.
            pageContext = new("");
        }
        contextMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
        var pageOnly = pageContext.Text;
        if (pageContext.Text.Length > 0) lines.Add("Ngữ cảnh trang hiện tại đã xác minh: " + pageContext.Text);
        if (pageContext.Source is not null) sources.Add(pageContext.Source);
        if (pageContext.UncheckedPractice && AiQuestionIntent.IsProtectedAnswerRequest(question))
            return new AiGrounding(string.Join("\n", lines), sources, true, pageContext.Part,
                PageContextText: pageOnly);

        AiRetrievalPlan? plan = forcedPlan ?? AiQuestionIntent.FastPlan(effectiveQuestion);
        var planMs = 0.0;
        if (!skipSemanticPlan && forcedPlan is null && plan is null && intentModel is not null && AiQuestionIntent.NeedsSemanticPlan(question,
            website.Length > 0, currentFeature is not null))
        {
            phase = Stopwatch.GetTimestamp();
            var planKey = history is null || history.Count == 0 ?
                "ai-plan:" + AiWebsiteGuide.Normalize(question) + "|" + currentFeature?.Route : null;
            if (planKey is not null && cache?.TryGetValue<AiRetrievalPlan>(planKey, out plan) == true)
            { }
            else
            {
                plan = await intentModel.PlanAsync(question, history, currentFeature?.Label, ct);
                if (planKey is not null && plan is not null)
                    cache?.Set(planKey, plan, TimeSpan.FromMinutes(15));
            }
            planMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
        }
        if (plan?.Includes("WEBSITE") == true && website.Length == 0 && !websiteNavigation &&
            !AiQuestionIntent.WantsWebsiteCatalog(question))
            lines.Add("Chức năng website đã xác minh:\n" + AiWebsiteGuide.VerifiedCatalog(question));
        var normalized = AiWebsiteGuide.Normalize(effectiveQuestion);
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(7)).DateTime;
        var practiceHubQuestion = currentFeature?.Route == "/practice" &&
            Has(normalized, "luyen", "tap", "thong ke", "ket qua", "trang nay", "co gi");
        var personal = Has(normalized, "toi", "cua minh", "my ", "tiep theo", "tien do", "trang thai",
            "da mua", "da hoc", "ket qua", "phan lop", "knn", "thanh toan", "payment", "access",
            "recommend", "goi y", "luyen tap cua", "diem", "what should i study");
        if (!AiQuestionIntent.IsPublicCountQuestion(question) &&
            (personal || plan?.Includes("PROGRESS") == true || plan?.Includes("PRACTICE_STATS") == true ||
            plan?.Includes("EXAM_RESULTS") == true || plan?.Includes("PAYMENT") == true ||
            plan?.Includes("COURSES") == true))
        {
            phase = Stopwatch.GetTimestamp();
            await AddPersonalAsync(learnerId, normalized, now, lines, sources, ct, plan);
            personalMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
        }
        if (plan?.Includes("PROFILE") == true)
            await AddProfileAsync(learnerId, normalized, lines, sources, ct);
        if (practiceHubQuestion && plan?.Includes("PRACTICE_STATS") != true &&
            !Has(normalized, "luyen part", "thong ke", "do chinh xac", "accuracy", "yeu part"))
            await AddPartStatsAsync(learnerId, lines, sources, ct);

        var exactFacts = new List<string>();
        if ((forcedPlan is null || forcedPlan.Includes("TOPICS") || forcedPlan.Includes("VOCABULARY")) &&
            (AiQuestionIntent.WantsVocabularyCatalog(effectiveQuestion) || plan?.Includes("TOPICS") == true ||
            plan?.Includes("VOCABULARY") == true && AiQuestionIntent.IsPublicCountQuestion(question) ||
            practiceHubQuestion))
        {
            var (topicCount, wordCount, namedTopic, namedWordCount) =
                await AddVocabularyCatalogAsync(effectiveQuestion, lines, sources, ct);
            if (AiQuestionIntent.IsPublicCountQuestion(question))
            {
                var asksTopics = Has(normalized, "chu de", "topic");
                var asksWordsInTopic = asksTopics && Has(normalized, "bao nhieu tu", "so tu",
                    "how many words", "count words");
                var fact = asksWordsInTopic ? namedTopic is null ? null :
                    $"Chủ đề {namedTopic} hiện có {namedWordCount} từ vựng." :
                    asksTopics ? $"Hiện có {topicCount} chủ đề từ vựng đang mở cho học viên." :
                    $"Hiện có {wordCount} từ vựng riêng biệt thuộc các chủ đề đang mở.";
                if (fact is not null) exactFacts.Add(fact);
            }
        }
        if ((forcedPlan is null || forcedPlan.Includes("COURSES")) &&
            (AiQuestionIntent.WantsCourseCatalog(effectiveQuestion) || plan?.Includes("COURSES") == true))
        {
            var courseCount = await AddCourseCatalogAsync(lines, sources, ct);
            if (AiQuestionIntent.IsPublicCountQuestion(question))
                exactFacts.Add($"Hiện có {courseCount} khóa học đang mở trên STO123.");
        }
        if (Has(normalized, "luyen tu", "on tu", "ket qua tu", "trac nghiem tu") ||
            currentFeature?.Route is "/practice/vocabulary" or "/practice/vocabulary/history")
            await AddVocabularyPracticeHistoryAsync(learnerId, lines, sources, ct);
        var searchLessons = !websiteNavigation && (forcedPlan is null || forcedPlan.Includes("LESSONS")) &&
            (AiQuestionIntent.WantsLessonContent(effectiveQuestion) ||
            plan?.Includes("LESSONS") == true);
        var searchVocabulary = !websiteNavigation && (forcedPlan is null || forcedPlan.Includes("VOCABULARY")) &&
            !AiQuestionIntent.IsPublicCountQuestion(question) &&
            (AiQuestionIntent.WantsVocabularyContent(effectiveQuestion) || plan?.Includes("VOCABULARY") == true);
        if (searchLessons && AiQuestionIntent.WantsLessonList(effectiveQuestion))
            await AddAuthorizedLessonCatalogAsync(learnerId, now, lines, sources, ct);
        if (searchLessons || searchVocabulary)
        {
            var part = Regex.Match(normalized, @"\bpart\s*([1-7])\b");
            var terms = (plan?.Terms ?? []).Concat(part.Success ?
                    new[] { "Part " + part.Groups[1].Value } : Array.Empty<string>())
                .Concat(Terms(effectiveQuestion))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            if (terms.Length > 0)
            {
                if (searchLessons && !AiQuestionIntent.WantsLessonList(effectiveQuestion))
                { phase = Stopwatch.GetTimestamp(); await AddLessonsAsync(learnerId, now, terms, lines, sources, ct);
                  lessonsMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds; }
                if (searchVocabulary)
                { phase = Stopwatch.GetTimestamp(); await AddCachedPublicAsync("vocabulary:" + string.Join('|', terms),
                      (entries, links) => AddVocabularyAsync(terms, entries, links, ct), lines, sources, ct);
                  vocabularyMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds; }
            }
        }
        if (AiQuestionIntent.IsFollowUp(question) &&
            Has(AiWebsiteGuide.Normalize(question), "tai lieu", "pdf", "video", "bai do", "noi dung") &&
            history is not null)
            await AddPreviousLessonResourcesAsync(learnerId, now, history, lines, sources, ct);
        if (!websiteNavigation && page?.Type != "PRACTICE" && AiQuestionIntent.WantsPracticeExamples(question))
        {
            phase = Stopwatch.GetTimestamp();
            await AddCachedPublicAsync("practice:" + normalized,
                (entries, links) => AddPracticeExamplesAsync(normalized, entries, links, ct), lines, sources, ct);
            practiceMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
        }
        if (practiceService is not null && AiQuestionIntent.WantsPracticeAvailability(question))
            await AddPracticeAvailabilityAsync(question, lines, sources, ct);
        if (Has(normalized, "thi thu", "mock", "de thi", "ets", "de soan san") ||
            plan?.Includes("EXAM_RESULTS") == true ||
            currentFeature?.Route == "/mock-test")
            await AddMockExamsAsync(learnerId, lines, sources, ct);
        if (!websiteNavigation && Has(normalized, "pdf", "tai lieu", "document") && page?.Type == "LESSON" &&
            page.CourseId is > 0 && page.StepId is > 0)
        {
            phase = Stopwatch.GetTimestamp();
            var document = await pdf.ReadFromAuthorizedStepAsync(learnerId, page.CourseId.Value,
                page.StepId.Value, ct);
            pdfMs = Stopwatch.GetElapsedTime(phase).TotalMilliseconds;
            if (document is not null)
            {
                var pdfLine = "Văn bản PDF của bài đang mở (chỉ các trang trích được): " + document;
                lines.Add(pdfLine);
                pageOnly = string.Join("\n", new[] { pageOnly, pdfLine }.Where(x => x.Length > 0));
            }
            else lines.Add("Không trích được văn bản từ PDF của bài này; video, audio và PDF dạng ảnh chưa được AI đọc nội dung.");
        }
        if (lines.Count == 0)
            lines.Add("Không tìm thấy dữ liệu STO123 phù hợp đã xác minh. Có thể giải thích kiến thức TOEIC chung, nhưng không được khẳng định đó là nội dung khóa học.");
        logger?.LogInformation("AI retrieval timing intent_ms={IntentMs:F1} context_ms={ContextMs:F1} personal_ms={PersonalMs:F1} lessons_ms={LessonsMs:F1} vocabulary_ms={VocabularyMs:F1} practice_ms={PracticeMs:F1} pdf_ms={PdfMs:F1} total_ms={TotalMs:F1}",
            planMs, contextMs, personalMs, lessonsMs, vocabularyMs, practiceMs, pdfMs,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        var groundingText = string.Join("\n", lines);
        return new AiGrounding(groundingText.Length > 16000 ?
            groundingText[..16000] + "\n[Dữ liệu còn lại chưa được đưa vào ngữ cảnh; không khẳng định đã liệt kê đầy đủ.]" : groundingText,
            sources.DistinctBy(x => x.Url).Take(6).ToArray(), pageContext.UncheckedPractice,
            pageContext.Part, exactFacts.Count == 0 ? null : string.Join("\n", exactFacts.Distinct()), pageOnly);
    }

    private async Task AddCachedPublicAsync(string key,
        Func<List<string>, List<AiSource>, Task> load, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        if (cache is null || revisions is null && revisionVersion is null)
        { await load(lines, sources); return; }
        long version;
        try { version = revisionVersion is null ? (await revisions!.ReadAsync(null, true, ct)).Version :
            await revisionVersion(ct); }
        catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException)
        {
            logger?.LogWarning(error, "AI public-content revision unavailable; reading current published SQL.");
            await load(lines, sources); return;
        }
        var cacheKey = "ai-public:" + version + ":" + key;
        if (!cache.TryGetValue<PublicGrounding>(cacheKey, out var entry))
        {
            var cachedLines = new List<string>(); var cachedSources = new List<AiSource>();
            await load(cachedLines, cachedSources);
            entry = new PublicGrounding(cachedLines.ToArray(), cachedSources.ToArray());
            cache.Set(cacheKey, entry, TimeSpan.FromMinutes(10));
        }
        lines.AddRange(entry!.Lines); sources.AddRange(entry.Sources);
    }

    private async Task<(int TopicCount, int WordCount, string? NamedTopic, int? NamedWordCount)>
        AddVocabularyCatalogAsync(string question, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var openTopics = db.ChuDe.AsNoTracking().Where(topic => topic.TrangThai == "DANG_MO");
        var topicCount = await openTopics.CountAsync(ct);
        var wordCount = await db.TuVung.AsNoTracking().CountAsync(word =>
            word.TuVungChuDe.Any(link => link.MaChuDeNavigation.TrangThai == "DANG_MO"), ct);
        lines.Add($"Số liệu SQL hiện tại cho học viên: {topicCount} chủ đề từ vựng đang mở; " +
            $"{wordCount} từ vựng riêng biệt nằm trong ít nhất một chủ đề đang mở. " +
            "Một từ liên kết nhiều chủ đề chỉ tính một lần ở tổng số từ.");
        sources.Add(new AiSource("Chủ đề từ vựng", "/vocabulary"));
        if (!AiQuestionIntent.WantsTopicList(question)) return (topicCount, wordCount, null, null);
        var topics = await openTopics.OrderBy(topic => topic.TenChuDe).ThenBy(topic => topic.MaChuDe)
            .Select(topic => new { topic.MaChuDe, topic.TenChuDe, topic.MoTa,
                wordCount = topic.TuVungChuDe.Count }).ToListAsync(ct);
        var normalizedQuestion = AiWebsiteGuide.Normalize(question);
        var matched = topics.Where(topic => Regex.IsMatch(normalizedQuestion,
            $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(AiWebsiteGuide.Normalize(topic.TenChuDe))}(?![\p{{L}}\p{{N}}])"))
            .OrderByDescending(topic => topic.TenChuDe.Length)
            .FirstOrDefault();
        if (matched is not null)
            lines.Add($"Chủ đề được hỏi: {matched.TenChuDe}; {matched.wordCount} từ vựng; mô tả: {matched.MoTa}.");
        if (matched is not null && AiQuestionIntent.WantsWordsInTopic(question))
        {
            var words = await db.TuVungChuDe.AsNoTracking()
                .Where(link => link.MaChuDe == matched.MaChuDe)
                .OrderBy(link => link.MaTuVungNavigation.TuVung1)
                .ThenBy(link => link.MaTuVung)
                .Select(link => new { link.MaTuVungNavigation.TuVung1,
                    link.MaTuVungNavigation.Nghia }).ToListAsync(ct);
            lines.Add($"Toàn bộ {words.Count} từ thuộc chủ đề {matched.TenChuDe}: " +
                string.Join("; ", words.Select(word => $"{word.TuVung1} ({word.Nghia})")) + ".");
        }
        var displayed = topics.ToArray();
        lines.Add("Chủ đề đang mở" + (topicCount > displayed.Length ? $" (hiển thị {displayed.Length}/{topicCount})" : "") +
            ": " + (displayed.Length == 0 ? "không có" : string.Join("; ", displayed.Select(topic =>
                $"{topic.TenChuDe}: {topic.wordCount} từ"))) + ".");
        foreach (var topic in (matched is null ? displayed.Take(4) : new[] { matched }))
            sources.Add(new AiSource(topic.TenChuDe, $"/vocabulary?topic={topic.MaChuDe}"));
        return (topicCount, wordCount, matched?.TenChuDe, matched?.wordCount);
    }

    private async Task<int> AddCourseCatalogAsync(List<string> lines, List<AiSource> sources,
        CancellationToken ct)
    {
        var published = db.KhoaHoc.AsNoTracking().Where(course => course.TrangThai == "DANG_MO");
        var count = await published.CountAsync(ct);
        var courses = await published.OrderBy(course => course.GiaiDoan)
            .ThenBy(course => course.MaKhoaHoc)
            .Select(course => new { course.MaKhoaHoc, course.TenKhoaHoc, course.GiaiDoan })
            .ToListAsync(ct);
        lines.Add($"Khóa học đang mở theo SQL: {count}. " +
            (courses.Count == 0 ? "Hiện không có khóa đang mở." :
                "Danh sách" + (count > courses.Count ? $" (hiển thị {courses.Count}/{count})" : "") +
                ": " + string.Join("; ", courses.Select(course =>
                    $"{course.TenKhoaHoc} (giai đoạn {course.GiaiDoan})")) + "."));
        foreach (var course in courses.Take(3))
            sources.Add(new AiSource(course.TenKhoaHoc, $"/courses/{course.MaKhoaHoc}"));
        return count;
    }

    private async Task AddPreviousLessonResourcesAsync(int learnerId, DateTime now,
        IReadOnlyList<AiPromptMessage> history, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var match = history.Where(message => message.Role == "MODEL")
            .Reverse().Select(message => Regex.Match(message.Text,
                @"/courses/([1-9]\d*)/steps/([1-9]\d*)"))
            .FirstOrDefault(found => found.Success);
        if (match is null || !int.TryParse(match.Groups[1].Value, out var courseId) ||
            !int.TryParse(match.Groups[2].Value, out var stepId)) return;
        var step = await db.BuocLoTrinh.AsNoTracking().Where(s => s.MaBuoc == stepId &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc == courseId && !s.IsArchived &&
            !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO" &&
            CourseEntitlement.ActiveCourseIds(db, learnerId, now).Contains(courseId) &&
            s.MaBaiGiang != null && s.MaBaiGiangNavigation.TrangThai == "DANG_MO")
            .Select(s => new { s.TieuDe, s.MaBuoc,
                resources = s.MaBaiGiangNavigation.TaiNguyenBaiGiang
                    .Where(resource => !resource.IsArchived).OrderBy(resource => resource.ThuTu)
                    .Take(12).Select(resource => new { resource.TenTaiNguyen,
                        resource.LoaiTaiNguyen }).ToList() }).FirstOrDefaultAsync(ct);
        if (step is null) return;
        lines.Add($"Bài học đã đề xuất trước đó (đã xác minh lại quyền): {step.TieuDe}. " +
            (step.resources.Count == 0 ? "Chưa có tài nguyên đang mở." :
                "Tài nguyên: " + string.Join("; ", step.resources.Select(resource =>
                    $"{resource.TenTaiNguyen} ({resource.LoaiTaiNguyen})")) + "."));
        sources.Add(new AiSource(step.TieuDe, $"/courses/{courseId}/steps/{step.MaBuoc}"));
    }

    private async Task AddPracticeAvailabilityAsync(string question, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToElement(await practiceService!.AvailabilityAsync(ct));
        var requested = Regex.Match(AiWebsiteGuide.Normalize(question), @"\bpart\s*([1-7])\b");
        var selectedPart = requested.Success ? int.Parse(requested.Groups[1].Value) : 0;
        var summaries = new List<string>();
        foreach (var part in payload.GetProperty("parts").EnumerateArray())
        {
            var number = part.GetProperty("part").GetInt32();
            if (selectedPart != 0 && number != selectedPart) continue;
            foreach (var difficulty in part.GetProperty("difficulties").EnumerateArray())
            {
                var level = difficulty.GetProperty("difficulty").GetInt32();
                var count = difficulty.GetProperty("validUnitCount").GetInt32();
                var unit = difficulty.GetProperty("unitType").GetString() == "GROUP" ? "nhóm" : "câu";
                var presets = string.Join(", ", difficulty.GetProperty("presets").EnumerateArray()
                    .Select(preset => $"{preset.GetProperty("preset").GetString()} " +
                        (preset.GetProperty("available").GetBoolean() ? "đủ" : "chưa đủ")));
                summaries.Add($"Part {number} độ khó {level}: {count} {unit} hợp lệ; {presets}");
            }
        }
        lines.Add("Khả dụng thực tế theo cùng bộ kiểm tra nguồn câu hỏi của trang Luyện theo Part: " +
            string.Join("; ", summaries.Take(selectedPart == 0 ? 21 : 3)) + ".");
        sources.Add(new AiSource("Luyện tập TOEIC theo Part", "/practice/parts"));
    }

    private async Task AddMockExamsAsync(int learnerId, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var visible = MockExamEligibility.Visible(db.DeThi.AsNoTracking(), 0);
        var count = await visible.CountAsync(ct);
        var exams = await visible.OrderBy(exam => exam.MaDeThi).Take(20)
            .Select(exam => new { exam.MaDeThi, exam.TenDe, exam.ThoiGianLamBai,
                active = exam.KetQuaLamBai.Any(result => result.MaHocVien == learnerId &&
                    result.LoaiBaiLam == ExamCore.Mock &&
                    (result.TrangThai == ExamCore.Active || result.TrangThai == "BO_DO")),
                completed = exam.KetQuaLamBai.Where(result => result.MaHocVien == learnerId &&
                    result.LoaiBaiLam == ExamCore.Mock &&
                    (result.TrangThai == ExamCore.Submitted || result.TrangThai == ExamCore.Expired))
                    .OrderByDescending(result => result.NgayNopBai)
                    .ThenByDescending(result => result.MaKetQua)
                    .Select(result => new { result.DiemTong, result.NgayNopBai }).FirstOrDefault() })
            .ToListAsync(ct);
        lines.Add($"Đề thi thử soạn sẵn đang mở theo SQL: {count}. " +
            (exams.Count == 0 ? "Chưa có đề đang mở." :
                "Đề" + (count > exams.Count ? $" (hiển thị {exams.Count}/{count})" : "") + ": " +
                string.Join("; ", exams.Select(exam => $"{exam.TenDe} ({exam.ThoiGianLamBai} phút; " +
                    (exam.active ? "đang làm" : exam.completed is not null ?
                        $"kết quả gần nhất {exam.completed.DiemTong?.ToString() ?? "chưa có điểm"} điểm" :
                        "chưa bắt đầu") + ")")) + "."));
        sources.Add(new AiSource("Thi thử TOEIC", "/mock-test"));
        if (exams.Any(exam => exam.completed is not null))
            sources.Add(new AiSource("Lịch sử thi thử", "/mock-test/history"));
    }

    private async Task AddVocabularyPracticeHistoryAsync(int learnerId, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var completed = db.LanLuyenTuVung.AsNoTracking().Where(attempt =>
            attempt.MaNguoiDung == learnerId && attempt.NgayNopBai != null);
        var total = await completed.CountAsync(ct);
        var recent = await completed.OrderByDescending(attempt => attempt.NgayNopBai)
            .ThenByDescending(attempt => attempt.MaLanLuyen).Take(5)
            .Select(attempt => new { attempt.MaLanLuyen, attempt.NgayNopBai,
                topic = attempt.MaChuDeNavigation.TenChuDe,
                questions = attempt.ChiTietLuyenTuVung.Count,
                correct = attempt.ChiTietLuyenTuVung.Count(answer => answer.LaDung == true) })
            .ToListAsync(ct);
        lines.Add($"Lượt luyện từ vựng đã nộp của chính học viên: {total}. " +
            (recent.Count == 0 ? "Chưa có kết quả được lưu." :
                "Gần nhất: " + string.Join("; ", recent.Select(attempt =>
                    $"{attempt.topic}: {attempt.correct}/{attempt.questions} đúng, " +
                    $"nộp {attempt.NgayNopBai:dd/MM/yyyy HH:mm}")) + "."));
        sources.Add(new AiSource("Luyện từ vựng", "/practice/vocabulary"));
        if (recent.Count > 0)
            sources.Add(new AiSource("Lịch sử luyện từ vựng", "/practice/vocabulary/history"));
    }

    private async Task AddAuthorizedLessonCatalogAsync(int learnerId, DateTime now,
        List<string> lines, List<AiSource> sources, CancellationToken ct)
    {
        var rows = await db.BuocLoTrinh.AsNoTracking().Where(step => !step.IsArchived &&
            step.MaBaiGiang != null && step.MaBaiGiangNavigation.TrangThai == "DANG_MO" &&
            !step.MaLessonNavigation.IsArchived && !step.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            step.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO" &&
            CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                .Contains(step.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc))
            .OrderBy(step => step.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc)
            .ThenBy(step => step.MaLessonNavigation.MaUnitNavigation.ThuTu)
            .ThenBy(step => step.MaLessonNavigation.ThuTu).ThenBy(step => step.ThuTu)
            .Select(step => new { step.MaBuoc, step.TieuDe,
                courseId = step.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc,
                course = step.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TenKhoaHoc,
                lesson = step.MaLessonNavigation.TenLesson }).ToListAsync(ct);
        lines.Add($"Toàn bộ {rows.Count} hoạt động bài giảng đã xuất bản trong khóa học viên đang có quyền: " +
            (rows.Count == 0 ? "không có" : string.Join("; ", rows.Select(row =>
                $"{row.course} / {row.lesson} / {row.TieuDe}"))) + ".");
        foreach (var row in rows.Take(5))
            sources.Add(new AiSource($"{row.course} · {row.lesson}",
                $"/courses/{row.courseId}/steps/{row.MaBuoc}"));
    }

    private async Task AddLessonsAsync(int learnerId, DateTime now, string[] terms,
        List<string> lines, List<AiSource> sources, CancellationToken ct)
    {
        var t1 = terms[0]; var t2 = terms.Length > 1 ? terms[1] : t1;
        var published = db.BuocLoTrinh.AsNoTracking().Where(s => !s.IsArchived &&
            s.MaBaiGiang != null && s.MaBaiGiangNavigation.TrangThai == "DANG_MO" &&
            !s.MaLessonNavigation.IsArchived && !s.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            s.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO" &&
            CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                .Contains(s.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc));
        var project = (IQueryable<BuocLoTrinh> query) => query
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
                    .Select(r => new { r.TenTaiNguyen, r.LoaiTaiNguyen }).ToList() });
        var titleMatches = db.Database.IsSqlServer()
            ? published.Where(s =>
                EF.Functions.Collate(s.TieuDe, "Latin1_General_100_CI_AI").Contains(t1) ||
                EF.Functions.Collate(s.MaBaiGiangNavigation.TieuDe, "Latin1_General_100_CI_AI").Contains(t1) ||
                EF.Functions.Collate(s.MaBaiGiangNavigation.NoiDungTomTat!, "Latin1_General_100_CI_AI").Contains(t1) ||
                EF.Functions.Collate(s.TieuDe, "Latin1_General_100_CI_AI").Contains(t2) ||
                EF.Functions.Collate(s.MaBaiGiangNavigation.TieuDe, "Latin1_General_100_CI_AI").Contains(t2) ||
                EF.Functions.Collate(s.MaBaiGiangNavigation.NoiDungTomTat!, "Latin1_General_100_CI_AI").Contains(t2))
            : published.Where(s => s.TieuDe.Contains(t1) ||
                s.MaBaiGiangNavigation.TieuDe.Contains(t1) ||
                s.MaBaiGiangNavigation.NoiDungTomTat.Contains(t1) ||
                s.TieuDe.Contains(t2) || s.MaBaiGiangNavigation.TieuDe.Contains(t2) ||
                s.MaBaiGiangNavigation.NoiDungTomTat.Contains(t2));
        var rows = await project(titleMatches).ToListAsync(ct);
        if (rows.Count == 0 || terms.Length > 1 && rows.All(row =>
            Score(row.TieuDe + " " + row.lesson + " " + row.summary, terms) < terms.Length))
        {
            var bodyQuery = db.Database.IsSqlServer()
                ? published.Where(s =>
                    EF.Functions.Collate(s.MaBaiGiangNavigation.NoiDung!, "Latin1_General_100_CI_AI").Contains(t1) ||
                    EF.Functions.Collate(s.MaBaiGiangNavigation.NoiDung!, "Latin1_General_100_CI_AI").Contains(t2))
                : published.Where(s => s.MaBaiGiangNavigation.NoiDung.Contains(t1) ||
                    s.MaBaiGiangNavigation.NoiDung.Contains(t2));
            var bodyMatches = await project(bodyQuery).ToListAsync(ct);
            rows = rows.Concat(bodyMatches).DistinctBy(row => row.MaBuoc).Take(12).ToList();
        }
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
        var visibleWords = db.TuVung.AsNoTracking().Where(w =>
            w.TuVungChuDe.Any(link => link.MaChuDeNavigation.TrangThai == "DANG_MO"));
        var matchingWords = db.Database.IsSqlServer()
            ? visibleWords.Where(w =>
                EF.Functions.Collate(w.TuVung1, "Latin1_General_100_CI_AI").Contains(t1) ||
                EF.Functions.Collate(w.Nghia, "Latin1_General_100_CI_AI").Contains(t1) ||
                EF.Functions.Collate(w.TuVung1, "Latin1_General_100_CI_AI").Contains(t2) ||
                EF.Functions.Collate(w.Nghia, "Latin1_General_100_CI_AI").Contains(t2))
            : visibleWords.Where(w => w.TuVung1.Contains(t1) || w.Nghia.Contains(t1) ||
                w.TuVung1.Contains(t2) || w.Nghia.Contains(t2));
        var rows = await matchingWords
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
        List<string> lines, List<AiSource> sources, CancellationToken ct,
        AiRetrievalPlan? plan = null)
    {
        if (Has(question, "tien do", "da hoc", "chua hoc", "hoan thanh", "con bai nao") ||
            plan?.Includes("PROGRESS") == true)
            await AddCourseProgressAsync(learnerId, now, lines, sources, ct);
        if (Has(question, "tiep theo", "tien do", "hoc gi", "study", "lesson", "lo trinh",
            "recommend", "goi y", "dau vao", "knn", "phan lop") ||
            plan?.Includes("PROGRESS") == true || plan?.Includes("EXAM_RESULTS") == true ||
            plan?.Includes("LESSONS") == true)
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
            if (Has(question, "dau vao", "knn", "phan lop", "ket qua") ||
                plan?.Includes("EXAM_RESULTS") == true)
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
                "recommend", "goi y") || plan?.Includes("PROGRESS") == true ||
                plan?.Includes("LESSONS") == true)
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
        if (Has(question, "truy cap", "da mua", "khoa hoc", "course", "access", "gia han", "het han") ||
            plan?.Includes("COURSES") == true)
        {
            var activeIds = await CourseEntitlement.ActiveCourseIds(db, learnerId, now).ToArrayAsync(ct);
            var courses = await db.KhoaHoc.AsNoTracking().Where(c => activeIds.Contains(c.MaKhoaHoc))
                .OrderBy(c => c.MaKhoaHoc)
                .Select(c => new { c.MaKhoaHoc, c.TenKhoaHoc,
                    expiresAt = db.DangKyGoiHoc.Where(e => e.MaNguoiDung == learnerId &&
                        e.TrangThai == "DANG_SU_DUNG" && e.NgayBatDau <= now && e.NgayKetThuc > now &&
                        e.GiaoDichThanhToan.Any(p => p.TrangThai == "THANH_CONG") &&
                        (e.MaGoiHoc != null && e.MaGoiHocNavigation.MaKhoaHoc.Any(k =>
                            k.MaKhoaHoc == c.MaKhoaHoc) || e.ChiTietDangKyKhoaHoc.Any(d =>
                            d.MaKhoaHoc == c.MaKhoaHoc)))
                        .Max(e => (DateTime?)e.NgayKetThuc) }).ToListAsync(ct);
            lines.Add(courses.Count == 0 ? "Hiện chưa có khóa học đang có quyền truy cập." :
                "Khóa đang có quyền truy cập: " + string.Join("; ", courses.Select(c =>
                    $"{c.TenKhoaHoc} (hết hạn {c.expiresAt:dd/MM/yyyy HH:mm})")) + ".");
            foreach (var course in courses.Take(3))
                sources.Add(new AiSource(course.TenKhoaHoc, $"/courses/{course.MaKhoaHoc}"));
        }
        if (Has(question, "thanh toan", "payment", "vnpay", "tra lai", "thu lai") ||
            plan?.Includes("PAYMENT") == true)
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
                    $"đăng ký: {payment.enrollment}. " +
                    (payment.TrangThai == "CHO_XU_LY" ?
                        now < retryAt.DateTime ? $"Giao dịch đang chờ; mốc cho phép xử lý quá hạn là {retryAt:dd/MM/yyyy HH:mm}. " :
                            "Đã qua mốc xử lý quá hạn; mở trang Khóa học để hệ thống làm mới trạng thái trước khi thử lại. " :
                     payment.TrangThai is "THAT_BAI" or "QUA_HAN" ?
                        "Có thể chọn Thanh toán lại trên trang Khóa học nếu khóa còn mở và chưa có giao dịch mới đang chờ hoặc quyền học đang hiệu lực. " :
                        "Không cần thanh toán lại giao dịch đã thành công. ") +
                    "Nếu đã bị trừ tiền mà chưa có quyền học, liên hệ hỗ trợ để đối soát.");
                sources.Add(new AiSource($"Thanh toán #{payment.MaGiaoDich}",
                    $"/courses?paymentId={payment.MaGiaoDich}"));
            }
        }
        if (Has(question, "tu vung", "vocabulary", "tien do tu", "da hoc tu") ||
            plan?.Includes("VOCABULARY") == true)
        {
            var progress = await db.TienDoTuVung.AsNoTracking().Where(p => p.MaNguoiDung == learnerId)
                .GroupBy(p => p.TrangThai).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);
            lines.Add("Tiến độ từ vựng: " + (progress.Count == 0 ? "chưa có từ được ghi nhận" :
                string.Join("; ", progress.Select(p => $"{p.status}: {p.count}"))) + ".");
            sources.Add(new AiSource("Học từ vựng", "/vocabulary"));
        }
        if (Has(question, "luyen part", "thong ke", "do chinh xac", "accuracy", "yeu part") ||
            plan?.Includes("PRACTICE_STATS") == true)
            await AddPartStatsAsync(learnerId, lines, sources, ct);
        if (Has(question, "thi thu", "mock", "diem thi", "ket qua thi") ||
            plan?.Includes("EXAM_RESULTS") == true)
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

    private async Task AddPartStatsAsync(int learnerId, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var stats = await db.ThongKeLuyenPart.AsNoTracking().Where(p => p.MaHocVien == learnerId)
            .OrderBy(p => p.MaPart).Select(p => new { p.MaPart, p.SoLanLuyen,
                p.TongCauDaLam, p.TongCauDung, p.SoCauLanCuoi, p.SoDungLanCuoi,
                p.LanLuyenGanNhat }).ToListAsync(ct);
        lines.Add("Thống kê luyện Part của chính học viên: " +
            (stats.Count == 0 ? "chưa có lượt hoàn thành" : string.Join("; ", stats.Select(p =>
                $"Part {p.MaPart}: {p.SoLanLuyen} lần, {p.TongCauDung}/{p.TongCauDaLam} đúng tích lũy, " +
                $"lần gần nhất {p.SoDungLanCuoi}/{p.SoCauLanCuoi}, luyện gần nhất " +
                $"{p.LanLuyenGanNhat:dd/MM/yyyy HH:mm}"))) + ".");
        sources.Add(new AiSource("Luyện tập theo Part", "/practice/parts"));
    }

    private async Task AddProfileAsync(int learnerId, string question, List<string> lines,
        List<AiSource> sources, CancellationToken ct)
    {
        var user = await db.NguoiDung.AsNoTracking().Where(person =>
            person.MaNguoiDung == learnerId && person.VaiTro == "HOC_VIEN")
            .Select(person => new { person.HoTen, person.Email, person.SoDienThoai,
                person.TrangThai }).FirstOrDefaultAsync(ct);
        if (user is null) { lines.Add("Không xác minh được hồ sơ học viên trong phiên hiện tại."); return; }
        lines.Add($"Hồ sơ của chính tài khoản đăng nhập: tên {user.HoTen}; trạng thái {user.TrangThai}." +
            (Has(question, "email", "thu dien tu") ? $" Email {user.Email}." : "") +
            (Has(question, "dien thoai", "phone") ? $" Số điện thoại {user.SoDienThoai ?? "chưa có"}." : ""));
        sources.Add(new AiSource("Hồ sơ cá nhân", "/profile"));
    }

    private async Task AddCourseProgressAsync(int learnerId, DateTime now,
        List<string> lines, List<AiSource> sources, CancellationToken ct)
    {
        var availableSteps = db.BuocLoTrinh.AsNoTracking().Where(step => !step.IsArchived &&
            !step.MaLessonNavigation.IsArchived && !step.MaLessonNavigation.MaUnitNavigation.IsArchived &&
            step.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TrangThai == "DANG_MO" &&
            CourseEntitlement.ActiveCourseIds(db, learnerId, now)
                .Contains(step.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc) &&
            (step.MaBaiGiang != null && step.MaBaiGiangNavigation.TrangThai == "DANG_MO" ||
             step.MaChuDe != null && step.MaChuDeNavigation.TrangThai == "DANG_MO" ||
             step.MaBaiTap != null && db.PhienBanBaiTapKhoaHoc.Any(version =>
                 version.MaBaiTap == step.MaBaiTap && version.TrangThai == "XUAT_BAN")));
        var total = await availableSteps.CountAsync(ct);
        var completed = await db.TienDoBuocLoTrinh.AsNoTracking().Where(progress =>
            progress.MaNguoiDung == learnerId && progress.TrangThai == "HOAN_THANH" &&
            availableSteps.Select(step => step.MaBuoc).Contains(progress.MaBuoc))
            .Select(progress => progress.MaBuoc).Distinct().CountAsync(ct);
        var pending = await availableSteps.Where(step => !db.TienDoBuocLoTrinh.Any(progress =>
                progress.MaNguoiDung == learnerId && progress.MaBuoc == step.MaBuoc &&
                progress.TrangThai == "HOAN_THANH"))
            .OrderBy(step => step.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc)
            .ThenBy(step => step.MaLessonNavigation.MaUnitNavigation.ThuTu)
            .ThenBy(step => step.MaLessonNavigation.ThuTu).ThenBy(step => step.ThuTu).Take(5)
            .Select(step => new { step.MaBuoc, step.TieuDe,
                courseId = step.MaLessonNavigation.MaUnitNavigation.MaKhoaHoc,
                course = step.MaLessonNavigation.MaUnitNavigation.MaKhoaHocNavigation.TenKhoaHoc })
            .ToListAsync(ct);
        lines.Add($"Tiến độ trên nội dung đang mở của các khóa hiện có quyền: {completed}/{total} hoạt động hoàn thành. " +
            (pending.Count == 0 ? "Không có hoạt động chưa hoàn thành đang mở." :
                "Các hoạt động chưa hoàn thành đầu tiên: " + string.Join("; ", pending.Select(step =>
                    $"{step.course} / {step.TieuDe}")) + "."));
        foreach (var step in pending.Take(3))
            sources.Add(new AiSource($"Học tiếp · {step.TieuDe}",
                $"/courses/{step.courseId}/steps/{step.MaBuoc}"));
    }

    private static string[] Terms(string question) => Regex.Matches(question, @"[\p{L}\p{N}]{2,}")
        .Select(m => m.Value).Where(word => word.Length >= 3 &&
            !StopWords.Contains(AiWebsiteGuide.Normalize(word)))
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(x => x.Length).Take(2).ToArray();
    private static int Score(string text, string[] terms)
    {
        var normalized = AiWebsiteGuide.Normalize(text);
        return terms.Count(term => normalized.Contains(AiWebsiteGuide.Normalize(term),
            StringComparison.Ordinal));
    }
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
