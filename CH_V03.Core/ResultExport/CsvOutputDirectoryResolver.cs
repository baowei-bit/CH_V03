using CH_V03.Core.Configuration;

namespace CH_V03.Core.ResultExport;

internal static class CsvOutputDirectoryResolver
{
    public static string Resolve(AppConfiguration configuration, bool isPass)
    {
        var directory = configuration.Output.CsvDirectory;
        if (!configuration.Output.ClassifyCsvByResult)
        {
            return directory;
        }

        // 始终创建两个结果目录，方便现场按结果直接查看文件。
        Directory.CreateDirectory(Path.Combine(directory, "PASS"));
        Directory.CreateDirectory(Path.Combine(directory, "FAIL"));
        return Path.Combine(directory, isPass ? "PASS" : "FAIL");
    }
}
