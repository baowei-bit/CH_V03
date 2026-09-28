namespace CH_V03.App.ViewModels;

public sealed class PluginSelectionItemViewModel
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description => string.IsNullOrWhiteSpace(Id) ? DisplayName : $"{DisplayName} ({Id})";
}
