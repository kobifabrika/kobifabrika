using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KobiFabrika.Infrastructure.Storage;

/// <summary>
/// Yüklenen dosyalar (logo). Geliştirmede yerel disk; Sprint 8'de canlıda kalıcı volume veya nesne depolama.
/// Dönen yol API kökünden göreli bir URL'dir: "/files/logos/{businessId}/{ad}".
/// </summary>
public interface IFileStorage
{
    Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default);
    Task DeleteAsync(string url, CancellationToken cancellationToken = default);
}

public sealed class StorageOptions
{
    public const string Section = "Storage";
    public const string PublicBasePath = "/files";

    /// <summary>İçerik köküne göre veya mutlak klasör.</summary>
    public string LocalPath { get; set; } = "App_Data/files";
}

public sealed class LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment env) : IFileStorage
{
    public string RootPath { get; } = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.LocalPath));

    public async Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var relative = Path.Combine(folder, fileName);
        var fullPath = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return $"{StorageOptions.PublicBasePath}/{relative.Replace('\\', '/')}";
    }

    public Task DeleteAsync(string url, CancellationToken cancellationToken = default)
    {
        if (url.StartsWith(StorageOptions.PublicBasePath + "/", StringComparison.Ordinal))
        {
            var fullPath = Resolve(url[(StorageOptions.PublicBasePath.Length + 1)..]);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Kök dışına çıkan yolları (../) reddeder.</summary>
    private string Resolve(string relative)
    {
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relative));
        if (!fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Geçersiz dosya yolu.");
        }

        return fullPath;
    }
}
