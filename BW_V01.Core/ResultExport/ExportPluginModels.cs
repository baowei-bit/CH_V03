namespace BW_V01.Core.ResultExport;

public sealed class ExportPluginConfigField
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DefaultValue { get; init; } = string.Empty;

    public bool IsRequired { get; init; }

    public bool IsSecret { get; init; }
}
