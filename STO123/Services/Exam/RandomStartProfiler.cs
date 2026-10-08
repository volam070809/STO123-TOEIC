using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace STO123.Services.Exam;

// Scoped to a request and enabled only for Development start profiling.
public sealed class RandomStartProfiler(ILogger<RandomStartProfiler> logger)
{
    private readonly Dictionary<string, long> ticks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Count, int MaxParameters, double ExecutionMs)> commands = new(StringComparer.Ordinal);
    private readonly AsyncLocal<string?> currentPhase = new();
    private Stopwatch? total;
    private string examKind = ExamCore.Mock;
    private int sqlCommands;
    private int saveChanges;
    private int blobChecks;
    private int blobCacheHits;
    private int insertedRows;

    public bool Enabled => total is not null;
    public IDisposable Begin(bool enabled, string kind = ExamCore.Mock)
    {
        if (!enabled) return EmptyScope.Instance;
        examKind = kind;
        ticks.Clear();
        commands.Clear();
        sqlCommands = 0;
        saveChanges = 0;
        blobChecks = 0;
        blobCacheHits = 0;
        insertedRows = 0;
        total = Stopwatch.StartNew();
        return new EndScope(this);
    }

    public IDisposable Phase(string name)
    {
        if (!Enabled) return EmptyScope.Instance;
        var previous = currentPhase.Value;
        currentPhase.Value = name;
        return new PhaseScope(this, name, previous);
    }

    public void CountSqlCommand(DbCommand command)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref sqlCommands);
        var key = SqlKind(command);
        var value = commands.GetValueOrDefault(key);
        commands[key] = (value.Count + 1, Math.Max(value.MaxParameters, command.Parameters.Count), value.ExecutionMs);
    }

    public void CompleteSqlCommand(DbCommand command, TimeSpan duration)
    {
        if (!Enabled) return;
        var key = SqlKind(command);
        var value = commands.GetValueOrDefault(key);
        commands[key] = (value.Count, value.MaxParameters, value.ExecutionMs + duration.TotalMilliseconds);
    }

    public void CountManualSqlCommand(string kind, int parameterCount, TimeSpan duration)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref sqlCommands);
        var value = commands.GetValueOrDefault(kind);
        commands[kind] = (value.Count + 1, Math.Max(value.MaxParameters, parameterCount),
            value.ExecutionMs + duration.TotalMilliseconds);
    }

    private string SqlKind(DbCommand command)
    {
        var sql = command.CommandText;
        var tables = new List<string>(3);
        if (sql.Contains("KetQuaLamBai", StringComparison.OrdinalIgnoreCase)) tables.Add("attempt");
        if (sql.Contains("NhomLuotLam", StringComparison.OrdinalIgnoreCase)) tables.Add("groups");
        if (sql.Contains("CauHoiLuotLam", StringComparison.OrdinalIgnoreCase)) tables.Add("questions");
        var table = tables.Count == 0 ? "other" : string.Join('+', tables);
        return $"{currentPhase.Value ?? "unphased"}/{table}";
    }
    public void CountSaveChanges() { if (Enabled) Interlocked.Increment(ref saveChanges); }
    public void AddMeasuredPhase(string name, TimeSpan duration)
    {
        if (Enabled) Add(name, (long)(duration.TotalSeconds * Stopwatch.Frequency));
    }
    public void CountBlobCheck() { if (Enabled) Interlocked.Increment(ref blobChecks); }
    public void CountBlobCacheHit() { if (Enabled) Interlocked.Increment(ref blobCacheHits); }
    public void SetInsertedRows(int rows) { if (Enabled) insertedRows = rows; }

    private void Add(string name, long elapsedTicks)
    {
        ticks[name] = ticks.GetValueOrDefault(name) + elapsedTicks;
    }

    private void End()
    {
        if (total is null) return;
        total.Stop();
        double Ms(string name) => Math.Round(1000d * ticks.GetValueOrDefault(name) / Stopwatch.Frequency, 2);
        logger.LogInformation(
            "Persisted {Kind} start profile (ms): StartDecision={Decision}, TemplateRead={Template}, SnapshotBuild={Build}, SnapshotWrite={Write}, TimerStart={Timer}, Commit={Commit}, Total={Total}; sqlCommands={SqlCommands}; saveChanges={SaveChanges}; insertedRows={InsertedRows}; blobChecks={BlobChecks}",
            examKind,
            Ms("startDecision"), Ms("templateRead"), Ms("snapshotBuild"), Ms("snapshotWrite"),
            Ms("timerStart"), Ms("commit"), Math.Round(total.Elapsed.TotalMilliseconds, 2),
            sqlCommands, saveChanges, insertedRows, blobChecks);
        logger.LogInformation("Persisted {Kind} SQL command profile: {Commands}", examKind, string.Join(", ",
            commands.OrderBy(pair => pair.Key).Select(pair =>
                $"{pair.Key}={pair.Value.Count}/{Math.Round(pair.Value.ExecutionMs, 2)}ms/maxParams:{pair.Value.MaxParameters}")));
        total = null;
    }

    private sealed class EndScope(RandomStartProfiler owner) : IDisposable
    {
        public void Dispose() => owner.End();
    }

    private sealed class PhaseScope(RandomStartProfiler owner, string name, string? previous) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private int disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                owner.Add(name, Stopwatch.GetTimestamp() - started);
                owner.currentPhase.Value = previous;
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }
}

public sealed class RandomStartCommandInterceptor(RandomStartProfiler profiler) : DbCommandInterceptor
{
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        profiler.CountSqlCommand(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        profiler.CountSqlCommand(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        profiler.CountSqlCommand(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        profiler.CompleteSqlCommand(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        profiler.CompleteSqlCommand(command, eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command,
        CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        profiler.CompleteSqlCommand(command, eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }
}
