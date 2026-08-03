using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CH_V01.Core.Configuration;
using CH_V01.Core.ResultExport;
using CH_V01.Core.TestFlow;

namespace CH_V01.App.Services;

/// <summary>
/// 按二维码中的客户代码归档测试结果，例如 PPRCAR26... 归档到 CAR 目录。
/// </summary>
public sealed class CustomerPathArchiveService
{
    private static readonly Regex CustomerCodePattern = new(
        @"^.{3}(?<customer>[A-Za-z]{2,3})(?=\d)",
        RegexOptions.CultureInvariant);

    public async Task<ResultExportResult> ArchiveAsync(
        AppConfiguration configuration,
        TestFlowSnapshot snapshot,
        IEnumerable<ResultExportResult> exportResults,
        CancellationToken cancellationToken = default)
    {
        if (!configuration.Output.CustomerPathSaveEnabled)
        {
            return ResultExportResult.Success("客户路径保存", "未启用客户路径保存", string.Empty);
        }

        if (!TryGetCustomerCode(snapshot.Barcode, out var customerCode))
        {
            return ResultExportResult.Fail("客户路径保存", "二维码格式不匹配，未识别到第 4 位开始的 2-3 位客户代码");
        }

        var rootDirectory = configuration.Output.CustomerPathRootDirectory;
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return ResultExportResult.Fail("客户路径保存", "客户路径保存根目录为空");
        }

        try
        {
            var resultDirectory = Path.Combine(rootDirectory, customerCode, snapshot.IsPass == true ? "PASS" : "FAIL");
            var csvDirectory = Path.Combine(resultDirectory, "CSV");
            var dclDirectory = Path.Combine(resultDirectory, "DCL");
            var logDirectory = Path.Combine(resultDirectory, "LOG");
            Directory.CreateDirectory(csvDirectory);
            Directory.CreateDirectory(dclDirectory);
            Directory.CreateDirectory(logDirectory);

            var copiedFiles = CopyExportFiles(exportResults, csvDirectory, dclDirectory);
            var logPath = Path.Combine(logDirectory, $"{SafeFileName(snapshot.Barcode)}_{(snapshot.EndTime ?? DateTime.Now):yyyyMMddHHmmssfff}.log");
            await File.WriteAllTextAsync(logPath, BuildTestLog(snapshot), Encoding.UTF8, cancellationToken);

            var fileSummary = string.Join("; ", copiedFiles.Append(logPath));
            return ResultExportResult.Success("客户路径保存", $"已归档到 {customerCode}\\{(snapshot.IsPass == true ? "PASS" : "FAIL")}", fileSummary);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail("客户路径保存", $"归档失败：{ex.Message}");
        }
    }

    private static IReadOnlyList<string> CopyExportFiles(IEnumerable<ResultExportResult> exportResults, string csvDirectory, string dclDirectory)
    {
        var copiedFiles = new List<string>();
        foreach (var filePath in exportResults
                     .Where(result => result.IsSuccess && !string.IsNullOrWhiteSpace(result.FilePath))
                     .SelectMany(result => result.FilePath.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var destinationDirectory = Path.GetExtension(filePath).ToLowerInvariant() switch
            {
                ".csv" => csvDirectory,
                ".dcl" or ".txt" => dclDirectory,
                _ => string.Empty
            };

            if (string.IsNullOrEmpty(destinationDirectory))
            {
                continue;
            }

            var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationPath, overwrite: true);
            copiedFiles.Add(destinationPath);
        }

        return copiedFiles;
    }

    private static bool TryGetCustomerCode(string barcode, out string customerCode)
    {
        var match = CustomerCodePattern.Match(barcode?.Trim() ?? string.Empty);
        customerCode = match.Success ? match.Groups["customer"].Value.ToUpperInvariant() : string.Empty;
        return match.Success;
    }

    private static string BuildTestLog(TestFlowSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SN={snapshot.Barcode}");
        builder.AppendLine($"Result={(snapshot.IsPass == true ? "PASS" : "FAIL")}");
        builder.AppendLine($"StartTime={(snapshot.StartTime ?? DateTime.Now):yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"EndTime={(snapshot.EndTime ?? DateTime.Now):yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Message={snapshot.Message}");
        builder.AppendLine($"FirstFailItem={snapshot.FirstFailItem}");
        builder.AppendLine($"FirstFailValue={snapshot.FirstFailValue}");
        builder.AppendLine();
        builder.AppendLine("Index,Item,LowerLimit,UpperLimit,Unit,Value,Result");
        foreach (var item in snapshot.Items)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                item.Index.ToString(), item.ItemName, item.LowerLimit, item.UpperLimit,
                item.Unit, item.TestValue, item.Result
            }.Select(EscapeLogValue)));
        }

        return builder.ToString();
    }

    private static string EscapeLogValue(string value) =>
        $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string SafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat((value ?? string.Empty).Select(character => invalidCharacters.Contains(character) ? '_' : character));
    }
}
