namespace CH_V01.Core.ResultExport;

public sealed class ExportPluginConfigField
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DefaultValue { get; init; } = string.Empty;

    public bool IsRequired { get; init; }

    public bool IsSecret { get; init; }

    /// <summary>
    /// Indicates that the option should be presented as a checkbox instead of a text field.
    /// Values are persisted as the strings "true" and "false".
    /// </summary>
    public bool IsCheckbox { get; init; }

    public string HelpText { get; init; } = string.Empty;
}
