using BW_V01.Core.Configuration;

namespace BW_V01.Core.TestFlow;

public enum TestFlowState
{
    Idle,
    WaitingScan,
    Ready,
    Testing,
    Completed,
    Failed
}

public sealed class TestFlowOptions
{
    public string BarcodeLength { get; set; } = "12";

    public string CodeSuffix { get; set; } = string.Empty;

    public bool AutoScan { get; set; } = true;

    public static TestFlowOptions FromConfiguration(AppConfiguration configuration)
    {
        return new TestFlowOptions
        {
            BarcodeLength = configuration.Identity.BarcodeLength,
            CodeSuffix = configuration.Identity.CodeSuffix,
            AutoScan = configuration.User.AutoScan
        };
    }
}

public sealed class TestFlowSnapshot
{
    public TestFlowState State { get; init; }

    public string Barcode { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string FirstFailItem { get; init; } = string.Empty;

    public string FirstFailValue { get; init; } = string.Empty;

    public bool? IsPass { get; init; }

    public DateTime? StartTime { get; init; }

    public DateTime? EndTime { get; init; }

    public TimeSpan Elapsed => StartTime.HasValue && EndTime.HasValue
        ? EndTime.Value - StartTime.Value
        : StartTime.HasValue
            ? DateTime.Now - StartTime.Value
            : TimeSpan.Zero;

    public IReadOnlyList<TestItemResult> Items { get; init; } = Array.Empty<TestItemResult>();
}

public sealed class TestItemResult
{
    public int Index { get; init; }

    public string ItemName { get; init; } = string.Empty;

    public string PointA { get; set; } = string.Empty;

    public string PointB { get; set; } = string.Empty;

    public string UpperLimit { get; init; } = string.Empty;

    public string LowerLimit { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public string TestValue { get; set; } = string.Empty;

    public string Result { get; set; } = "WAIT";

    public bool IsPass => string.Equals(Result, "OK", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Result, "PASS", StringComparison.OrdinalIgnoreCase);
}

public sealed record ParsedTestResult(int Index, string ItemName, string Value, string Result, string PointA, string PointB);
