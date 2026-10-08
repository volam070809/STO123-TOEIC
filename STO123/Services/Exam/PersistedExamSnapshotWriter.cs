using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using STO123.Models;

namespace STO123.Services.Exam;

// Writes the immutable snapshot in one SQL Server command while retaining the caller's transaction.
public sealed class PersistedExamSnapshotWriter(ToeicDbContext db, RandomStartProfiler profile)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General);

    private const string InsertSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @AttemptMap TABLE (AttemptId int NOT NULL);
        INSERT INTO dbo.KetQuaLamBai
            (MaDeThi, MaHocVien, MaLuotLam, LoaiBaiLam, TrangThai, NgayLamBai,
             HetHanLuc, ThoiGianConLaiGiay, BatDauPhienLuc)
        OUTPUT inserted.MaKetQua INTO @AttemptMap (AttemptId)
        VALUES (@examId, @learnerId, @attemptGuid, @kind, 'DANG_LAM', SYSUTCDATETIME(),
                NULL, @durationSeconds, NULL);
        DECLARE @attemptId int = (SELECT TOP (1) AttemptId FROM @AttemptMap);

        DECLARE @GroupMap TABLE (GroupOrder int NOT NULL PRIMARY KEY,
                                 GroupId int NOT NULL, PartId int NOT NULL);
        INSERT INTO dbo.NhomLuotLam
            (MaKetQua, MaNguLieuGoc, MaPart, ThuTu, NoiDungNguLieu, NoiDungDich,
             DuongDanAudio, DuongDanAnh, TaiLieuJson)
        OUTPUT inserted.ThuTu, inserted.MaNhomLuotLam, inserted.MaPart
            INTO @GroupMap (GroupOrder, GroupId, PartId)
        SELECT @attemptId, j.MaNguLieuGoc, j.MaPart, j.ThuTu, j.NoiDungNguLieu,
               j.NoiDungDich, j.DuongDanAudio, j.DuongDanAnh, j.TaiLieuJson
        FROM OPENJSON(@groups) WITH (
            MaNguLieuGoc int '$.MaNguLieuGoc', MaPart int '$.MaPart', ThuTu int '$.ThuTu',
            NoiDungNguLieu nvarchar(max) '$.NoiDungNguLieu',
            NoiDungDich nvarchar(max) '$.NoiDungDich',
            DuongDanAudio varchar(512) '$.DuongDanAudio',
            DuongDanAnh varchar(512) '$.DuongDanAnh',
            TaiLieuJson nvarchar(max) '$.TaiLieuJson') AS j;
        IF @@ROWCOUNT <> @groupCount THROW 51001, 'Exam group snapshot count mismatch.', 1;

        DECLARE @QuestionRows TABLE (
            GroupOrder int NULL, MaCauHoiGoc int NULL, MaPart int NOT NULL,
            ThuTu int NOT NULL, ThuTuTrongPart int NOT NULL, NoiDung nvarchar(max) NULL,
            PhuongAnA nvarchar(512) NOT NULL, PhuongAnB nvarchar(512) NOT NULL,
            PhuongAnC nvarchar(512) NOT NULL, PhuongAnD nvarchar(512) NULL,
            PhuongAnDung char(1) NOT NULL, GiaiThich nvarchar(max) NULL);
        INSERT INTO @QuestionRows
        SELECT j.GroupOrder, j.MaCauHoiGoc, j.MaPart, j.ThuTu, j.ThuTuTrongPart,
               j.NoiDung, j.PhuongAnA, j.PhuongAnB, j.PhuongAnC, j.PhuongAnD,
               j.PhuongAnDung, j.GiaiThich
        FROM OPENJSON(@questions) WITH (
            GroupOrder int '$.GroupOrder', MaCauHoiGoc int '$.MaCauHoiGoc',
            MaPart int '$.MaPart', ThuTu int '$.ThuTu',
            ThuTuTrongPart int '$.ThuTuTrongPart', NoiDung nvarchar(max) '$.NoiDung',
            PhuongAnA nvarchar(512) '$.PhuongAnA',
            PhuongAnB nvarchar(512) '$.PhuongAnB',
            PhuongAnC nvarchar(512) '$.PhuongAnC',
            PhuongAnD nvarchar(512) '$.PhuongAnD',
            PhuongAnDung char(1) '$.PhuongAnDung',
            GiaiThich nvarchar(max) '$.GiaiThich') AS j;
        IF @@ROWCOUNT <> 200 THROW 51002, 'Exam question payload count mismatch.', 1;
        IF EXISTS (SELECT 1 FROM @QuestionRows q LEFT JOIN @GroupMap g ON g.GroupOrder = q.GroupOrder
                   WHERE q.GroupOrder IS NOT NULL AND (g.GroupId IS NULL OR g.PartId <> q.MaPart))
            THROW 51003, 'Exam question group mapping is invalid.', 1;

        INSERT INTO dbo.CauHoiLuotLam
            (MaKetQua, MaNhomLuotLam, MaCauHoiGoc, MaPart, ThuTu, ThuTuTrongPart,
             NoiDung, PhuongAnA, PhuongAnB, PhuongAnC, PhuongAnD, PhuongAnDung, GiaiThich)
        SELECT @attemptId, g.GroupId, q.MaCauHoiGoc, q.MaPart, q.ThuTu, q.ThuTuTrongPart,
               q.NoiDung, q.PhuongAnA, q.PhuongAnB, q.PhuongAnC, q.PhuongAnD,
               q.PhuongAnDung, q.GiaiThich
        FROM @QuestionRows q LEFT JOIN @GroupMap g ON g.GroupOrder = q.GroupOrder;
        IF @@ROWCOUNT <> 200 THROW 51004, 'Exam question snapshot count mismatch.', 1;

        DECLARE @timerStart datetime2(3) = SYSUTCDATETIME();
        UPDATE dbo.KetQuaLamBai
        SET BatDauPhienLuc = @timerStart,
            HetHanLuc = DATEADD(second, @durationSeconds, @timerStart)
        WHERE MaKetQua = @attemptId;
        IF @@ROWCOUNT <> 1 THROW 51005, 'Exam timer could not be started.', 1;
        DECLARE @timerDone datetime2(3) = SYSUTCDATETIME();
        SELECT @attemptId AS AttemptId,
               CONVERT(float, DATEDIFF_BIG(microsecond, @timerStart, @timerDone)) / 1000.0 AS TimerMs;
        """;

    public async Task<(int AttemptId, double TimerMilliseconds)> InsertAsync(int learnerId, int examId,
        string kind, Guid attemptGuid, int durationSeconds, IReadOnlyList<NhomLuotLam> groups,
        IReadOnlyList<(CauHoiLuotLam Question, int? GroupOrder)> questions, CancellationToken ct)
    {
        if (kind is not (ExamCore.Mock or ExamCore.Placement) || questions.Count != 200)
            throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Đề thi phải có đúng 200 câu và loại bài làm hợp lệ.", 409);
        var groupJson = JsonSerializer.Serialize(groups.Select(g => new {
            g.MaNguLieuGoc, g.MaPart, g.ThuTu, g.NoiDungNguLieu, g.NoiDungDich,
            g.DuongDanAudio, g.DuongDanAnh, g.TaiLieuJson
        }), Json);
        var questionJson = JsonSerializer.Serialize(questions.Select(item => new {
            item.GroupOrder, item.Question.MaCauHoiGoc, item.Question.MaPart,
            item.Question.ThuTu, item.Question.ThuTuTrongPart, item.Question.NoiDung,
            item.Question.PhuongAnA, item.Question.PhuongAnB, item.Question.PhuongAnC,
            item.Question.PhuongAnD, item.Question.PhuongAnDung, item.Question.GiaiThich
        }), Json);
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = InsertSql;
            command.CommandTimeout = 60;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            Add(command, "@examId", examId, DbType.Int32);
            Add(command, "@kind", kind, DbType.AnsiString, 16);
            Add(command, "@learnerId", learnerId, DbType.Int32);
            Add(command, "@attemptGuid", attemptGuid, DbType.Guid);
            Add(command, "@durationSeconds", durationSeconds, DbType.Int32);
            Add(command, "@groupCount", groups.Count, DbType.Int32);
            Add(command, "@groups", groupJson, DbType.String, -1);
            Add(command, "@questions", questionJson, DbType.String, -1);
            var watch = Stopwatch.StartNew();
            try
            {
                await using var reader = await command.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new ExamProblem("INVALID_EXAM_STRUCTURE", "Không thể tạo bản chụp đề thi.", 409);
                return (reader.GetInt32(0), reader.GetDouble(1));
            }
            finally
            {
                watch.Stop();
                profile.CountManualSqlCommand("snapshotWrite/openJson", 8, watch.Elapsed);
            }
        }
        finally { if (openedHere) await connection.CloseAsync(); }
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value,
        DbType type, int size = 0)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        if (size != 0) parameter.Size = size;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
