using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace STO123.Services.Admin;

public sealed class CourseImageStorage(BlobServiceClient blobs, IConfiguration configuration)
{
    private const string Prefix = "course-images/";
    private const string PublicPrefix = "/api/public/course-images/";
    private readonly BlobContainerClient container = blobs.GetBlobContainerClient(
        configuration["AzureBlob:ContainerName"] ??
        throw new InvalidOperationException("AzureBlob:ContainerName is missing."));

    public async Task<string> UploadAsync(Stream image, string extension, string contentType, CancellationToken ct)
    {
        var fileName = $"{Guid.NewGuid():N}.{extension}";
        await container.GetBlobClient(Prefix + fileName).UploadAsync(image, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
        }, ct);
        return PublicPrefix + fileName;
    }

    public async Task<(Stream Stream, string ContentType)?> OpenAsync(string fileName, CancellationToken ct)
    {
        if (!SafeFileName(fileName)) return null;
        var blob = container.GetBlobClient(Prefix + fileName);
        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            return (await blob.OpenReadAsync(cancellationToken: ct), properties.Value.ContentType);
        }
        catch (RequestFailedException error) when (error.Status == 404) { return null; }
    }

    public async Task DeleteAsync(string publicPath, CancellationToken ct)
    {
        if (TryReference(publicPath, out var reference))
            await container.GetBlobClient(reference).DeleteIfExistsAsync(cancellationToken: ct);
    }

    public static bool TryReference(string? publicPath, out string reference)
    {
        reference = string.Empty;
        if (publicPath?.StartsWith(PublicPrefix, StringComparison.Ordinal) != true) return false;
        var fileName = publicPath[PublicPrefix.Length..];
        if (!SafeFileName(fileName)) return false;
        reference = Prefix + fileName;
        return true;
    }

    private static bool SafeFileName(string value)
    {
        var extension = Path.GetExtension(value).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(value);
        return value == Path.GetFileName(value) && stem.Length == 32 &&
            stem.All(character => char.IsAsciiHexDigit(character)) && extension is ".jpg" or ".png" or ".webp";
    }
}
