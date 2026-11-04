using System.Text.RegularExpressions;

namespace STO123.Services.Ai;

public static class AiQuestionIntent
{
    public static AiRetrievalPlan? FastPlan(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        var domains = new HashSet<string>(StringComparer.Ordinal);
        if (Has(text, "chu de", "topic", "nhom tu")) domains.Add("TOPICS");
        if (Has(text, "tu vung", "vocabulary", "nghia tu", "bao nhieu tu", "co may tu", "on tu"))
            domains.Add("VOCABULARY");
        if (Has(text, "khoa hoc", "khoa da mua", "khoa cua", "so huu khoa", "thoi han hoc",
            "bao nhieu khoa", "co may khoa"))
            domains.Add("COURSES");
        if (Has(text, "bai hoc", "bai giang", "bai nao", "tai lieu", "lecture", "lesson"))
            domains.Add("LESSONS");
        if (Has(text, "tien do", "hoc tiep", "chua hoan thanh", "da hoan thanh", "chua hoc"))
            domains.Add("PROGRESS");
        if (Has(text, "yeu part", "luyen part", "thong ke part", "ti le dung", "ty le dung"))
            domains.Add("PRACTICE_STATS");
        if (Has(text, "thi thu", "diem thi", "ket qua thi", "dau vao", "knn", "mock"))
            domains.Add("EXAM_RESULTS");
        if (Has(text, "thanh toan", "giao dich", "vnpay", "hoan tien"))
            domains.Add("PAYMENT");
        if (Has(text, "ho so", "ten toi", "email cua toi", "tai khoan cua toi", "so dien thoai cua toi"))
            domains.Add("PROFILE");
        if (IsWebsiteNavigation(question)) domains.Add("WEBSITE");
        return domains.Count == 0 ? null : new(domains, []);
    }

    public static bool IsWebsiteNavigation(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "o dau", "where", "how to use", "how to open", "cach mo", "cach vao",
            "trang nao", "menu nao", "vao dau", "nhu the nao", "lam sao", "huong dan");
    }

    public static bool WantsWebsiteCatalog(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "chuc nang nao", "tat ca chuc nang", "cac chuc nang",
            "website co gi", "web co gi", "website co nhung", "all features");
    }

    public static bool WantsLessonList(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "danh sach bai hoc", "liet ke bai hoc", "tat ca bai hoc",
            "cac bai hoc", "nhung bai hoc", "all lessons", "list lessons");
    }

    public static bool IsProtectedAnswerRequest(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        if (Has(text, "o dau", "where can i", "cach xem", "how to view", "how to check")) return false;
        return Has(text, "dap an", "phuong an dung", "correct option", "correct answer",
            "right answer", "give me the answer", "which option", "which answer",
            "chon a", "chon b", "chon c", "chon d", "giai ho cau", "giai cau nay",
            "answer this question", "solve this question");
    }

    public static bool ReferencesCurrentQuestion(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return IsProtectedAnswerRequest(question) || Has(text,
            "cau nay", "cau hoi nay", "bai nay", "cau vua roi", "cau dang lam",
            "this question", "that question", "current question", "question above",
            "lua chon", "phuong an", "giai thich cau");
    }

    public static bool WantsLessonContent(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        if (WantsPracticeExamples(question) || WantsVocabularyContent(question)) return false;
        return Has(text, "bai hoc", "bai giang", "lesson", "lecture", "khoa hoc", "course") ||
            Has(text, "find", "search", "look up", "tim") &&
            !Has(text, "o dau", "where", "cach dung", "how to use");
    }

    public static bool WantsVocabularyContent(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        if (Has(text, "tu vung", "vocabulary", "nghia", "meaning", "word")) return true;
        // A single unfamiliar word is a likely lookup; multiword grammar questions stay general.
        return Regex.IsMatch(text, @"^(explain|giai thich)\s+[\p{L}]{3,}[?.!]?$") &&
            !Has(text, "grammar", "ngu phap", "thi ", "part ");
    }

    public static bool WantsVocabularyCatalog(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "chu de", "topic", "nhom tu", "danh sach tu") ||
            Has(text, "bao nhieu", "so luong", "tong so", "how many", "count") &&
            Has(text, "tu vung", "vocabulary", "word", "tu moi");
    }

    public static bool WantsTopicList(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "chu de", "topic", "danh sach", "co nhung", "ke ten", "thuoc chu de");
    }

    public static bool WantsWordsInTopic(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "chu de", "topic", "nhom tu") &&
            Has(text, "tu nao", "nhung tu", "cac tu", "danh sach tu", "liet ke tu",
                "co tu gi", "tu vung trong", "all words", "list words");
    }

    public static bool IsPublicCountQuestion(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "bao nhieu", "co may", "so luong", "tong so", "how many", "count") &&
            !Has(text, "toi da", "cua toi", "cua minh", "my ", "da hoc", "da lam", "da mua");
    }

    public static bool NeedsSemanticPlan(string question, bool hasVerifiedFeature,
        bool hasCurrentPage)
    {
        if (IsPublicCountQuestion(question) || IsProtectedAnswerRequest(question)) return false;
        var text = AiWebsiteGuide.Normalize(question);
        return hasVerifiedFeature || IsWebsiteNavigation(question) || IsFollowUp(question) ||
            Regex.IsMatch(text, @"\b(toi|minh|my|me)\b") ||
            Has(text, "sto123", "he thong", "website", "tai khoan", "cua toi", "toi dang",
                "toi co", "toi nen", "da mua", "da hoc", "het han", "thanh toan", "lo trinh",
                "tien do", "ket qua", "yeu part", "my course", "my progress") ||
            hasCurrentPage && Has(text, "bai", "khoa", "tu", "phan", "trang nay");
    }

    public static bool WantsCourseCatalog(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "khoa hoc", "course", "chuong trinh hoc") &&
            Has(text, "bao nhieu", "so luong", "danh sach", "co nhung", "what courses", "available");
    }

    public static bool IsFollowUp(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "bai do", "khoa do", "chu de do", "tu do", "cac tu do",
            "phan do", "vay", "tiep theo", "bai nay", "cai do", "that lesson",
            "that course", "that topic", "those", "then");
    }

    public static bool WantsPracticeExamples(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Regex.IsMatch(text, @"\bpart\s*[1-7]\b") &&
            !Has(text, "o dau", "where", "how to practice", "cach luyen") &&
            Has(text, "practice question", "cau hoi", "find", "tim", "document", "tai lieu", "vi du");
    }

    public static bool WantsPracticeAvailability(string question)
    {
        var text = AiWebsiteGuide.Normalize(question);
        return Has(text, "part", "luyen tap", "bai luyen") &&
            Has(text, "du cau", "con cau", "bao nhieu cau", "co the bat dau",
                "muc luyen", "do kho", "available", "availability", "short", "medium", "long");
    }

    private static bool Has(string text, params string[] phrases) =>
        phrases.Any(phrase => text.Contains(phrase, StringComparison.Ordinal));
}
