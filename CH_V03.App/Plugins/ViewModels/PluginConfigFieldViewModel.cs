namespace CH_V03.App.ViewModels;

public sealed class PluginConfigFieldViewModel : ViewModelBase
{
    private readonly IDictionary<string, string> _values;
    private string _value;

    public PluginConfigFieldViewModel(
        string key,
        string displayName,
        string defaultValue,
        bool isRequired,
        bool isSecret,
        bool isCheckbox,
        string helpText,
        IDictionary<string, string> values)
    {
        Key = key;
        DisplayName = displayName;
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        IsSecret = isSecret;
        IsCheckbox = isCheckbox;
        HelpText = string.IsNullOrWhiteSpace(helpText)
            ? (string.IsNullOrWhiteSpace(DefaultValue) ? Key : $"{Key}，默认：{DefaultValue}")
            : helpText;
        _values = values;

        if (!_values.TryGetValue(Key, out _value!) && !string.IsNullOrWhiteSpace(DefaultValue))
        {
            _value = DefaultValue;
            _values[Key] = DefaultValue;
        }

        _value ??= string.Empty;
        BrowseDirectoryCommand = new Commands.RelayCommand(_ => BrowseDirectory());
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string DefaultValue { get; }

    public bool IsRequired { get; }

    public bool IsSecret { get; }

    public bool IsCheckbox { get; }

    public IReadOnlyList<string> Choices { get; } = Array.Empty<string>();

    public bool IsHongXinMesMode => string.Equals(Key, "MesUploadMode", StringComparison.OrdinalIgnoreCase);

    public bool IsCapacityUploadMode
    {
        get => string.Equals(Value, "Capacity", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                Value = "Capacity";
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSinglePieceStationMode));
            }
        }
    }

    public bool IsSinglePieceStationMode
    {
        get => string.Equals(Value, "Station", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                Value = "Station";
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCapacityUploadMode));
            }
        }
    }

    public bool IsDirectory => Key.EndsWith("Directory", StringComparison.OrdinalIgnoreCase);

    public System.Windows.Input.ICommand BrowseDirectoryCommand { get; }

    public string Label => IsRequired ? $"{DisplayName} *" : DisplayName;

    public string HelpText { get; }

    public string Value
    {
        get => _value;
        set
        {
            value ??= string.Empty;
            if (!SetField(ref _value, value))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                _values.Remove(Key);
            }
            else
            {
                _values[Key] = value;
            }

            OnPropertyChanged(nameof(IsCapacityUploadMode));
            OnPropertyChanged(nameof(IsSinglePieceStationMode));
        }
    }

    public bool IsChecked
    {
        get => bool.TryParse(Value, out var value) ? value : true;
        set => Value = value.ToString().ToLowerInvariant();
    }

    private void BrowseDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择保存文件夹" };
        if (dialog.ShowDialog() == true)
        {
            Value = dialog.FolderName;
        }
    }
}
