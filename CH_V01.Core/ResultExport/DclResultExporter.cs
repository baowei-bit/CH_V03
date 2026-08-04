using System.Text;

namespace CH_V01.Core.ResultExport;

public sealed class DclResultExporter : IResultExporter, IResultExportPlugin
{
    public string Id => "Default.Dcl";

    public string Name => "默认 DCL";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return Array.Empty<ExportPluginConfigField>();
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(context.Snapshot.Barcode))
            {
                return ResultExportResult.Fail(Name, "条码为空，跳过 DCL 生成");
            }

            var directory = context.Configuration.Output.DclDirectory;
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, $"{context.Snapshot.Barcode}.dcl"));
            await File.WriteAllTextAsync(filePath, BuildDcl(context), Encoding.Default, cancellationToken);
            return ResultExportResult.Success(Name, "DCL 生成成功", filePath);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"DCL 生成失败：{ex.Message}");
        }
    }

    private static string BuildDcl(ResultExportContext context)
    {
        var config = context.Configuration;
        var builder = new StringBuilder();
        builder.Append($"{DateTime.Now:yyyy/MM/dd HH:mm:ss},");
        builder.Append(",,,,");
        builder.Append($"{context.Snapshot.Barcode},");
        builder.Append($"Product part number:{config.Mes.UserNo},");
        builder.Append($"prodNo:{config.Mes.ProdNo},");
        builder.Append($"Machine:{config.Mes.PackNo},");
        builder.Append($"Fixture:{config.Mes.Tooling},");
        builder.Append($"OPID:{config.User.LoginJobNo},");
        builder.Append($"stationNo:{config.Identity.StationNo},");

        foreach (var item in context.Snapshot.Items)
        {
            builder.Append($" {item.ItemName}:{item.TestValue}{item.Unit}({item.Result});");
        }

        builder.Append($" Test Result: {(context.Snapshot.IsPass == true ? "OK" : "NG")}.");
        return builder.ToString();
    }
}

