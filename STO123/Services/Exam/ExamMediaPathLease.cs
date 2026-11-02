using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace STO123.Services.Exam;

// Serializes a source-media reference change with deletion of that Blob path.
public sealed class ExamMediaPathLease : IAsyncDisposable
{
    private readonly SqlConnection connection;
    private readonly string[] resources;

    private ExamMediaPathLease(SqlConnection connection, string[] resources)
    {
        this.connection = connection;
        this.resources = resources;
    }

    public static async Task<ExamMediaPathLease> AcquireAsync(string connectionString,
        string container, string path, CancellationToken ct)
        => await AcquireManyAsync(connectionString, [(container, path)], ct);

    public static async Task<ExamMediaPathLease> AcquireManyAsync(string connectionString,
        IEnumerable<(string Container, string Path)> paths, CancellationToken ct)
    {
        var resources = paths.Select(p => "exam-media:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{p.Container}/{p.Path}"))))
            .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            foreach (var resource in resources)
            {
                await using var command = new SqlCommand(
                    "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
                    "@Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=30000; " +
                    "SELECT @result;", connection);
                command.Parameters.AddWithValue("@resource", resource);
                var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
                if (result < 0) throw new InvalidOperationException("Không thể khóa tệp media để cập nhật an toàn. Vui lòng thử lại.");
            }
            return new(connection, resources);
        }
        catch
        {
            SqlConnection.ClearPool(connection);
            await connection.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var resource in resources.Reverse())
            {
                await using var command = new SqlCommand(
                    "EXEC sys.sp_releaseapplock @Resource=@resource, @LockOwner='Session';", connection);
                command.Parameters.AddWithValue("@resource", resource);
                await command.ExecuteNonQueryAsync();
            }
        }
        catch
        {
            // Closing the SQL session also releases session-owned application locks.
            SqlConnection.ClearPool(connection);
        }
        finally { await connection.DisposeAsync(); }
    }
}
