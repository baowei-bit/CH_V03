using System.Text;

namespace CH_V03.Core.ResultExport;

public sealed class SinglePieceCsvResultExporter : IResultExporter
{
    public string Name => "单片 CSV";

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!context.Configuration.Output.SinglePieceCsvEnabled)
            {
                return ResultExportResult.Success("单片 CSV", "未启用单片 CSV", string.Empty);
            }

            if (string.IsNullOrWhiteSpace(context.Snapshot.Barcode))
            {
                return ResultExportResult.Fail("单片 CSV", "条码为空，跳过单片 CSV 生成");
            }

            var directory = context.Configuration.Output.SinglePieceCsvDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                return ResultExportResult.Fail("单片 CSV", "单片 CSV 保存路径为空");
            }

            Directory.CreateDirectory(directory);
            var fileName = BuildFileName(context);
            var filePath = GetAvailablePath(Path.Combine(directory, fileName));
            var lines = new[]
            {
                BuildHeader(context),
                BuildLowerLimitLine(context),
                BuildUpperLimitLine(context),
                BuildUnitLine(context),
                BuildResultLine(context)
            };

            await File.WriteAllLinesAsync(filePath, lines, Encoding.UTF8, cancellationToken);
            return ResultExportResult.Success("单片 CSV", "单片 CSV 生成成功", filePath);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail("单片 CSV", $"单片 CSV 生成失败：{ex.Message}");
        }
    }

    private static string BuildFileName(ResultExportContext context)
    {
        var template = context.Configuration.Output.SinglePieceCsvFileName;
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "{Result}_{SN}_{Time}.csv";
        }

        var name = template
            .Replace("{Result}", context.ResultTextNgOk, StringComparison.OrdinalIgnoreCase)
            .Replace("{SN}", context.Snapshot.Barcode, StringComparison.OrdinalIgnoreCase)
            .Replace("{Barcode}", context.Snapshot.Barcode, StringComparison.OrdinalIgnoreCase)
            .Replace("{Time}", context.EndTime.ToString("yyyyMMddHHmmss"), StringComparison.OrdinalIgnoreCase)
            .Replace("{Name}", context.Configuration.Output.CsvName, StringComparison.OrdinalIgnoreCase);

        if (!Path.HasExtension(name))
        {
            name += ".csv";
        }

        return ResultExportFileName.ApplyTestPieceSuffix(context, SanitizeFileName(name));
    }

    private static string BuildHeader(ResultExportContext context)
    {
        var fixedHeaders = new[]
        {
            "SerialNumber", "Test Pass/Fail Status", "config", "StartTime", "EndTime", "TestTime",
            "Failing items", "errValue", "testModel", "plateID", "testType", "version", "guid", "user"
        };

        return Join(fixedHeaders.Concat(context.Snapshot.Items.Select(item => item.ItemName)));
    }

    private static string BuildLowerLimitLine(ResultExportContext context)
    {
        return "Lower Limit----->," + Blank(13) + Join(context.Snapshot.Items.Select(item => item.LowerLimit));
    }

    private static string BuildUpperLimitLine(ResultExportContext context)
    {
        return "Upper Limit----->," + Blank(13) + Join(context.Snapshot.Items.Select(item => item.UpperLimit));
    }

    private static string BuildUnitLine(ResultExportContext context)
    {
        return "Measurement Unit----->,,,,,Sec," + Blank(8) + Join(context.Snapshot.Items.Select(item => item.Unit));
    }

    private static string BuildResultLine(ResultExportContext context)
    {
        var values = new List<string>
        {
            context.Snapshot.Barcode,
            context.ResultText,
            context.Configuration.Output.CsvName,
            context.StartTime.ToString("yyyy/M/d H:mm:ss"),
            context.EndTime.ToString("yyyy/M/d H:mm:ss"),
            context.Snapshot.Elapsed.TotalSeconds.ToString("F7"),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.TestValue)),
            context.GetOption("Module", "A"),
            context.Configuration.Mes.PackNo,
            context.Configuration.Identity.TestType,
            context.Configuration.Identity.TestProgram,
            context.Guid,
            context.Configuration.Mes.UserNo
        };

        values.AddRange(context.Snapshot.Items.Select(item => item.TestValue));
        return Join(values);
    }

    private static string Join(IEnumerable<string> values)
    {
        return string.Join(',', values.Select(Escape));
    }

    private static string Blank(int count)
    {
        return string.Concat(Enumerable.Repeat(",", count));
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName.Trim();
    }

    private static string GetAvailablePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}_{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{name}_{Guid.NewGuid():N}{extension}");
    }
}
