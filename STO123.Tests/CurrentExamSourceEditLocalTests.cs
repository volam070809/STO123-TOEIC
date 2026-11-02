using System.Security.Claims;
using System.Text.RegularExpressions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using STO123.Controllers;
using STO123.Models;
using STO123.Services.Exam;

namespace STO123.Tests;

public sealed class CurrentExamSourceEditLocalTests
{
    [Fact]
    public void Legacy_version_question_without_difficulty_remains_readable()
    {
        var json = """
            {"id":1,"part":5,"order":1,"partOrder":1,"groupId":null,
             "text":"Legacy","a":"A","b":"B","c":"C","d":"D",
             "correct":"A","explanation":"Old"}
            """;
        var question = System.Text.Json.JsonSerializer.Deserialize<VersionQuestion>(json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal((byte)2, question?.Difficulty);
    }

    [Fact]
    public async Task Published_shared_source_updates_both_current_exams_and_preserves_old_snapshots()
    {
        if (Environment.GetEnvironmentVariable("STO123_RUN_LOCAL_EXAM_EDIT_TEST") != "1") return;
        var name = "STO123_EditTest_" + Guid.NewGuid().ToString("N");
        var master = new SqlConnectionStringBuilder {
            DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = "master",
            IntegratedSecurity = true, TrustServerCertificate = true
        }.ConnectionString;
        var database = new SqlConnectionStringBuilder(master) { InitialCatalog = name }.ConnectionString;
        var containerName = "sto123edit" + Guid.NewGuid().ToString("N")[..16];
        var blobs = new BlobServiceClient("UseDevelopmentStorage=true");
        var container = blobs.GetBlobContainerClient(containerName);
        await Execute(master, $"CREATE DATABASE [{name}]");
        try
        {
            var root = Directory.GetCurrentDirectory();
            while (!File.Exists(Path.Combine(root, "DB_TOEIC123_ver2.sql")))
                root = Directory.GetParent(root)?.FullName ??
                    throw new FileNotFoundException("Không tìm thấy schema SQL.");
            var schema = (await File.ReadAllTextAsync(Path.Combine(root, "DB_TOEIC123_ver2.sql")))
                .Replace("USE DB_TOEIC123_ver2;", $"USE [{name}];");
            foreach (var batch in Regex.Split(schema, @"(?im)^\s*GO\s*$"))
                if (!string.IsNullOrWhiteSpace(batch)) await Execute(database, batch);
            await container.CreateIfNotExistsAsync();
            await Upload(container, "test/audio.mp3", "audio/mpeg");
            await Upload(container, "test/new-audio.mp3", "audio/mpeg");
            await Upload(container, "test/image.png", "image/png");
            var services = new ServiceCollection();
            services.AddLogging(); services.AddMemoryCache();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ExamMedia:ContainerName"] = containerName }).Build());
            services.AddSingleton(blobs);
            services.AddDbContext<ToeicDbContext>(options => options.UseSqlServer(database));
            services.AddScoped<ExamPartLookup>(); services.AddScoped<RandomStartProfiler>();
            services.AddScoped<ExamGenerationService>(); services.AddScoped<ExamVersionStore>();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ToeicDbContext>();
            var store = scope.ServiceProvider.GetRequiredService<ExamVersionStore>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            await Execute(database, """
                INSERT INTO dbo.NguoiDung(HoTen, Email, VaiTro, TrangThai)
                VALUES (N'Test Admin', 'edit@test.local', 'ADMIN_NOI_DUNG', 'HOAT_DONG');
                INSERT INTO dbo.NguoiDung(HoTen, Email, VaiTro, TrangThai)
                VALUES (N'Test Learner', 'learner@test.local', 'HOC_VIEN', 'HOAT_DONG');
                """);
            var adminId = await db.NguoiDung.Where(x => x.VaiTro == "ADMIN_NOI_DUNG")
                .Select(x => x.MaNguoiDung).SingleAsync();
            var learnerId = await db.NguoiDung.Where(x => x.VaiTro == "HOC_VIEN")
                .Select(x => x.MaNguoiDung).SingleAsync();
            var partIds = await db.PartTOEIC.AsNoTracking()
                .ToDictionaryAsync(x => x.SoPart, x => x.MaPart);
            var ordered = new List<CauHoi>();
            void AddUnit(int part, int count)
            {
                NguLieu? resource = part == 5 ? null : new NguLieu {
                    NoiDungNguLieu = part == 6 ? "[1] [2] [3] [4]" : $"Resource Part {part}",
                    DuongDanAudio = part is 1 or 2 or 3 or 4 ? "test/audio.mp3" : null,
                    DuongDanAnh = part == 1 ? "test/image.png" : null
                };
                if (resource is not null) db.NguLieu.Add(resource);
                if (part == 7)
                    db.NguLieuTaiLieu.Add(new NguLieuTaiLieu {
                        MaNguLieuNavigation = resource!, ThuTu = 1,
                        LoaiTaiLieu = "TEXT", NoiDung = "Original document"
                    });
                for (var i = 0; i < count; i++)
                {
                    var question = new CauHoi {
                        MaPart = partIds[part], NoiDung = $"Part {part} question {ordered.Count + 1}",
                        PhuongAnA = "A", PhuongAnB = "B", PhuongAnC = "C",
                        PhuongAnD = part == 2 ? null : "D", PhuongAnDung = "A",
                        DoKho = 2, GiaiThich = "Original explanation",
                        TrangThai = ExamCore.Published, LoaiCauHoi = "DE_THI"
                    };
                    db.CauHoi.Add(question);
                    if (resource is not null)
                        db.NhomCauHoi.Add(new NhomCauHoi {
                            MaCauHoiNavigation = question, MaNguLieuNavigation = resource, ThuTu = i + 1
                        });
                    ordered.Add(question);
                }
            }
            for (var i = 0; i < 6; i++) AddUnit(1, 1);
            for (var i = 0; i < 25; i++) AddUnit(2, 1);
            for (var i = 0; i < 13; i++) AddUnit(3, 3);
            for (var i = 0; i < 10; i++) AddUnit(4, 3);
            for (var i = 0; i < 30; i++) AddUnit(5, 1);
            for (var i = 0; i < 4; i++) AddUnit(6, 4);
            for (var i = 0; i < 27; i++) AddUnit(7, 2);
            Assert.Equal(200, ordered.Count);
            await db.SaveChangesAsync();
            var exams = new[] { 1, 2 }.Select(index => new DeThi {
                TenDe = $"Shared exam {index}", LoaiDe = "DE_THI",
                ThoiGianLamBai = 120, TrangThai = "CLOSE", MaNguoiSoan = adminId
            }).ToArray();
            foreach (var exam in exams)
            {
                db.DeThi.Add(exam);
                await db.SaveChangesAsync();
            }
            foreach (var exam in exams)
                db.CauHoiDeThi.AddRange(ordered.Select((q, i) => new CauHoiDeThi {
                    MaDeThi = exam.MaDeThi, MaCauHoi = q.MaCauHoi, ThuTu = i + 1
                }));
            await db.SaveChangesAsync();
            var oldRows = new List<PhienBanDeThi>();
            foreach (var exam in exams)
                oldRows.Add(await store.PublishAsync(exam.MaDeThi, null, Guid.NewGuid(), adminId,
                    CancellationToken.None));
            foreach (var exam in exams) exam.TrangThai = "OPEN";
            await db.SaveChangesAsync();
            var groupQuestion = ordered.First(q => q.MaPart == partIds[3]);
            var group = await db.NhomCauHoi.AsNoTracking()
                .Where(x => x.MaCauHoi == groupQuestion.MaCauHoi)
                .Select(x => x.MaNguLieu).SingleAsync();
            var groupIds = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaNguLieu == group)
                .OrderBy(x => x.ThuTu).Select(x => x.MaCauHoi).ToArrayAsync();
            var oldSnapshots = await Task.WhenAll(oldRows.Select(row =>
                store.ReadAsync(row, CancellationToken.None)));
            var compact = new CompactExamStore(db, store);
            var attemptId = await compact.CreateVersionedAsync(learnerId, oldRows[0],
                oldSnapshots[0], ExamCore.Mock, CancellationToken.None);
            var questionController = new AdminQuestionAuthoringController(db, blobs, config, store) {
                ControllerContext = Context(adminId)
            };
            var changed = Assert.IsType<OkObjectResult>(await questionController.Update(
                groupIds[0], new AdminQuestionAuthoringController.QuestionInput(
                    "Changed question", "New A", "B", "C", "D", "B", 3, "Changed explanation"),
                CancellationToken.None));
            Assert.NotNull(changed.Value);
            db.ChangeTracker.Clear();
            var currentIds = await db.DeThi.AsNoTracking().OrderBy(e => e.MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).ToArrayAsync();
            Assert.All(currentIds, id => Assert.DoesNotContain(id, oldRows.Select(x => (int?)x.MaPhienBanDeThi)));
            foreach (var (exam, old, currentId) in exams.Zip(oldSnapshots, currentIds))
            {
                var row = await db.PhienBanDeThi.AsNoTracking().SingleAsync(v => v.MaPhienBanDeThi == currentId);
                var current = await store.ReadAsync(row, CancellationToken.None);
                Assert.Equal("Changed question", current.Questions.Single(q => q.Id == groupIds[0]).Text);
                Assert.Equal("Original explanation", current.Questions.Single(q => q.Id == groupIds[1]).Explanation);
                Assert.NotEqual("Changed question", old.Questions.Single(q => q.Id == groupIds[0]).Text);
                Assert.Equal("OPEN", await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == exam.MaDeThi)
                    .Select(e => e.TrangThai).SingleAsync());
            }
            var resumed = await compact.LoadAsync(attemptId, learnerId, SnapshotView.Active,
                true, CancellationToken.None);
            Assert.Equal(oldRows[0].MaPhienBanDeThi, resumed.Attempt.MaPhienBanDeThi);
            Assert.NotEqual("Changed question", resumed.Questions.Single(q =>
                q.MaCauHoiGoc == groupIds[0]).NoiDung);
            Assert.Equal("A", resumed.Questions.Single(q => q.MaCauHoiGoc == groupIds[0]).PhuongAnDung);
            var resourceController = new AdminExamSourceMediaController(db, blobs, config, store) {
                ControllerContext = Context(adminId)
            };
            Assert.IsType<OkObjectResult>(await resourceController.UpdateUnit(group,
                new AdminExamSourceMediaController.UnitResourceRequest(
                    "Changed dialogue", null, "test/audio.mp3", null, []), CancellationToken.None));
            db.ChangeTracker.Clear();
            foreach (var exam in exams)
            {
                var row = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == exam.MaDeThi)
                    .Select(e => e.MaPhienBanHienHanh).SingleAsync();
                var version = await db.PhienBanDeThi.AsNoTracking()
                    .SingleAsync(v => v.MaPhienBanDeThi == row);
                Assert.Equal("Changed dialogue", (await store.ReadAsync(version, CancellationToken.None))
                    .Groups.Single(g => g.Id == group).Text);
            }
            var oldAttemptAfterGroupEdit = await compact.LoadAsync(attemptId, learnerId,
                SnapshotView.Active, true, CancellationToken.None);
            Assert.Equal("Resource Part 3", oldAttemptAfterGroupEdit.Groups
                .Single(g => g.MaNhomLuotLam == group).NoiDungNguLieu);
            var latestId = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == exams[0].MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).SingleAsync();
            var latestRow = await db.PhienBanDeThi.AsNoTracking()
                .SingleAsync(v => v.MaPhienBanDeThi == latestId);
            var latestSnapshot = await store.ReadAsync(latestRow, CancellationToken.None);
            var newAttemptId = await compact.CreateVersionedAsync(learnerId, latestRow,
                latestSnapshot, ExamCore.Mock, CancellationToken.None);
            var newAttempt = await compact.LoadAsync(newAttemptId, learnerId,
                SnapshotView.Active, true, CancellationToken.None);
            Assert.Equal("Changed question", newAttempt.Questions
                .Single(q => q.MaCauHoiGoc == groupIds[0]).NoiDung);
            Assert.Equal("Changed dialogue", newAttempt.Groups
                .Single(g => g.MaNhomLuotLam == group).NoiDungNguLieu);
            Assert.IsType<OkObjectResult>(await resourceController.UpdateUnit(group,
                new AdminExamSourceMediaController.UnitResourceRequest(
                    "Changed dialogue", null, "test/new-audio.mp3", null, []), CancellationToken.None));
            db.ChangeTracker.Clear();
            var part6Id = ordered.First(q => q.MaPart == partIds[6]).MaCauHoi;
            var part6Group = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaCauHoi == part6Id)
                .Select(x => x.MaNguLieu).SingleAsync();
            Assert.IsType<OkObjectResult>(await resourceController.UpdateUnit(part6Group,
                new AdminExamSourceMediaController.UnitResourceRequest(
                    "Passage [1] then [2], [3], [4]", null, null, null, []), CancellationToken.None));
            db.ChangeTracker.Clear();
            var part7Id = ordered.First(q => q.MaPart == partIds[7]).MaCauHoi;
            var part7Group = await db.NhomCauHoi.AsNoTracking().Where(x => x.MaCauHoi == part7Id)
                .Select(x => x.MaNguLieu).SingleAsync();
            var table = """{"version":1,"title":"Table","headers":["A","B"],"rows":[["1","2"]],"legacy":"keep"}""";
            var email = """{"version":1,"from":"a@test.local","to":"b@test.local","date":"today","subject":"Subject","body":"Email body"}""";
            var chat = """{"version":1,"messages":[{"sender":"A","time":"09:00","text":"Hello"}]}""";
            var form = """{"version":1,"title":"Form","fields":[{"label":"Name","value":"Lan"}]}""";
            Assert.IsType<OkObjectResult>(await resourceController.UpdateUnit(part7Group,
                new AdminExamSourceMediaController.UnitResourceRequest(
                    null, null, null, null, [
                        new("TABLE", table, null), new("EMAIL", email, null), new("CHAT", chat, null)
                    ]), CancellationToken.None));
            db.ChangeTracker.Clear();
            var part7Pointer = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == exams[0].MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).SingleAsync();
            var part7Row = await db.PhienBanDeThi.AsNoTracking()
                .SingleAsync(v => v.MaPhienBanDeThi == part7Pointer);
            var structured = (await store.ReadAsync(part7Row, CancellationToken.None))
                .Groups.Single(g => g.Id == part7Group).Documents;
            Assert.Equal(["TABLE", "EMAIL", "CHAT"], structured.Select(d => d.Type));
            Assert.Equal(table, structured[0].Content);
            Assert.IsType<OkObjectResult>(await resourceController.UpdateUnit(part7Group,
                new AdminExamSourceMediaController.UnitResourceRequest(
                    null, null, null, null, [
                        new("FORM", form, null), new("TEXT", "Plain text", null),
                        new("IMAGE", null, "test/image.png")
                    ]), CancellationToken.None));
            db.ChangeTracker.Clear();
            var latestPart7Pointer = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == exams[0].MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).SingleAsync();
            var latestPart7Row = await db.PhienBanDeThi.AsNoTracking()
                .SingleAsync(v => v.MaPhienBanDeThi == latestPart7Pointer);
            var secondSet = (await store.ReadAsync(latestPart7Row, CancellationToken.None))
                .Groups.Single(g => g.Id == part7Group).Documents;
            Assert.Equal(["FORM", "TEXT", "IMAGE"], secondSet.Select(d => d.Type));
            Assert.Equal("Plain text", secondSet[1].Content);
            Assert.NotNull(secondSet[2].ImagePath);
            async Task EditConcurrently(string content)
            {
                using var inner = provider.CreateScope();
                var innerDb = inner.ServiceProvider.GetRequiredService<ToeicDbContext>();
                var innerStore = inner.ServiceProvider.GetRequiredService<ExamVersionStore>();
                await innerStore.ApplySourceEditAsync([groupIds[0]], async token => {
                    var question = await innerDb.CauHoi.SingleAsync(q =>
                        q.MaCauHoi == groupIds[0], token);
                    question.NoiDung = content;
                }, adminId, CancellationToken.None);
            }
            await Task.WhenAll(EditConcurrently("Concurrent A"), EditConcurrently("Concurrent B"));
            db.ChangeTracker.Clear();
            var finalText = await db.CauHoi.AsNoTracking().Where(q => q.MaCauHoi == groupIds[0])
                .Select(q => q.NoiDung).SingleAsync();
            Assert.Contains(finalText, new[] { "Concurrent A", "Concurrent B" });
            foreach (var exam in exams)
            {
                var pointer = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == exam.MaDeThi)
                    .Select(e => e.MaPhienBanHienHanh).SingleAsync();
                var row = await db.PhienBanDeThi.AsNoTracking()
                    .SingleAsync(v => v.MaPhienBanDeThi == pointer);
                Assert.Equal(finalText, (await store.ReadAsync(row, CancellationToken.None))
                    .Questions.Single(q => q.Id == groupIds[0]).Text);
            }
            var beforeFailure = await db.DeThi.AsNoTracking().OrderBy(e => e.MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).ToArrayAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => store.ApplySourceEditAsync(groupIds,
                async token => {
                    var resource = await db.NguLieu.SingleAsync(x => x.MaNguLieu == group, token);
                    resource.DuongDanAudio = "test/missing.mp3";
                }, adminId, CancellationToken.None));
            db.ChangeTracker.Clear();
            Assert.Equal(beforeFailure, await db.DeThi.AsNoTracking().OrderBy(e => e.MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).ToArrayAsync());
            Assert.Equal("test/new-audio.mp3", await db.NguLieu.AsNoTracking()
                .Where(x => x.MaNguLieu == group).Select(x => x.DuongDanAudio).SingleAsync());
            var unreachableOptions = new BlobClientOptions();
            unreachableOptions.Retry.MaxRetries = 0;
            unreachableOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(2);
            var unreachableBlobs = new BlobServiceClient(
                new Uri("http://127.0.0.1:1/devstoreaccount1"), unreachableOptions);
            var unreachableStore = new ExamVersionStore(db, unreachableBlobs, config,
                scope.ServiceProvider.GetRequiredService<ExamGenerationService>(),
                scope.ServiceProvider.GetRequiredService<IMemoryCache>());
            var beforeBlobFailure = await db.DeThi.AsNoTracking().OrderBy(e => e.MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).ToArrayAsync();
            var versionsBeforeBlobFailure = await db.PhienBanDeThi.CountAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => unreachableStore.ApplySourceEditAsync(groupIds,
                async token => {
                    var question = await db.CauHoi.SingleAsync(x => x.MaCauHoi == groupIds[0], token);
                    question.NoiDung = "Blob failure must roll back";
                }, adminId, CancellationToken.None));
            db.ChangeTracker.Clear();
            Assert.Equal(beforeBlobFailure, await db.DeThi.AsNoTracking().OrderBy(e => e.MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).ToArrayAsync());
            Assert.Equal(versionsBeforeBlobFailure, await db.PhienBanDeThi.CountAsync());
            Assert.Equal(finalText, await db.CauHoi.AsNoTracking().Where(q => q.MaCauHoi == groupIds[0])
                .Select(q => q.NoiDung).SingleAsync());
            var secondExam = await db.DeThi.SingleAsync(e => e.MaDeThi == exams[1].MaDeThi);
            secondExam.ThoiGianLamBai = 119; // Isolated fault: second affected exam fails validation.
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var beforePartialFailure = await db.DeThi.AsNoTracking().OrderBy(e => e.MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).ToArrayAsync();
            var beforeVersionCount = await db.PhienBanDeThi.CountAsync();
            var beforeText = await db.CauHoi.AsNoTracking().Where(q => q.MaCauHoi == groupIds[0])
                .Select(q => q.NoiDung).SingleAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => store.ApplySourceEditAsync(groupIds,
                async token => {
                    var question = await db.CauHoi.SingleAsync(x => x.MaCauHoi == groupIds[0], token);
                    question.NoiDung = "This must roll back";
                }, adminId, CancellationToken.None));
            db.ChangeTracker.Clear();
            Assert.Equal(beforePartialFailure, await db.DeThi.AsNoTracking().OrderBy(e => e.MaDeThi)
                .Select(e => e.MaPhienBanHienHanh).ToArrayAsync());
            Assert.Equal(beforeVersionCount, await db.PhienBanDeThi.CountAsync());
            Assert.Equal(beforeText, await db.CauHoi.AsNoTracking().Where(q => q.MaCauHoi == groupIds[0])
                .Select(q => q.NoiDung).SingleAsync());
            var manual = new AdminManualExamController(db,
                scope.ServiceProvider.GetRequiredService<ExamGenerationService>(), store) {
                ControllerContext = Context(adminId)
            };
            Assert.IsType<OkObjectResult>(await manual.CreateDraft(
                new AdminManualExamController.DraftRequest("Draft progress"), CancellationToken.None));
            db.ChangeTracker.Clear();
            var draftId = await db.DeThi.AsNoTracking().Where(e => e.TenDe == "Draft progress")
                .Select(e => e.MaDeThi).SingleAsync();
            var created = Assert.IsType<OkObjectResult>(await questionController.CreateInExam(draftId,
                new AdminQuestionAuthoringController.UnitInput(
                    5, "DE_THI", null, null, null, null, [],
                    [new("New draft question", "A", "B", "C", "D", "A", 2, "Explanation")]),
                CancellationToken.None));
            Assert.NotNull(created.Value);
            db.ChangeTracker.Clear();
            var draftLinkId = await db.CauHoiDeThi.AsNoTracking().Where(x => x.MaDeThi == draftId)
                .Select(x => x.MaCauHoi).SingleAsync();
            var reopened = Assert.IsType<OkObjectResult>(await manual.GetDraft(draftId,
                CancellationToken.None));
            Assert.NotNull(reopened.Value);
            Assert.IsType<ConflictObjectResult>(await manual.PublishDraft(draftId, CancellationToken.None));
            Assert.IsType<OkObjectResult>(await manual.RemoveUnit(draftId, draftLinkId,
                CancellationToken.None));
            db.ChangeTracker.Clear();
            Assert.False(await db.CauHoiDeThi.AsNoTracking().AnyAsync(x => x.MaDeThi == draftId));
            Assert.True(await db.CauHoi.AsNoTracking().AnyAsync(x => x.MaCauHoi == draftLinkId &&
                x.LoaiCauHoi == "DE_THI"));
            Assert.IsType<OkObjectResult>(await questionController.Update(draftLinkId,
                new AdminQuestionAuthoringController.QuestionInput(
                    "Independent edited", "A", "B", "C", "D", "C", 1, "Independent explanation"),
                CancellationToken.None));
            db.ChangeTracker.Clear();
            Assert.Equal("Independent edited", await db.CauHoi.AsNoTracking()
                .Where(x => x.MaCauHoi == draftLinkId).Select(x => x.NoiDung).SingleAsync());
            Assert.IsType<OkObjectResult>(await manual.CreateDraft(
                new AdminManualExamController.DraftRequest("Complete draft"), CancellationToken.None));
            db.ChangeTracker.Clear();
            var completeId = await db.DeThi.AsNoTracking().Where(e => e.TenDe == "Complete draft")
                .Select(e => e.MaDeThi).SingleAsync();
            db.CauHoiDeThi.AddRange(ordered.Select((q, index) => new CauHoiDeThi {
                MaDeThi = completeId, MaCauHoi = q.MaCauHoi, ThuTu = index + 1
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            Assert.IsType<OkObjectResult>(await manual.PublishDraft(completeId, CancellationToken.None));
            db.ChangeTracker.Clear();
            var complete = await db.DeThi.AsNoTracking().Where(e => e.MaDeThi == completeId)
                .Select(e => new { e.MaPhienBanHienHanh, e.TrangThai }).SingleAsync();
            Assert.NotNull(complete.MaPhienBanHienHanh);
            Assert.Equal("CLOSE", complete.TrangThai);
        }
        finally
        {
            await container.DeleteIfExistsAsync();
            await Execute(master, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];");
        }
    }

    private static ControllerContext Context(int userId) => new() {
        HttpContext = new DefaultHttpContext {
            User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
        }
    };

    private static async Task Execute(string connectionString, string command)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var sql = new SqlCommand(command, connection) { CommandTimeout = 120 };
        await sql.ExecuteNonQueryAsync();
    }

    private static async Task Upload(Azure.Storage.Blobs.BlobContainerClient container,
        string path, string contentType)
    {
        await container.GetBlobClient(path).UploadAsync(
            new BinaryData(new byte[] { 1, 2, 3 }),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } });
    }
}
