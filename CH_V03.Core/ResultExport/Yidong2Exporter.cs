using CH_V03.Core.TestFlow;
using Microsoft.VisualBasic.FileIO;

namespace CH_V03.Core.ResultExport;

public sealed class Yidong2Exporter : IResultExportPlugin
{
    private const string ProductDirectory = @"awinic\AW93305DNR_ProductionTest_GUI_For_Huaqin_V";
    public static bool UsesVersion3 => Directory.Exists(InstallationDirectory(true));
    public static string InstallationDirectory(bool version3)
    {
        var relative = ProductDirectory + (version3 ? "1.0.0.3" : "1.0.0.1");
        var sitePath = Path.Combine(@"D:\", relative);
        var defaultPath = Path.Combine(@"C:\", relative);
        return Directory.Exists(sitePath) || (!version3 && !Directory.Exists(defaultPath)) ? sitePath : defaultPath;
    }
    public static string DirectoryDefault => Path.Combine(InstallationDirectory(UsesVersion3), "savedata");
    public static readonly IReadOnlyList<string> Columns = Array.AsReadOnly<string>(["short", "GAP1", "GAP2", "GAP3", "GAP4", "GAP5", "GAP AVG"]);
    public static readonly IReadOnlyList<string> Version3Columns = Array.AsReadOnly<string>([.. Columns,
        "offset_CH0(pF)", "offset_CH1(pF)", "offset_CH2(pF)", "offset_CH3(pF)", "offset_CH4(pF)",
        "abs_gap1", "abs_gap2", "abs_gap3", "abs_gap4", "abs_gap5", "abs_gap avg"]);
    public string Id => "Yidong2";
    public string Name => "Yidong2 联合测试";
    private sealed class JointResultExporter : YiDongEapResultExporter
    {
        public override string Id => "Yidong2";
        public override string Name => "Yidong2 联合测试";
        protected override string HorizontalName(ResultExportContext context) =>
            context.Snapshot.Items.LastOrDefault()?.ItemName == "abs_gap avg"
                ? base.HorizontalName(context) + "_V1003" : base.HorizontalName(context);
    }
    private readonly YiDongEapResultExporter _exporter = new JointResultExporter();
    public IReadOnlyList<ExportPluginConfigField> GetConfigFields() =>
        [.. _exporter.GetConfigFields(),
         new() { Key = "CompanionDirectory", DisplayName = "陪测 CSV 目录", DefaultValue = DirectoryDefault }];

    public Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        var items = context.Snapshot.Items.Select(i => i.ItemName);
        if (!items.TakeLast(Columns.Count).SequenceEqual(Columns) && !items.TakeLast(Version3Columns.Count).SequenceEqual(Version3Columns))
            return Task.FromResult(ResultExportResult.Fail(Name, "缺少联合测试结果"));
        return _exporter.ExportAsync(context, cancellationToken);
    }

    public Task<ResultExportResult> ExportPieceAsync(ResultExportContext context) =>
        new CsvResultExporter().WriteAsync(context, $"Yidong2_MES_{context.Guid}", CancellationToken.None);

    public static TestFlowSnapshot Merge(TestFlowSnapshot local, Stream stream, out bool anonymous)
    {
        using var csv = new TextFieldParser(stream);
        csv.SetDelimiters(",");
        var header = csv.ReadFields() ?? throw new InvalidDataException("陪测 CSV 缺少表头");
        var row = csv.ReadFields() ?? throw new InvalidDataException("陪测 CSV 缺少数据行");
        if (!csv.EndOfData || row.Length != header.Length)
            throw new InvalidDataException("需要一件产品一行、列数完整的陪测 CSV");
        string Read(string name)
        {
            int index = Array.FindIndex(header, h => h.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? row[index].Trim() : throw new InvalidDataException($"缺少陪测列 {name}");
        }
        var barcode = Read("BarCode");
        anonymous = barcode.Length == 0;
        if (!anonymous && barcode != local.Barcode) throw new InvalidDataException("陪测条码与当前 SN 不匹配");
        bool pass = Read("TestResult").ToUpperInvariant() switch
        {
            "PASS" or "OK" => true, "FAIL" or "NG" => false,
            _ => throw new InvalidDataException("未知陪测结果")
        };
        var items = local.Items.ToList();
        int index = items.Select(i => i.Index).DefaultIfEmpty().Max();
        var columns = header.Any(h => h.Trim().StartsWith("abs_gap", StringComparison.OrdinalIgnoreCase)) ? Version3Columns : Columns;
        foreach (string column in columns)
        {
            string value = Read(column switch { "short" => "short test", "GAP AVG" => "gap avg (upper-lower)", _ => column });
            if (value.Length == 0) throw new InvalidDataException($"陪测列 {column} 为空");
            items.Add(new() { Index = ++index, ItemName = column, TestValue = value, Result = pass ? "PASS" : "已测" });
        }
        return new() { Barcode = local.Barcode, State = local.State, Items = items, StartTime = local.StartTime,
            EndTime = DateTime.Now, IsPass = local.IsPass == true && pass, Message = "联合测试完成",
            FirstFailItem = local.IsPass == false ? local.FirstFailItem : pass ? "" : "陪测 TestResult",
            FirstFailValue = local.IsPass == false ? local.FirstFailValue : pass ? "" : "FAIL" };
    }
}
