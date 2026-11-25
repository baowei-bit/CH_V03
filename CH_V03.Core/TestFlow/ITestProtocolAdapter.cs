namespace CH_V03.Core.TestFlow;

public interface ITestProtocolAdapter
{
    string Id { get; }

    string Name { get; }

    string LowerMachineOkCommand { get; }

    string LowerMachineNgCommand { get; }

    string TestOverCommand => "TestOver\r\n";

    bool IsIgnoredPayload(string payload);

    bool IsStartSignal(string payload);

    bool IsItemPayload(string payload);

    bool IsResultPayload(string payload);

    IReadOnlyList<TestItemResult> ParseItems(string payload);

    IReadOnlyList<ParsedTestResult> ParseResults(string payload);
}

