namespace CH_V03.App.ViewModels;

public sealed class TestItemViewModel : ViewModelBase
{
    private string _upperLimit = string.Empty;
    private string _lowerLimit = string.Empty;

    public string Index { get; init; } = string.Empty;

    public string ItemName { get; init; } = string.Empty;

    public string UpperLimit
    {
        get => _upperLimit;
        set
        {
            if (SetField(ref _upperLimit, value))
            {
                LimitsChanged?.Invoke(this);
            }
        }
    }

    public string LowerLimit
    {
        get => _lowerLimit;
        set
        {
            if (SetField(ref _lowerLimit, value))
            {
                LimitsChanged?.Invoke(this);
            }
        }
    }

    public string TestValue { get; init; } = string.Empty;

    public string PointA { get; init; } = string.Empty;

    public string PointB { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public Action<TestItemViewModel>? LimitsChanged { get; init; }
}
