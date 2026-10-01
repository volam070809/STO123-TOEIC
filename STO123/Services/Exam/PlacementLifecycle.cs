using STO123.Models;

namespace STO123.Services.Exam;

public enum PlacementAction { Create, Resume, FinalizeExpired, ResultOnly }

public static class PlacementLifecycle
{
    public static PlacementAction Decide(KetQuaLamBai? existing, DateTime nowUtc)
    {
        if (existing is null) return PlacementAction.Create;
        if (existing.TrangThai is ExamCore.Submitted or ExamCore.Expired) return PlacementAction.ResultOnly;
        if (existing.TrangThai is ExamCore.Active or "BO_DO")
            return existing.HetHanLuc <= nowUtc ? PlacementAction.FinalizeExpired : PlacementAction.Resume;
        throw new ExamProblem("INVALID_ATTEMPT_STATUS", "Trạng thái bài phân lớp không hợp lệ.", 409);
    }
}
