using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.Core.TestFlow;

namespace CH_V03.App.ViewModels;

/// <summary>按测试项和点位浏览本次运行的近期测试趋势。</summary>
public sealed class TestTrendAnalysisViewModel : DialogViewModelBase
{
    private const int MaxSelectedItems = 4;
    private readonly IReadOnlyList<ProductionRecordViewModel> _records;
    private TestTrendSeriesViewModel? _selectedSeries;
    private int _selectedRecordCount = 20;
    private string _selectionLimitMessage = string.Empty;

    public TestTrendAnalysisViewModel(IEnumerable<ProductionRecordViewModel> records)
    {
        _records = records.Where(record => record.TestItems.Count > 0).OrderByDescending(record => record.EndTime).ToList();
        RecordCountChoices = new[] { 10, 20, 50, 100, 200, 500 };
        ItemChoices = new ObservableCollection<TestTrendItemChoice>(BuildItemChoices(_records));
        Series = new ObservableCollection<TestTrendSeriesViewModel>();
        foreach (var choice in ItemChoices) choice.SelectionChanged = OnChoiceSelectionChanged;
        if (ItemChoices.FirstOrDefault() is { } first) first.IsSelected = true;
        RefreshCommand = new RelayCommand(Refresh);
        SelectSeriesCommand = new RelayCommand(parameter => SelectSeries(parameter as TestTrendSeriesViewModel));
        CloseCommand = new RelayCommand(() => Close(true));
        Refresh();
    }

    public IReadOnlyList<int> RecordCountChoices { get; }
    public ObservableCollection<TestTrendItemChoice> ItemChoices { get; }
    public ObservableCollection<TestTrendSeriesViewModel> Series { get; }

    public int SelectedRecordCount
    {
        get => _selectedRecordCount;
        set { if (SetField(ref _selectedRecordCount, value)) Refresh(); }
    }

    public TestTrendSeriesViewModel? SelectedSeries
    {
        get => _selectedSeries;
        private set
        {
            if (SetField(ref _selectedSeries, value))
            {
                OnPropertyChanged(nameof(DetailSamples));
                OnPropertyChanged(nameof(DetailTitle));
                OnPropertyChanged(nameof(EmptyMessage));
            }
        }
    }

    public IReadOnlyList<TestTrendSampleViewModel> DetailSamples => SelectedSeries?.Samples ?? Array.Empty<TestTrendSampleViewModel>();
    public string DetailTitle => SelectedSeries is null ? "近期测试明细" : $"近期测试明细：{SelectedSeries.Choice.DisplayName}";
    public string RecordInfo => _records.Count == 0 ? "本次运行尚无可分析的测试记录。" : $"本次运行可分析 {_records.Count} 件记录（最多保留 500 件）；可同时比较 {MaxSelectedItems} 个测试项。";
    public string SelectedItemsSummary => Series.Count == 0 ? "请选择测试项/点位" : string.Join("；", Series.Select(series => series.Choice.ShortDisplayName));
    public string SelectionLimitMessage { get => _selectionLimitMessage; private set => SetField(ref _selectionLimitMessage, value); }
    public string EmptyMessage => SelectedSeries is null ? "请先完成至少一件测试，再选择测试项。" : "选定范围内没有该测试项的测试数据。";
    public ICommand RefreshCommand { get; }
    public ICommand SelectSeriesCommand { get; }
    public ICommand CloseCommand { get; }

    private void OnChoiceSelectionChanged(TestTrendItemChoice choice)
    {
        if (choice.IsSelected && ItemChoices.Count(item => item.IsSelected) > MaxSelectedItems)
        {
            choice.IsSelected = false;
            SelectionLimitMessage = $"最多同时选择 {MaxSelectedItems} 个测试项。";
            return;
        }

        SelectionLimitMessage = string.Empty;
        Refresh();
    }

    private void Refresh()
    {
        var selectedChoices = ItemChoices.Where(choice => choice.IsSelected).ToList();
        var selectedRecords = _records.Take(Math.Max(1, SelectedRecordCount)).Reverse().ToList();
        foreach (var choice in ItemChoices)
        {
            choice.FailureCount = CreateSamples(choice, selectedRecords).Count(sample => sample.IsAbnormal);
        }

        var currentChoice = SelectedSeries?.Choice;
        Series.Clear();
        foreach (var choice in selectedChoices)
        {
            Series.Add(new TestTrendSeriesViewModel(choice, CreateSamples(choice, selectedRecords)));
        }

        SelectedSeries = Series.FirstOrDefault(series => ReferenceEquals(series.Choice, currentChoice)) ?? Series.FirstOrDefault();
        OnPropertyChanged(nameof(RecordInfo));
        OnPropertyChanged(nameof(SelectedItemsSummary));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void SelectSeries(TestTrendSeriesViewModel? series)
    {
        if (series is not null) SelectedSeries = series;
    }

    private static IReadOnlyList<TestTrendSampleViewModel> CreateSamples(TestTrendItemChoice choice, IEnumerable<ProductionRecordViewModel> records)
    {
        var samples = new List<TestTrendSampleViewModel>();
        var sequence = 1;
        foreach (var record in records)
        {
            var item = record.TestItems.FirstOrDefault(candidate => candidate.Index == choice.Index && string.Equals(candidate.ItemName, choice.ItemName, StringComparison.Ordinal));
            if (item is not null) samples.Add(new TestTrendSampleViewModel(sequence++, record, item));
        }

        return samples;
    }

    private static IEnumerable<TestTrendItemChoice> BuildItemChoices(IEnumerable<ProductionRecordViewModel> records) => records
        .SelectMany(record => record.TestItems)
        .GroupBy(item => new { item.Index, item.ItemName, item.PointA, item.PointB, item.Unit })
        .OrderBy(group => group.Key.Index)
        .ThenBy(group => group.Key.ItemName)
        .Select(group => new TestTrendItemChoice(group.Key.Index, group.Key.ItemName, group.Key.PointA, group.Key.PointB, group.Key.Unit));
}

public sealed class TestTrendItemChoice : ViewModelBase
{
    private bool _isSelected;
    private int _failureCount;

