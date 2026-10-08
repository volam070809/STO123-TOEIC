using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using STO123.DTOs.Exam;
using STO123.Models;

namespace STO123.Services.Exam;

public enum SnapshotView { Active, Result, Review }

public sealed record ExamSnapshot(KetQuaLamBai Attempt, string? ExamName,
    List<NhomLuotLam> Groups, List<CauHoiLuotLam> Questions,
    Dictionary<int, ChiTietKetQua> Answers);

// One SQL batch keeps the independent snapshot result sets separate, avoiding a multiplying JOIN.
public sealed class ExamSnapshotQueryService(ToeicDbContext db)
{
    public async Task<ExamTimerDto> HeartbeatAsync(int id, int learnerId, CancellationToken ct)
    {
        const string sql = """
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            DECLARE @started datetime2(3), @stored int, @status varchar(16),
                @owner int, @kind varchar(16);
            SELECT @started = BatDauPhienLuc, @stored = ThoiGianConLaiGiay,
                @status = TrangThai, @owner = MaHocVien, @kind = LoaiBaiLam
            FROM dbo.KetQuaLamBai WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE MaKetQua = @id;
            IF @owner = @learner AND @kind IN ('MOCK', 'PLACEMENT')
                AND @status IN ('DANG_LAM', 'BO_DO') AND @started IS NOT NULL AND @stored IS NOT NULL
            BEGIN
                DECLARE @elapsedMs bigint = DATEDIFF_BIG(millisecond, @started, @now);
                DECLARE @stale bit = CASE WHEN @elapsedMs > @leaseSeconds * 1000 THEN 1 ELSE 0 END;
                DECLARE @elapsed bigint = CASE WHEN @elapsedMs < 0 THEN 0 ELSE @elapsedMs / 1000 END;
                DECLARE @charge int = CASE WHEN @elapsed > @leaseSeconds THEN @leaseSeconds ELSE CONVERT(int, @elapsed) END;
                DECLARE @remaining int = CASE WHEN @stored > @charge THEN @stored - @charge ELSE 0 END;
                UPDATE dbo.KetQuaLamBai
                SET ThoiGianConLaiGiay = @remaining,
                    BatDauPhienLuc = CASE WHEN @stale = 1 OR @remaining = 0 THEN NULL ELSE @now END
                WHERE MaKetQua = @id;
            END;
            COMMIT TRANSACTION;
            SELECT MaHocVien, LoaiBaiLam, TrangThai, ThoiGianConLaiGiay, BatDauPhienLuc
            FROM dbo.KetQuaLamBai WHERE MaKetQua = @id;
            """;
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            Add(command, "@id", id); Add(command, "@learner", learnerId);
            Add(command, "@now", DateTime.UtcNow, DbType.DateTime2);
            Add(command, "@leaseSeconds", ExamTimer.LeaseSeconds);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
            if (reader.GetInt32(0) != learnerId)
                throw new ExamProblem("ATTEMPT_FORBIDDEN", "Bạn không có quyền xem lượt làm bài này.", 403);
            if (reader.GetString(1) is not (ExamCore.Mock or ExamCore.Placement))
                throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
            var status = reader.GetString(2);
            var paused = reader.IsDBNull(4);
            var remaining = status is ExamCore.Active or "BO_DO" ? IntOrNull(reader, 3) : null;
            if (status is ExamCore.Active or "BO_DO" && remaining is null)
                throw new ExamProblem("TIMER_NOT_INITIALIZED", "Đồng hồ bài thi chưa được khởi tạo.", 409);
            return new ExamTimerDto(status, remaining, paused);
        }
        finally { if (openedHere) await connection.CloseAsync(); }
    }

    public async Task<ExamSnapshot> LoadAsync(int id, int learnerId, SnapshotView view,
        bool resume, CancellationToken ct)
    {
        var questionColumns = view switch
        {
            SnapshotView.Active => "q.MaCauHoiLuotLam, q.MaNhomLuotLam, q.ThuTu, q.ThuTuTrongPart, p.SoPart, " +
                "CASE WHEN p.SoPart IN (1, 2) THEN NULL ELSE q.NoiDung END, " +
                "CASE WHEN p.SoPart IN (1, 2) THEN NULL ELSE q.PhuongAnA END, " +
                "CASE WHEN p.SoPart IN (1, 2) THEN NULL ELSE q.PhuongAnB END, " +
                "CASE WHEN p.SoPart IN (1, 2) THEN NULL ELSE q.PhuongAnC END, " +
                "CASE WHEN p.SoPart IN (1, 2) THEN NULL ELSE q.PhuongAnD END",
            SnapshotView.Review => "q.MaCauHoiLuotLam, q.MaNhomLuotLam, q.ThuTu, q.ThuTuTrongPart, p.SoPart, q.NoiDung, q.PhuongAnA, q.PhuongAnB, q.PhuongAnC, q.PhuongAnD, q.PhuongAnDung, q.GiaiThich",
            _ => "q.MaCauHoiLuotLam, p.SoPart, q.PhuongAnDung"
        };
        var groupSelect = view == SnapshotView.Result ? "" :
            """
            SELECT g.MaNhomLuotLam, g.ThuTu, p.SoPart, g.NoiDungNguLieu, g.DuongDanAudio, g.DuongDanAnh, g.TaiLieuJson
            FROM dbo.NhomLuotLam AS g JOIN dbo.PartTOEIC AS p ON p.MaPart = g.MaPart
            WHERE g.MaKetQua = @id AND EXISTS (SELECT 1 FROM dbo.KetQuaLamBai AS a WHERE a.MaKetQua = @id AND a.MaHocVien = @learner)
            ORDER BY g.ThuTu;
            """;
        var sql = $"""
            SET NOCOUNT ON;
            UPDATE dbo.KetQuaLamBai WITH (UPDLOCK, ROWLOCK)
            SET ThoiGianConLaiGiay = CASE WHEN BatDauPhienLuc IS NOT NULL AND
                    DATEDIFF_BIG(millisecond, BatDauPhienLuc, @now) > @leaseMs
                    THEN CASE WHEN ThoiGianConLaiGiay > @leaseSeconds THEN ThoiGianConLaiGiay - @leaseSeconds ELSE 0 END
                    ELSE ThoiGianConLaiGiay END,
                BatDauPhienLuc = CASE
                    WHEN BatDauPhienLuc IS NOT NULL AND DATEDIFF_BIG(millisecond, BatDauPhienLuc, @now) > @leaseMs
                        THEN CASE WHEN @resume = 1 AND ThoiGianConLaiGiay > @leaseSeconds THEN @now ELSE NULL END
                    WHEN @resume = 1 AND BatDauPhienLuc IS NULL AND ThoiGianConLaiGiay > 0 THEN @now
                    ELSE BatDauPhienLuc END,
                TrangThai = CASE WHEN @resume = 1 THEN 'DANG_LAM' ELSE TrangThai END
            WHERE @active = 1 AND MaKetQua = @id AND MaHocVien = @learner AND LoaiBaiLam IN ('MOCK', 'PLACEMENT')
                AND TrangThai IN ('DANG_LAM', 'BO_DO')
                AND ((@resume = 1 AND (BatDauPhienLuc IS NULL OR TrangThai = 'BO_DO' OR
                    DATEDIFF_BIG(millisecond, BatDauPhienLuc, @now) > @leaseMs)) OR
                    (@resume = 0 AND BatDauPhienLuc IS NOT NULL AND
                    DATEDIFF_BIG(millisecond, BatDauPhienLuc, @now) > @leaseMs));
            SELECT a.MaKetQua, a.MaHocVien, a.MaDeThi, a.LoaiBaiLam, a.TrangThai,
                a.NgayLamBai, a.NgayNopBai, a.ThoiGianConLaiGiay, a.BatDauPhienLuc,
                a.DiemNghe, a.DiemDoc, a.DiemTong, a.GiaiDoanLucNop, d.TenDe
            FROM dbo.KetQuaLamBai AS a LEFT JOIN dbo.DeThi AS d ON d.MaDeThi = a.MaDeThi
            WHERE a.MaKetQua = @id;
            {groupSelect}
            SELECT {questionColumns}
            FROM dbo.CauHoiLuotLam AS q JOIN dbo.PartTOEIC AS p ON p.MaPart = q.MaPart
            WHERE q.MaKetQua = @id AND EXISTS (SELECT 1 FROM dbo.KetQuaLamBai AS a WHERE a.MaKetQua = @id AND a.MaHocVien = @learner)
            ORDER BY q.ThuTu;
            SELECT c.MaCauHoiLuotLam, c.DapAnChon, c.DanhDau
            FROM dbo.ChiTietKetQua AS c JOIN dbo.CauHoiLuotLam AS q ON q.MaCauHoiLuotLam = c.MaCauHoiLuotLam
            WHERE q.MaKetQua = @id AND EXISTS (SELECT 1 FROM dbo.KetQuaLamBai AS a WHERE a.MaKetQua = @id AND a.MaHocVien = @learner);
            """;
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            Add(command, "@id", id); Add(command, "@learner", learnerId);
            Add(command, "@now", DateTime.UtcNow, DbType.DateTime2);
            Add(command, "@resume", resume ? 1 : 0);
            Add(command, "@active", view == SnapshotView.Active ? 1 : 0);
            Add(command, "@leaseMs", ExamTimer.LeaseSeconds * 1000);
            Add(command, "@leaseSeconds", ExamTimer.LeaseSeconds);
            await using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
            var owner = reader.GetInt32(1);
            if (owner != learnerId) throw new ExamProblem("ATTEMPT_FORBIDDEN", "Bạn không có quyền xem lượt làm bài này.", 403);
            var kind = reader.GetString(3);
            if (kind is not (ExamCore.Mock or ExamCore.Placement))
                throw new ExamProblem("ATTEMPT_NOT_FOUND", "Không tìm thấy lượt làm bài.", 404);
            var attempt = new KetQuaLamBai {
                MaKetQua = reader.GetInt32(0), MaHocVien = owner,
                MaDeThi = IntOrNull(reader, 2), LoaiBaiLam = kind, TrangThai = reader.GetString(4),
                NgayLamBai = reader.GetDateTime(5), NgayNopBai = DateOrNull(reader, 6),
                ThoiGianConLaiGiay = IntOrNull(reader, 7), BatDauPhienLuc = DateOrNull(reader, 8),
                DiemNghe = IntOrNull(reader, 9), DiemDoc = IntOrNull(reader, 10),
                DiemTong = IntOrNull(reader, 11), GiaiDoanLucNop = reader.IsDBNull(12) ? null : reader.GetByte(12)
            };
            var examName = StringOrNull(reader, 13);
            var groups = new List<NhomLuotLam>();
            await reader.NextResultAsync(ct);
            if (view != SnapshotView.Result)
            {
                while (await reader.ReadAsync(ct))
                    groups.Add(new NhomLuotLam {
                        MaNhomLuotLam = reader.GetInt32(0), ThuTu = reader.GetInt32(1),
                        MaPartNavigation = new PartTOEIC { SoPart = reader.GetInt32(2) },
                        NoiDungNguLieu = StringOrNull(reader, 3), DuongDanAudio = StringOrNull(reader, 4),
                        DuongDanAnh = StringOrNull(reader, 5), TaiLieuJson = StringOrNull(reader, 6)
                    });
                await reader.NextResultAsync(ct);
            }
            var questions = new List<CauHoiLuotLam>();
            while (await reader.ReadAsync(ct))
            {
                var result = view == SnapshotView.Result;
                var q = new CauHoiLuotLam {
                    MaCauHoiLuotLam = reader.GetInt32(0),
                    MaPartNavigation = new PartTOEIC { SoPart = reader.GetInt32(result ? 1 : 4) }
                };
                if (result) q.PhuongAnDung = reader.GetString(2);
                else
                {
                    q.MaNhomLuotLam = IntOrNull(reader, 1); q.ThuTu = reader.GetInt32(2);
                    q.ThuTuTrongPart = reader.GetInt32(3); q.NoiDung = StringOrNull(reader, 5);
                    q.PhuongAnA = StringOrNull(reader, 6); q.PhuongAnB = StringOrNull(reader, 7);
                    q.PhuongAnC = StringOrNull(reader, 8); q.PhuongAnD = StringOrNull(reader, 9);
                    if (view == SnapshotView.Review) { q.PhuongAnDung = reader.GetString(10); q.GiaiThich = StringOrNull(reader, 11); }
                }
                questions.Add(q);
            }
            var answers = new Dictionary<int, ChiTietKetQua>();
            await reader.NextResultAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new ChiTietKetQua { MaCauHoiLuotLam = reader.GetInt32(0),
                    DapAnChon = StringOrNull(reader, 1), DanhDau = reader.GetBoolean(2) };
                answers.Add(row.MaCauHoiLuotLam, row);
            }
            return new ExamSnapshot(attempt, examName, groups, questions, answers);
        }
        finally { if (openedHere) await connection.CloseAsync(); }
    }

    private static int? IntOrNull(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static DateTime? DateOrNull(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetDateTime(index);
    private static string? StringOrNull(DbDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static void Add(DbCommand command, string name, object value, DbType? type = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name; parameter.Value = value;
        if (type.HasValue) parameter.DbType = type.Value;
        command.Parameters.Add(parameter);
    }
}
