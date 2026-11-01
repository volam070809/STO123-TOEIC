using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class MockExamEligibilityTests
{
    [Fact]
    public void Configured_placement_exam_can_also_be_a_visible_mock_when_open_and_standard()
    {
        var exams = new[] {
            new DeThi { MaDeThi = 1, LoaiDe = "DE_THI", TrangThai = "OPEN", ThoiGianLamBai = 120 },
            new DeThi { MaDeThi = 2, LoaiDe = "DE_THI", TrangThai = "OPEN", ThoiGianLamBai = 120 },
            new DeThi { MaDeThi = 3, LoaiDe = "DE_THI", TrangThai = "CLOSE", ThoiGianLamBai = 120 },
            new DeThi { MaDeThi = 4, LoaiDe = "DE_THI_DAU_VAO", TrangThai = "OPEN", ThoiGianLamBai = 120 }
        }.AsQueryable();
        Assert.Equal([1, 2], MockExamEligibility.Visible(exams, 1).Select(x => x.MaDeThi));
    }
}