    public TestTrendItemChoice(int index, string itemName, string pointA, string pointB, string unit)
    {
        Index = index;
        ItemName = itemName;
        PointA = pointA;
        PointB = pointB;
        Unit = unit;
    }

    public int Index { get; }
    public string ItemName { get; }
    public string PointA { get; }
    public string PointB { get; }
    public string Unit { get; }
    public Action<TestTrendItemChoice>? SelectionChanged { get; set; }
    public string DisplayName => $"{Index}. {ItemName}  [{PointA} - {PointB}]  {Unit}";
    public string ShortDisplayName => $"{Index}. {ItemName}";
    public string FailureSummary => FailureCount == 0 ? "无异常" : $"异常 {FailureCount} 件";
    public bool HasFailures => FailureCount > 0;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value)) SelectionChanged?.Invoke(this);
        }
    }

    public int FailureCount
    {
        get => _failureCount;
        set
        {
            if (SetField(ref _failureCount, value))
            {
                OnPropertyChanged(nameof(FailureSummary));
                OnPropertyChanged(nameof(HasFailures));
            }
        }
    }
}

public sealed class TestTrendSeriesViewModel
{
    public TestTrendSeriesViewModel(TestTrendItemChoice choice, IReadOnlyList<TestTrendSampleViewModel> samples)
    {
        Choice = choice;
        Samples = samples;
    }

    public TestTrendItemChoice Choice { get; }
    public IReadOnlyList<TestTrendSampleViewModel> Samples { get; }
    public int AbnormalCount => Samples.Count(sample => sample.IsAbnormal);
    public string AbnormalSummary => $"异常 {AbnormalCount} / {Samples.Count}";
    public string Statistics => BuildStatistics();

    private string BuildStatistics()
    {
        var values = Samples.Where(sample => sample.NumericValue.HasValue).Select(sample => sample.NumericValue!.Value).ToList();
        return values.Count == 0
            ? "最小 --   最大 --   平均 --"
            : $"最小 {values.Min():0.###}   最大 {values.Max():0.###}   平均 {values.Average():0.###}";
    }
}

public sealed class TestTrendSampleViewModel
{
    public TestTrendSampleViewModel(int sequence, ProductionRecordViewModel record, TestItemResult item)
    {
        Sequence = sequence;
        EndTime = record.EndTime;
        Barcode = record.Barcode;
        PointA = item.PointA;
        PointB = item.PointB;
        LowerLimit = item.LowerLimit;
        UpperLimit = item.UpperLimit;
        Unit = item.Unit;
        TestValue = item.TestValue;
        Result = item.Result;
        NumericValue = TryParseMeasurement(item.TestValue, out var value) ? value : null;
        NumericLowerLimit = TryParseMeasurement(item.LowerLimit, out var lower) ? lower : null;
        NumericUpperLimit = TryParseMeasurement(item.UpperLimit, out var upper) ? upper : null;
        IsOutOfRange = NumericValue.HasValue && ((NumericLowerLimit.HasValue && NumericValue.Value < NumericLowerLimit.Value) || (NumericUpperLimit.HasValue && NumericValue.Value > NumericUpperLimit.Value));
        IsFailedResult = IsFailed(item.Result);
    }

    public int Sequence { get; }
    public DateTime EndTime { get; }
    public string Barcode { get; }
    public string PointA { get; }
    public string PointB { get; }
    public string LowerLimit { get; }
    public string UpperLimit { get; }
    public string Unit { get; }
    public string TestValue { get; }
    public string Result { get; }
    public double? NumericValue { get; }
    public double? NumericLowerLimit { get; }
    public double? NumericUpperLimit { get; }
    public bool IsOutOfRange { get; }
    public bool IsFailedResult { get; }
    public bool IsAbnormal => IsOutOfRange || IsFailedResult;

    private static bool IsFailed(string? result)
    {
        var normalized = result?.Trim() ?? string.Empty;
        return normalized.Equals("NG", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("FAIL", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("FAIL", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseMeasurement(string value, out double result)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            result = hex;
            return true;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result)
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
