using System.Collections.ObjectModel;
using System.Windows.Input;
using BW_V01.App.Commands;

namespace BW_V01.App.ViewModels;

public sealed class DataAnalysisViewModel : DialogViewModelBase
{
    public DataAnalysisViewModel(IEnumerable<ProductionRecordViewModel> records, int passCount, int failCount, int currentConsecutiveNg)
    {
        Records = new ObservableCollection<ProductionRecordViewModel>(records.OrderByDescending(item => item.EndTime));
        PassCount = passCount;
        FailCount = failCount;
        CurrentConsecutiveNg = currentConsecutiveNg;
        CloseCommand = new RelayCommand(() => Close(true));
    }

    public ObservableCollection<ProductionRecordViewModel> Records { get; }

    public int PassCount { get; }

    public int FailCount { get; }

    public int TotalCount => PassCount + FailCount;

    public int CurrentConsecutiveNg { get; }

    public double YieldRate => TotalCount == 0 ? 0 : Math.Round(PassCount * 100.0 / TotalCount, 2);

    public string YieldRateText => $"{YieldRate:F2}%";

    public int RecordPassCount => Records.Count(item => string.Equals(item.Result, "PASS", StringComparison.OrdinalIgnoreCase));

    public int RecordFailCount => Records.Count(item => string.Equals(item.Result, "FAIL", StringComparison.OrdinalIgnoreCase));

    public string WorstFailItem
    {
        get
        {
            var item = Records
                .Where(record => !string.IsNullOrWhiteSpace(record.FirstFailItem))
                .GroupBy(record => record.FirstFailItem)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();

            return item is null ? "无" : $"{item.Key} ({item.Count()})";
        }
    }

    public ICommand CloseCommand { get; }
}
