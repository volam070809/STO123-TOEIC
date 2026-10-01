using STO123.DTOs.Exam;
using STO123.Models;

namespace STO123.Services.Exam;

public static class ExamQuestionProjection
{
    public static ExamQuestionDto BeforeSubmit(CauHoiLuotLam question, ChiTietKetQua? answer)
    {
        var part = question.MaPartNavigation.SoPart;
        var listeningOptionsHidden = part is 1 or 2;
        return new(question.MaCauHoiLuotLam, question.ThuTu, question.ThuTuTrongPart, part,
            listeningOptionsHidden ? null : question.NoiDung,
            listeningOptionsHidden ? null : question.PhuongAnA,
            listeningOptionsHidden ? null : question.PhuongAnB,
            listeningOptionsHidden ? null : question.PhuongAnC,
            listeningOptionsHidden ? null : question.PhuongAnD,
            answer?.DapAnChon, answer?.DanhDau ?? false);
    }
}
