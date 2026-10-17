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

    /// <summary>Kayıtlı dosyanın içeriği (PDF'e logo basmak için); yoksa null.</summary>
    Task<byte[]?> ReadAsync(string url, CancellationToken cancellationToken = default);
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

    public async Task<byte[]?> ReadAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!url.StartsWith(StorageOptions.PublicBasePath + "/", StringComparison.Ordinal))
        {
            return null;
        }

        var fullPath = Resolve(url[(StorageOptions.PublicBasePath.Length + 1)..]);
        return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, cancellationToken) : null;
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

/// <summary>
/// Herkese açık olmayan dosyalar (geri bildirim ekran görüntüsü). <c>/files</c> altında sunulmaz; sadece yetkili uç okur.
/// Anahtar göreli yoldur ("feedback/{id}.png"). Klasör <c>Storage:PrivatePath</c> (varsayılan App_Data/private).
/// </summary>
public interface IPrivateFileStorage
{
    Task<string> SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class PrivateStorageOptions
{
    public string PrivatePath { get; set; } = "App_Data/private";
}

public sealed class LocalPrivateFileStorage(IOptions<PrivateStorageOptions> options, IHostEnvironment env) : IPrivateFileStorage
{
    public string RootPath { get; } = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.PrivatePath));

    public async Task<string> SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return key;
    }

    public async Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(key);
        return File.Exists(fullPath) ? await File.ReadAllBytesAsync(fullPath, cancellationToken) : null;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullPath = Resolve(key);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        var fullPath = Path.GetFullPath(Path.Combine(RootPath, key));
        if (!fullPath.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Geçersiz dosya yolu.");
        }

        return fullPath;
    }
}
