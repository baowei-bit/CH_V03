using System.Diagnostics;
using System.IO;
using System.IO.Compression;

var arguments = ReadArguments(args);
if (!arguments.TryGetValue("pid", out var pidText)
    || !int.TryParse(pidText, out var pid)
    || !arguments.TryGetValue("package", out var package)
    || !arguments.TryGetValue("install", out var install)
    || !arguments.TryGetValue("restart", out var restart))
{
    return;
}

try
{
    WaitForApplicationExit(pid);
    var staging = Path.Combine(Path.GetTempPath(), "CeHao", "staging", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(staging);
    ZipFile.ExtractToDirectory(package, staging);
    if (!File.Exists(Path.Combine(staging, "CeHao.exe")))
    {
        throw new InvalidDataException("更新包中未找到 CeHao.exe。");
    }

    CopyPackage(staging, install);
    Process.Start(new ProcessStartInfo(restart) { UseShellExecute = true, WorkingDirectory = install });
}
catch (Exception exception)
{
    System.Windows.MessageBox.Show($"CeHao 更新失败，原有配置未被修改。\n\n{exception.Message}", "CeHao 软件更新", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
}

static Dictionary<string, string> ReadArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index + 1 < values.Length; index += 2)
    {
        if (values[index].StartsWith("--", StringComparison.Ordinal)) result[values[index][2..]] = values[index + 1];
    }
    return result;
}

static void WaitForApplicationExit(int pid)
{
    try { Process.GetProcessById(pid).WaitForExit(30000); } catch (ArgumentException) { }
}

static void CopyPackage(string source, string destination)
{
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(source, file);
        // 保留现场设备/MES 参数，发布包不会覆盖本地 appsettings.json。
        if (string.Equals(relative, Path.Combine("config", "appsettings.json"), StringComparison.OrdinalIgnoreCase)) continue;
        var target = Path.Combine(destination, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, true);
    }
}
