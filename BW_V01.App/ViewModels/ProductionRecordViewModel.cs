namespace BW_V01.App.ViewModels;

public sealed class ProductionRecordViewModel : ViewModelBase
{
    public DateTime EndTime { get; init; }

    public string Barcode { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public string FirstFailItem { get; init; } = string.Empty;

    public double TestSeconds { get; init; }

    public int ItemCount { get; init; }
}
