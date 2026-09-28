using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Services;

internal sealed class UpdateService
{
    private static readonly HttpClient HttpClient = new();

    public async Task<UpdateCheckResult> CheckAsync(UpdateConfiguration settings, CancellationToken cancellationToken)
    {
        if (!settings.CheckOnStartup || string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            return UpdateCheckResult.None;
        }

        try
        {
            var manifestUri = new Uri(new Uri(EnsureTrailingSlash(settings.ServerUrl)),
                $"updates/{Uri.EscapeDataString(string.IsNullOrWhiteSpace(settings.Channel) ? "stable" : settings.Channel)}/latest.json");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.CheckTimeoutSeconds, 2, 30)));
            using var response = await HttpClient.GetAsync(manifestUri, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.None;
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || !IsNewer(manifest.Version) || string.IsNullOrWhiteSpace(manifest.PackageUrl))
            {
                return UpdateCheckResult.None;
            }

            manifest.PackageUrl = new Uri(manifestUri, manifest.PackageUrl).AbsoluteUri;
            return new UpdateCheckResult(manifest);
        }
        catch
        {
            // 更新服务器不可达不能影响现场测试软件的正常启动。
            return UpdateCheckResult.None;
        }
    }

    public async Task DownloadAndStartUpdaterAsync(UpdateManifest manifest, string installDirectory, CancellationToken cancellationToken)
    {
        var updaterSource = Path.Combine(installDirectory, "CeHao.Updater.exe");
        if (!File.Exists(updaterSource))
        {
            throw new FileNotFoundException("未找到 CeHao.Updater.exe，请使用完整发布包重新安装一次。", updaterSource);
        }

        var workDirectory = Path.Combine(Path.GetTempPath(), "CeHao", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        var packageFile = Path.Combine(workDirectory, "package.zip");
        await using (var source = await HttpClient.GetStreamAsync(manifest.PackageUrl, cancellationToken))
        await using (var target = File.Create(packageFile))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256)
            && !string.Equals(await CalculateSha256Async(packageFile, cancellationToken), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包校验失败，已取消安装。");
        }

        var updaterDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CeHao", "Updater");
        Directory.CreateDirectory(updaterDirectory);
        var updaterTarget = Path.Combine(updaterDirectory, "CeHao.Updater.exe");
        File.Copy(updaterSource, updaterTarget, true);

        var startInfo = new ProcessStartInfo(updaterTarget,
            $"--pid {Environment.ProcessId} --package \"{packageFile}\" --install \"{installDirectory}\" --restart \"{Path.Combine(installDirectory, "CeHao.exe")}\"")
        {
            UseShellExecute = true,
            WorkingDirectory = installDirectory
        };
        var updaterProcess = Process.Start(startInfo);
        if (updaterProcess is null)
        {
            throw new InvalidOperationException("无法启动更新程序。");
        }
    }

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static bool IsNewer(string remoteVersion)
    {
        var current = typeof(App).Assembly.GetName().Version ?? new Version(0, 0);
        return TryParseVersion(remoteVersion, out var remote) && remote > current;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0);
        var match = Regex.Match(value ?? string.Empty, @"\d+(?:\.\d+){1,3}");
        if (!match.Success || !Version.TryParse(match.Value, out var parsed) || parsed is null)
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";
}

internal sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string PackageUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

internal sealed record UpdateCheckResult(UpdateManifest? Manifest)
{
    public static UpdateCheckResult None { get; } = new((UpdateManifest?)null);
    public bool IsAvailable => Manifest is not null;
}
