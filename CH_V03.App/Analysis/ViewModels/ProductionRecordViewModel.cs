namespace CH_V03.App.ViewModels;

public sealed class ProductionRecordViewModel : ViewModelBase
{
    public DateTime EndTime { get; init; }

    public string Barcode { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public string FirstFailItem { get; init; } = string.Empty;

    public double TestSeconds { get; init; }

    public int ItemCount { get; init; }

    /// <summary>本次成品测试的逐项结果，用于运行数据中的趋势分析。</summary>
    public IReadOnlyList<CH_V03.Core.TestFlow.TestItemResult> TestItems { get; init; } = Array.Empty<CH_V03.Core.TestFlow.TestItemResult>();
}
