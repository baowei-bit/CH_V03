using System.Windows.Input;
using CH_V01.App.Commands;
using CH_V01.Core.Configuration;

namespace CH_V01.App.ViewModels;

/// <summary>昆山嘉联益现场使用的单页配置模型。</summary>
public sealed class KunshanJiaLianYiConfigurationViewModel : DialogViewModelBase
{
    private const string MesPluginId = "JiaLianYi.CareerRule";
    private const string ExportPluginId = "JiaLianYi.Export";
    private static readonly (string Key, string Label, string DefaultValue)[] SharedFieldDefinitions =
    {
        ("PartNumber", "产品料号 *", ""),
        ("Station", "测试站别 *", ""),
        ("ModifyUser", "测试人员 *", ""),
        ("Mechine", "设备 *", ""),
        ("Tooling", "治具 *", ""),
        ("JobNo", "工单 *", ""),
        ("OperationSeq", "制程序号 *", ""),
        ("ItemCode", "品号 *", ""),
        ("Unit", "单位 *", "pcs"),
        ("Module", "模块 *", "1"),
        ("SwVersion", "软件版本", "")
    };

    public KunshanJiaLianYiConfigurationViewModel(AppConfiguration configuration)
    {
        Configuration = configuration;
        ForceDedicatedPlugins();
        SharedFields = SharedFieldDefinitions
            .Select(field => new PluginConfigFieldViewModel(
                field.Key, field.Label.TrimEnd(' ', '*'), field.DefaultValue,
                field.Label.EndsWith('*'), false, false, string.Empty, Configuration.Mes.PluginOptions))
            .ToList();
        ExportFields = new[]
        {
            new PluginConfigFieldViewModel("CsvDirectory", "CSV目录", "", false, false, false, string.Empty, ExportOptions),
            new PluginConfigFieldViewModel("FailCsvDirectory", "FAIL CSV目录", "", false, false, false, string.Empty, ExportOptions),
            new PluginConfigFieldViewModel("DclDirectory", "DCL目录", "", false, false, false, string.Empty, ExportOptions),
            new PluginConfigFieldViewModel("CsvName", "CSV文件前缀", "", false, false, false, string.Empty, ExportOptions),
            new PluginConfigFieldViewModel("DclExtension", "DCL扩展名", ".txt", false, false, false, string.Empty, ExportOptions)
        };
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }
    public IReadOnlyList<PluginConfigFieldViewModel> SharedFields { get; }
    public IReadOnlyList<PluginConfigFieldViewModel> ExportFields { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private Dictionary<string, string> ExportOptions => Configuration.Export.PluginOptions[ExportPluginId];

    private void ForceDedicatedPlugins()
    {
        Configuration.Mes.Enabled = true;
        Configuration.Mes.SelectedPlugin = MesPluginId;
        Configuration.Export.EnabledPluginIds = new List<string> { ExportPluginId };
        if (!Configuration.Export.PluginOptions.TryGetValue(ExportPluginId, out var exportOptions))
        {
            exportOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Configuration.Export.PluginOptions[ExportPluginId] = exportOptions;
        }

        foreach (var (key, _, defaultValue) in SharedFieldDefinitions)
        {
            var value = Configuration.Mes.PluginOptions.TryGetValue(key, out var mesValue)
                ? mesValue
                : key == "JobNo" ? Configuration.Mes.ProdNo : defaultValue;
            if (!string.IsNullOrWhiteSpace(value))
            {
                Configuration.Mes.PluginOptions[key] = value;
                exportOptions[key] = value;
            }
        }
    }

    private void Save()
    {
        foreach (var field in SharedFields)
        {
            if (Configuration.Mes.PluginOptions.TryGetValue(field.Key, out var value))
            {
                ExportOptions[field.Key] = value;
            }
            else
            {
                ExportOptions.Remove(field.Key);
            }
        }

        // 主界面的工单展示和运行时默认值读取 Mes.ProdNo；专用页以 JobNo 为唯一录入项。
        Configuration.Mes.ProdNo = Configuration.Mes.PluginOptions.TryGetValue("JobNo", out var jobNo)
            ? jobNo
            : string.Empty;

        Configuration.Mes.YieldControlThreshold = Math.Clamp(
            double.IsFinite(Configuration.Mes.YieldControlThreshold) ? Configuration.Mes.YieldControlThreshold : 90,
            0,
            100);
        Close(true);
    }
}
