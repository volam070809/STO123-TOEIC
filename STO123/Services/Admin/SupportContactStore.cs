using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace STO123.Services.Admin;

public sealed record SupportContactSettings(string? ZaloUrl, bool ZaloEnabled,
    string? MessengerUrl, bool MessengerEnabled, string? Revision);
public sealed record SaveSupportContactRequest(string? ZaloUrl, bool ZaloEnabled,
    string? MessengerUrl, bool MessengerEnabled, string? Revision);

public sealed class SupportContactStore
{
    public const string DefaultZalo = "https://zalo.me/0342463560";
    public const string DefaultMessenger = "https://m.me/lam.vo.561222";
    private const string ContainerName = "sto123-app-settings";
    private const string BlobName = "contact-support/v1.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly BlobServiceClient blobs;
    private readonly string containerName;
    public SupportContactStore(BlobServiceClient blobs) : this(blobs, ContainerName) { }
    public SupportContactStore(BlobServiceClient blobs, string containerName)
    {
        this.blobs = blobs;
        this.containerName = containerName;
    }
    private BlobContainerClient Container => blobs.GetBlobContainerClient(containerName);
    private BlobClient Blob => Container.GetBlobClient(BlobName);
    private sealed record Document(string? ZaloUrl, bool ZaloEnabled,
        string? MessengerUrl, bool MessengerEnabled);

    public static string? ValidationError(SaveSupportContactRequest input)
    {
        if (!ValidUrl(input.ZaloUrl, "zalo.me"))
            return "Liên kết Zalo phải dùng HTTPS và có dạng https://zalo.me/<số điện thoại hoặc tài khoản>.";
        if (!ValidUrl(input.MessengerUrl, "m.me"))
            return "Liên kết Messenger phải dùng HTTPS và có dạng https://m.me/<tài khoản>.";
        if (input.ZaloEnabled && string.IsNullOrWhiteSpace(input.ZaloUrl))
            return "Hãy nhập liên kết Zalo trước khi bật hiển thị.";
        if (input.MessengerEnabled && string.IsNullOrWhiteSpace(input.MessengerUrl))
            return "Hãy nhập liên kết Messenger trước khi bật hiển thị.";
        return null;
    }

    private static bool ValidUrl(string? value, string host)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) &&
            uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            uri.AbsolutePath.Length > 1 && !uri.AbsolutePath.Contains("..", StringComparison.Ordinal) &&
            uri.AbsolutePath.Count(c => c == '/') == 1;
    }

    public async Task<SupportContactSettings> ReadAsync(CancellationToken ct)
    {
        try
        {
            var response = await Blob.DownloadContentAsync(ct);
            var data = response.Value.Content.ToObjectFromJson<Document>(Json)
                ?? throw new InvalidDataException("Cấu hình liên hệ trên Blob không hợp lệ.");
            if (ValidationError(new SaveSupportContactRequest(data.ZaloUrl, data.ZaloEnabled,
                    data.MessengerUrl, data.MessengerEnabled, null)) is not null)
                throw new InvalidDataException("Cấu hình liên hệ trên Blob không hợp lệ.");
            return new(data.ZaloUrl, data.ZaloEnabled, data.MessengerUrl,
                data.MessengerEnabled, response.Value.Details.ETag.ToString());
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            return new(DefaultZalo, true, DefaultMessenger, true, null);
        }
    }

    public async Task<SupportContactSettings> SaveAsync(SaveSupportContactRequest input, CancellationToken ct)
    {
        await Container.CreateIfNotExistsAsync(cancellationToken: ct);
        var document = new Document(input.ZaloUrl?.Trim(), input.ZaloEnabled,
            input.MessengerUrl?.Trim(), input.MessengerEnabled);
        var options = new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/json; charset=utf-8" },
            Conditions = input.Revision is null ? new BlobRequestConditions { IfNoneMatch = ETag.All } :
                new BlobRequestConditions { IfMatch = new ETag(input.Revision) }
        };
        await Blob.UploadAsync(BinaryData.FromObjectAsJson(document, Json), options, ct);
        var saved = await ReadAsync(ct);
        if (saved.ZaloUrl != document.ZaloUrl || saved.ZaloEnabled != document.ZaloEnabled ||
            saved.MessengerUrl != document.MessengerUrl || saved.MessengerEnabled != document.MessengerEnabled)
            throw new IOException("Không xác minh được cấu hình liên hệ sau khi lưu.");
        return saved;
    }
}
