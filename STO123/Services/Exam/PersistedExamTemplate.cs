using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using STO123.Models;

namespace STO123.Services.Exam;

// All source rows are read in one SQL batch. No source tracking or Blob requests occur at learner Start.
public sealed class PersistedExamTemplate(ToeicDbContext db, RandomStartProfiler profile)
{
    private static ExamProblem Invalid() => new("INVALID_EXAM_STRUCTURE", "Cấu trúc đề thi không hợp lệ.", 409);

    public async Task<IReadOnlyList<PlannedUnit>> LoadAsync(int examId, CancellationToken ct)
    {
        const string sql = """
            SET NOCOUNT ON;
            SELECT MaDeThi, LoaiDe, TrangThai, ThoiGianLamBai FROM dbo.DeThi WHERE MaDeThi = @exam;
            SELECT l.MaCauHoi, l.ThuTu, q.MaPart, p.SoPart, q.NoiDung, q.PhuongAnA,
                   q.PhuongAnB, q.PhuongAnC, q.PhuongAnD, q.PhuongAnDung, q.GiaiThich,
                   q.DoKho, q.TrangThai
            FROM dbo.CauHoiDeThi l JOIN dbo.CauHoi q ON q.MaCauHoi = l.MaCauHoi
            JOIN dbo.PartTOEIC p ON p.MaPart = q.MaPart
            WHERE l.MaDeThi = @exam ORDER BY l.ThuTu;
            SELECT m.MaCauHoi, m.MaNguLieu, m.ThuTu
            FROM dbo.NhomCauHoi m
            WHERE m.MaNguLieu IN (SELECT DISTINCT m2.MaNguLieu FROM dbo.NhomCauHoi m2
                                  JOIN dbo.CauHoiDeThi l ON l.MaCauHoi = m2.MaCauHoi
                                  WHERE l.MaDeThi = @exam);
            SELECT r.MaNguLieu, r.NoiDungNguLieu, r.NoiDungDich, r.DuongDanAudio, r.DuongDanAnh
            FROM dbo.NguLieu r
            WHERE r.MaNguLieu IN (SELECT DISTINCT m.MaNguLieu FROM dbo.NhomCauHoi m
                                  JOIN dbo.CauHoiDeThi l ON l.MaCauHoi = m.MaCauHoi
                                  WHERE l.MaDeThi = @exam);
            SELECT d.MaTaiLieu, d.MaNguLieu, d.ThuTu, d.LoaiTaiLieu, d.NoiDung, d.DuongDanAnh
            FROM dbo.NguLieuTaiLieu d
            WHERE d.MaNguLieu IN (SELECT DISTINCT m.MaNguLieu FROM dbo.NhomCauHoi m
                                  JOIN dbo.CauHoiDeThi l ON l.MaCauHoi = m.MaCauHoi
                                  WHERE l.MaDeThi = @exam) ORDER BY d.MaNguLieu, d.ThuTu;
            """;
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@exam";
            parameter.Value = examId;
            command.Parameters.Add(parameter);
            var sqlTime = Stopwatch.StartNew();
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) || reader.GetString(1) != "DE_THI" ||
                reader.GetString(2) != "OPEN" || reader.GetInt32(3) != ExamCore.DurationMinutes) throw Invalid();
            await reader.NextResultAsync(ct);
            var links = new List<CauHoiDeThi>();
            var questions = new Dictionary<int, CauHoi>();
            var partNumbers = new Dictionary<int, int>();
            static string? Optional(System.Data.Common.DbDataReader row, int ordinal) =>
                row.IsDBNull(ordinal) ? null : row.GetString(ordinal);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(0);
                var partId = reader.GetInt32(2);
                links.Add(new CauHoiDeThi { MaDeThi = examId, MaCauHoi = id, ThuTu = reader.GetInt32(1) });
                questions.Add(id, new CauHoi {
                    MaCauHoi = id, MaPart = partId, NoiDung = Optional(reader, 4),
                    PhuongAnA = reader.GetString(5), PhuongAnB = reader.GetString(6),
                    PhuongAnC = reader.GetString(7), PhuongAnD = Optional(reader, 8),
                    PhuongAnDung = reader.GetString(9), GiaiThich = Optional(reader, 10),
                    DoKho = reader.GetByte(11), TrangThai = reader.GetString(12)
                });
                partNumbers[partId] = reader.GetInt32(3);
            }
            await reader.NextResultAsync(ct);
            var memberships = new List<NhomCauHoi>();
            while (await reader.ReadAsync(ct)) memberships.Add(new NhomCauHoi {
                MaCauHoi = reader.GetInt32(0), MaNguLieu = reader.GetInt32(1), ThuTu = reader.GetInt32(2)
            });
            await reader.NextResultAsync(ct);
            var resources = new Dictionary<int, NguLieu>();
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt32(0);
                resources.Add(id, new NguLieu { MaNguLieu = id, NoiDungNguLieu = Optional(reader, 1),
                    NoiDungDich = Optional(reader, 2), DuongDanAudio = Optional(reader, 3),
                    DuongDanAnh = Optional(reader, 4) });
            }
            await reader.NextResultAsync(ct);
            var documents = new List<NguLieuTaiLieu>();
            while (await reader.ReadAsync(ct)) documents.Add(new NguLieuTaiLieu {
                MaTaiLieu = reader.GetInt32(0), MaNguLieu = reader.GetInt32(1),
                ThuTu = reader.GetInt32(2), LoaiTaiLieu = reader.GetString(3),
                NoiDung = Optional(reader, 4), DuongDanAnh = Optional(reader, 5)
            });
            var selectedIds = links.Select(x => x.MaCauHoi).ToHashSet();
            var units = new List<PlannedUnit>();
            var membersByQuestion = memberships.ToDictionary(x => x.MaCauHoi);
            var membersByResource = memberships.GroupBy(x => x.MaNguLieu).ToDictionary(x => x.Key, x => x.OrderBy(m => m.ThuTu).ToArray());
            var docsByResource = documents.GroupBy(x => x.MaNguLieu).ToDictionary(x => x.Key, x => (IReadOnlyList<NguLieuTaiLieu>)x.ToArray());
            foreach (var link in links)
            {
                var q = questions[link.MaCauHoi];
                var part = partNumbers[q.MaPart];
                if (!ExamSourceValidator.ValidQuestion(q, part)) throw Invalid();
                if (part == 5)
                {
                    if (membersByQuestion.ContainsKey(q.MaCauHoi)) throw Invalid();
                    units.Add(new PlannedUnit(part, null, [q], []));
                    continue;
                }
                if (!membersByQuestion.TryGetValue(q.MaCauHoi, out var member) ||
                    !resources.TryGetValue(member.MaNguLieu, out var resource)) throw Invalid();
                if (units.Any(x => x.Resource?.MaNguLieu == resource.MaNguLieu)) continue;
                var group = membersByResource[resource.MaNguLieu];
                if (group.Any(x => !selectedIds.Contains(x.MaCauHoi)) ||
                    group.Any(x => partNumbers.GetValueOrDefault(questions[x.MaCauHoi].MaPart) != part) ||
                    !ExamSourceValidator.ValidGroup(part, group) || !ExamSourceValidator.ValidMedia(resource, part)) throw Invalid();
                var docs = docsByResource.GetValueOrDefault(resource.MaNguLieu, []);
                if (!ExamSourceValidator.ValidDocuments(part, docs)) throw Invalid();
                units.Add(new PlannedUnit(part, resource, group.Select(x => questions[x.MaCauHoi]).ToArray(), docs));
            }
            var result = FixedExamPlan.Select(links, units);
            sqlTime.Stop();
            profile.CountManualSqlCommand("templateRead/sourceBatch", 1, sqlTime.Elapsed);
            return result;
        }
        finally { if (opened) await connection.CloseAsync(); }
    }
}
