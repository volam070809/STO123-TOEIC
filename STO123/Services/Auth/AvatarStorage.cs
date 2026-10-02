using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace STO123.Services.Auth;

public sealed class AvatarStorage(BlobServiceClient blobs, IConfiguration configuration)
{
    private const string ProviderMetadata = "provideravatar";
    private readonly BlobContainerClient container = blobs.GetBlobContainerClient(
        configuration["AzureBlob:ContainerName"] ??
        throw new InvalidOperationException("AzureBlob:ContainerName is missing."));

    public static bool IsCustomReference(int userId, string? reference) =>
        reference?.StartsWith($"avatars/{userId}/", StringComparison.Ordinal) == true;

    public static string? Version(string? reference) =>
        reference is null ? null : Path.GetFileNameWithoutExtension(reference);

    public static bool IsProviderUrl(string? value) =>
        value is { Length: <= 512 } &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public static string? ValidProviderUrl(string? value) => IsProviderUrl(value) ? value : null;

    public async Task UpdateProviderUrlAsync(string reference, string? providerUrl, CancellationToken ct)
    {
        var blob = container.GetBlobClient(reference);
        var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
        var metadata = new Dictionary<string, string>(properties.Value.Metadata, StringComparer.OrdinalIgnoreCase);
        if (IsProviderUrl(providerUrl))
            metadata[ProviderMetadata] = Convert.ToBase64String(Encoding.UTF8.GetBytes(providerUrl!));
        else
            metadata.Remove(ProviderMetadata);
        await blob.SetMetadataAsync(metadata, conditions: new BlobRequestConditions
        { IfMatch = properties.Value.ETag }, cancellationToken: ct);
    }

    public async Task<string?> ProviderUrlAsync(int userId, string? reference, CancellationToken ct)
    {
        if (!IsCustomReference(userId, reference)) return IsProviderUrl(reference) ? reference : null;
        try
        {
            var properties = await container.GetBlobClient(reference!).GetPropertiesAsync(cancellationToken: ct);
            if (!properties.Value.Metadata.TryGetValue(ProviderMetadata, out var encoded)) return null;
            var url = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return IsProviderUrl(url) ? url : null;
        }
        catch (RequestFailedException error) when (error.Status == 404) { return null; }
        catch (FormatException) { return null; }
    }

    public async Task<string> UploadAsync(int userId, Stream image, string extension, string contentType,
        string? providerUrl, CancellationToken ct)
    {
        var path = $"avatars/{userId}/{Guid.NewGuid():N}.{extension}";
        var metadata = new Dictionary<string, string>();
        if (IsProviderUrl(providerUrl))
            metadata[ProviderMetadata] = Convert.ToBase64String(Encoding.UTF8.GetBytes(providerUrl!));
        await container.GetBlobClient(path).UploadAsync(image, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Metadata = metadata
        }, ct);
        return path;
    }

    public Task DeleteAsync(string reference, CancellationToken ct) =>
        container.GetBlobClient(reference).DeleteIfExistsAsync(cancellationToken: ct);

    public async Task<(Stream Stream, string ContentType)?> OpenAsync(string reference, CancellationToken ct)
    {
        var blob = container.GetBlobClient(reference);
        try
        {
            var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
            return (await blob.OpenReadAsync(cancellationToken: ct), properties.Value.ContentType);
        }
        catch (RequestFailedException error) when (error.Status == 404) { return null; }
    }
}
