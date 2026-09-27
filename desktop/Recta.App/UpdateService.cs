using System.Diagnostics;
using System.Text.Json;

namespace Recta.App;

// 检查更新:GitHub Releases 最新版 → 版本比对 → 下载 Setup 到安装目录 → 静默执行。
// 资产命名与 packaging/Recta.iss 的 OutputBaseFilename 约定一致:
//   Recta-v{版本}-win-x64-Setup.exe
// Inno(AppId 固定)静默升级时沿用已登记的安装目录,任意自定义安装位(如 F:\Recta)均可原地更新。
public static class UpdateService
{
    public const string Repo = "Furina-1314/Recta";

    private static readonly HttpClient Http = CreateHttp();

    /// <summary>最新 Release 中的 Windows x64 安装包。</summary>
    public sealed record ReleaseInfo(string Tag, Version LatestVersion,
                                     string AssetName, string AssetUrl, long AssetSize);

    /// <summary>是否运行于安装器登记的安装目录(存在卸载器数据)。开发构建不提供自动更新。</summary>
    public static bool IsInstalledBuild =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.dat"));

    /// <summary>安装包落位:安装文件夹(用户要求,同时便于留档重装)。</summary>
    public static string DownloadDir => AppContext.BaseDirectory;

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"Recta-App/{CrashLog.AppVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>当前应用版本(发布时经 -p:Version 写入程序集;开发构建回退 0.0.0 视为待更新)。
    /// SDK 会在版本后追加 "+源修订哈希",解析时剥去。</summary>
    public static Version CurrentVersion =>
        ParseVersion(CrashLog.AppVersion) ?? new Version(0, 0, 0);

    public static bool IsNewer(Version latest) => latest > CurrentVersion;

    /// <returns>null 表示未找到可用的最新版(无资产/版本号不可解析)。</returns>
    public static async Task<ReleaseInfo?> CheckLatestAsync(CancellationToken ct)
    {
        try
        {
            return await CheckViaApiAsync(ct);
        }
        catch (HttpRequestException) when (!ct.IsCancellationRequested)
        {
            // GitHub 匿名 API 配额有限(60 次/时/IP),共享出口 IP 会 403。
            // 回退:releases/latest 页面重定向不受配额限制,按发布约定构造资产地址。
            return await CheckViaRedirectAsync(ct);
        }
    }

    private static async Task<ReleaseInfo?> CheckViaApiAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(
            $"https://api.github.com/repos/{Repo}/releases/latest", ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var latest))
        {
            return null;
        }
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith("-win-x64-Setup.exe", StringComparison.OrdinalIgnoreCase))
            {
                return new ReleaseInfo(tag, latest, name,
                    asset.GetProperty("browser_download_url").GetString() ?? "",
                    asset.GetProperty("size").GetInt64());
            }
        }
        return null;
    }

    private static async Task<ReleaseInfo?> CheckViaRedirectAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(
            $"https://github.com/{Repo}/releases/latest",
            HttpCompletionOption.ResponseHeadersRead, ct);
        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? "";
        var marker = "/tag/";
        var markerIndex = finalUrl.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }
        var tag = finalUrl[(markerIndex + marker.Length)..];
        if (!TryParseVersion(tag, out var latest))
        {
            return null;
        }
        // 命名与 packaging/Recta.iss 的 OutputBaseFilename 保持一致。
        var name = $"Recta-v{latest}-win-x64-Setup.exe";
        return new ReleaseInfo(tag, latest, name,
            $"https://github.com/{Repo}/releases/download/v{latest}/{name}", 0);
    }

    public static async Task<string> DownloadAsync(ReleaseInfo release,
        IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(DownloadDir);
        var destPath = Path.Combine(DownloadDir, release.AssetName);

        using var response = await Http.GetAsync(release.AssetUrl,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, 81920, useAsync: true);

        var total = response.Content.Headers.ContentLength ?? release.AssetSize;
        long written = 0;
        var buffer = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            if (total > 0)
            {
                progress?.Report(written / (double)total);
            }
        }
        return destPath;
    }

    /// <summary>静默升级:不弹向导、强关运行中的旧实例;调用方随后自行退出交出文件锁。</summary>
    public static void LaunchInstaller(string setupPath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/SILENT /FORCECLOSEAPPLICATIONS",
            UseShellExecute = true,
        });
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        var parsed = text is null ? null : ParseVersion(text);
        version = parsed ?? new Version(0, 0, 0);
        return parsed is not null;
    }

    /// <summary>容错解析:"v1.2.3" / "1.2.3+abc123"(剥构建元数据) → Version;不可解析返回 null。</summary>
    private static Version? ParseVersion(string text)
    {
        var core = text.TrimStart('v', 'V');
        var plus = core.IndexOf('+');
        if (plus >= 0)
        {
            core = core[..plus];
        }
        return Version.TryParse(core, out var version) && version.Major >= 0 ? version : null;
    }
}
