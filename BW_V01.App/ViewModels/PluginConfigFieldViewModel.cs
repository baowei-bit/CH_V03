namespace BW_V01.App.ViewModels;

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
        IDictionary<string, string> values)
    {
        Key = key;
        DisplayName = displayName;
        DefaultValue = defaultValue;
        IsRequired = isRequired;
        IsSecret = isSecret;
        _values = values;

        if (!_values.TryGetValue(Key, out _value!) && !string.IsNullOrWhiteSpace(DefaultValue))
        {
            _value = DefaultValue;
            _values[Key] = DefaultValue;
        }

        _value ??= string.Empty;
    }

    public string Key { get; }

    public string DisplayName { get; }

    public string DefaultValue { get; }

    public bool IsRequired { get; }

    public bool IsSecret { get; }

    public string Label => IsRequired ? $"{DisplayName} *" : DisplayName;

    public string HelpText => string.IsNullOrWhiteSpace(DefaultValue) ? Key : $"{Key}，默认：{DefaultValue}";

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
        }
    }
}
