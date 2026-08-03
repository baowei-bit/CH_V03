namespace CH_V01.App.ViewModels;

public sealed class PluginSelectionItemViewModel
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description => string.IsNullOrWhiteSpace(Id) ? DisplayName : $"{DisplayName} ({Id})";
}
