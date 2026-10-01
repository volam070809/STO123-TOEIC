using STO123.Models;

namespace STO123.Services.Exam;

public sealed record ExamValidationIssue(string Code, string Message);
public sealed record ExamValidationReport(bool Valid, IReadOnlyList<ExamValidationIssue> Issues);

public static class ExamSourceValidator
{
    public static bool ValidQuestion(CauHoi q, int part)
    {
        if (q.TrangThai != ExamCore.Published ||
            string.IsNullOrWhiteSpace(q.PhuongAnA) || string.IsNullOrWhiteSpace(q.PhuongAnB) ||
            string.IsNullOrWhiteSpace(q.PhuongAnC) ||
            (part != 2 && string.IsNullOrWhiteSpace(q.PhuongAnD))) return false;
        var answer = q.PhuongAnDung?.Trim();
        return answer is "A" or "B" or "C" ||
            (part != 2 && answer == "D" && !string.IsNullOrWhiteSpace(q.PhuongAnD));
    }

    public static bool ValidGroup(int part, IReadOnlyList<NhomCauHoi> members)
    {
        var count = part switch { 1 or 2 => 1, 3 or 4 => 3, 6 => 4, 7 => -1, _ => 0 };
        if (count == 0 || (count > 0 && members.Count != count) ||
            (part == 7 && members.Count is < 2 or > 5)) return false;
        return members.OrderBy(m => m.ThuTu).Select((m, index) => m.ThuTu == index + 1).All(x => x);
    }

    public static bool ValidMedia(NguLieu resource, int part) => part switch
    {
        1 => !string.IsNullOrWhiteSpace(resource.DuongDanAudio) && !string.IsNullOrWhiteSpace(resource.DuongDanAnh),
        2 or 3 or 4 => !string.IsNullOrWhiteSpace(resource.DuongDanAudio),
        6 => !string.IsNullOrWhiteSpace(resource.NoiDungNguLieu),
        _ => true
    };

    public static bool ValidDocuments(int part, IReadOnlyList<NguLieuTaiLieu> documents) =>
        (part != 7 || documents.Count is >= 1 and <= 3) &&
        documents.OrderBy(d => d.ThuTu).Select((d, index) => d.ThuTu == index + 1 &&
            ExamDocumentCodec.IsValidSource(d)).All(x => x);

    public static ExamValidationReport ValidateFixed(DeThi exam, IReadOnlyList<CauHoiDeThi> links,
        IReadOnlyDictionary<int, CauHoi> questions, IReadOnlyDictionary<int, int> partNumbers,
        IReadOnlyList<NhomCauHoi> memberships, IReadOnlyDictionary<int, NguLieu> resources,
        IReadOnlyList<NguLieuTaiLieu> documents, IReadOnlySet<string> existingBlobPaths)
    {
        var issues = new List<ExamValidationIssue>();
        void Add(string code, string message) => issues.Add(new(code, message));
        if (exam.LoaiDe is not ("DE_THI" or "DE_THI_DAU_VAO") || exam.ThoiGianLamBai != ExamCore.DurationMinutes)
            Add("EXAM_METADATA", "Loại đề hoặc thời lượng không hợp lệ.");
        if (links.Count != 200 || links.Select((x, i) => x.ThuTu == i + 1).Any(valid => !valid) ||
            links.Select(x => x.MaCauHoi).Distinct().Count() != links.Count)
            Add("EXAM_ORDER", "Đề phải có 200 câu, thứ tự 1..200 duy nhất.");

        var byQuestion = memberships.ToDictionary(m => m.MaCauHoi);
        var byResource = memberships.GroupBy(m => m.MaNguLieu).ToDictionary(g => g.Key, g => g.ToList());
        var orderedParts = new List<int>();
        var seenGroups = new HashSet<int>();
        for (var i = 0; i < links.Count; i++)
        {
            var id = links[i].MaCauHoi;
            if (!questions.TryGetValue(id, out var question) || !partNumbers.TryGetValue(question.MaPart, out var part))
            {
                Add("QUESTION_MISSING", $"Câu nguồn {id} không tồn tại hoặc thiếu Part.");
                continue;
            }
            orderedParts.Add(part);
            if (!ValidQuestion(question, part)) Add("QUESTION_INVALID", $"Câu {id} chưa xuất bản hoặc đáp án không hợp lệ.");
            if (part == 5)
            {
                if (byQuestion.ContainsKey(id)) Add("GROUP_INVALID", $"Câu Part 5 {id} không được thuộc nhóm.");
                continue;
            }
            if (!byQuestion.TryGetValue(id, out var member))
            {
                Add("GROUP_MISSING", $"Câu {id} thiếu nhóm nguồn.");
                continue;
            }
            if (!seenGroups.Add(member.MaNguLieu)) continue;
            var group = byResource[member.MaNguLieu];
            if (!ValidGroup(part, group) || group.Any(m => !questions.TryGetValue(m.MaCauHoi, out var q) ||
                !partNumbers.TryGetValue(q.MaPart, out var number) || number != part || !ValidQuestion(q, part)))
                Add("GROUP_INVALID", $"Nhóm {member.MaNguLieu} thiếu câu, sai Part hoặc sai thứ tự.");
            var expected = group.OrderBy(m => m.ThuTu).Select(m => m.MaCauHoi).ToArray();
            if (!links.Skip(i).Take(expected.Length).Select(l => l.MaCauHoi).SequenceEqual(expected))
                Add("GROUP_SPLIT", $"Nhóm {member.MaNguLieu} bị tách hoặc đổi thứ tự trong đề.");
            if (!resources.TryGetValue(member.MaNguLieu, out var resource) || !ValidMedia(resource, part))
            {
                Add("MEDIA_PATH_MISSING", $"Nhóm {member.MaNguLieu} thiếu ngữ liệu hoặc đường dẫn bắt buộc.");
                continue;
            }
            var paths = new[] { resource.DuongDanAudio, resource.DuongDanAnh }
                .Concat(documents.Where(d => d.MaNguLieu == member.MaNguLieu).Select(d => d.DuongDanAnh))
                .Where(p => !string.IsNullOrWhiteSpace(p));
            foreach (var path in paths.Where(p => !existingBlobPaths.Contains(p)))
                Add("BLOB_MISSING", $"Không tìm thấy Blob cho nhóm {member.MaNguLieu}: {path}");
            var groupDocuments = documents.Where(d => d.MaNguLieu == member.MaNguLieu).ToList();
            if (!ValidDocuments(part, groupDocuments))
                Add("DOCUMENT_INVALID", $"Nhóm {member.MaNguLieu} có tài liệu không hợp lệ.");
        }
        foreach (var (part, count) in ExamCore.PartCounts)
            if (orderedParts.Count(x => x == part) != count)
                Add("PART_COUNT", $"Part {part} cần {count} câu.");
        if (!orderedParts.SequenceEqual(orderedParts.OrderBy(x => x)))
            Add("PART_ORDER", "Thứ tự Part phải từ 1 đến 7.");
        return new(issues.Count == 0, issues);
    }
}
