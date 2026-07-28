using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage;
using PixelDone.Core;

namespace PixelDone.Windows.Services;

public sealed class WindowsAttachmentService(string attachmentDirectory)
{
    public async Task<TodoAttachment> ImportAsync(
        string sourcePath,
        string todoId,
        TodoAttachment? existing,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        if (bytes is not { Length: > 0 and <= 10 * 1024 * 1024 })
        {
            throw new InvalidOperationException("Images must be no larger than 10 MiB.");
        }

        var (mimeType, extension) = Detect(bytes);
        var file = await StorageFile.GetFileFromPathAsync(sourcePath);
        using (var stream = await file.OpenReadAsync())
        {
            _ = await BitmapDecoder.CreateAsync(stream);
        }

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var attachmentId = existing?.Id ?? Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(attachmentDirectory);
        var target = Path.Combine(
            attachmentDirectory,
            $"{todoId}-{attachmentId}-{sha256[..16]}.{extension}");
        await File.WriteAllBytesAsync(target, bytes, cancellationToken);
        return new TodoAttachment(
            attachmentId,
            todoId,
            target,
            existing?.RemotePath,
            sha256,
            mimeType,
            bytes.LongLength,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            existing?.RemoteVersion is null ? SyncState.LocalOnly : SyncState.Dirty,
            existing?.RemoteVersion);
    }

    public static void DeleteLocalFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static (string MimeType, string Extension) Detect(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff
            ? ("image/jpeg", "jpg")
            : bytes.Length >= 8 &&
              bytes.AsSpan(0, 8).SequenceEqual(
                  new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                ? ("image/png", "png")
                : bytes.Length >= 12 &&
                  bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                  bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)
                    ? ("image/webp", "webp")
                    : throw new InvalidOperationException(
                        "The selected file must be JPEG, PNG, or WebP.");
}
