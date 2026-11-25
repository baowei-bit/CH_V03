using CH_V03.Core.TestFlow;

namespace CH_V03.Core.ResultExport;

/// <summary>Items in the marked group on the test screen. Keep the measured value in the snapshot for judgment and MES.</summary>
public static class SpecialTestValue
{
    public const string OptionKey = "HideSpecialItemTestValues";

    public static bool IsSpecial(string? itemName)
    {
        var name = (itemName ?? string.Empty).Trim();
        return name.StartsWith("s03 ref", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("swipe phase0 offset", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("swipe phase1 offset", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("swipe phase0 diff", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("swipe phase1 diff", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCsvHidden(ResultExportContext context) =>
        bool.TryParse(context.GetOption(OptionKey, "false"), out var hidden) && hidden;

    public static string CsvValue(ResultExportContext context, TestItemResult item) =>
        IsCsvHidden(context) && IsSpecial(item.ItemName) ? string.Empty : item.TestValue;
}
