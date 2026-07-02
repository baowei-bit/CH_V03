namespace BW_V01.Core.TestFlow;

public sealed class DefaultIctProtocolAdapter : ITestProtocolAdapter
{
    public string Id => "Default.Ict";

    public string Name => "默认 ICT 下位机协议";

    public string LowerMachineOkCommand => "DMOK\r\n";

    public string LowerMachineNgCommand => "DMNG\r\n";

    public bool IsIgnoredPayload(string payload)
    {
        return Contains(payload, "usMotorPulse=100");
    }

    public bool IsStartSignal(string payload)
    {
        return Contains(payload, "DM");
    }

    public bool IsItemPayload(string payload)
    {
        return Contains(payload, "Item") && Contains(payload, "end");
    }

    public bool IsResultPayload(string payload)
    {
        return Contains(payload, "Test") && Contains(payload, "end");
    }

    public IReadOnlyList<TestItemResult> ParseItems(string payload)
    {
        var items = new List<TestItemResult>();
        foreach (var row in SplitRows(payload))
        {
            var parts = row.Split(',').Select(value => value.Trim()).ToArray();
            if (parts.Length < 5 || !parts[0].StartsWith("Item", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            items.Add(new TestItemResult
            {
                Index = items.Count + 1,
                ItemName = parts.ElementAtOrDefault(1) ?? string.Empty,
                UpperLimit = parts.ElementAtOrDefault(2) ?? string.Empty,
                LowerLimit = parts.ElementAtOrDefault(3) ?? string.Empty,
                Unit = parts.ElementAtOrDefault(4) ?? string.Empty,
                PointA = parts.ElementAtOrDefault(5) ?? string.Empty,
                PointB = parts.ElementAtOrDefault(6) ?? string.Empty
            });
        }

        return items;
    }

    public IReadOnlyList<ParsedTestResult> ParseResults(string payload)
    {
        var results = new List<ParsedTestResult>();
        foreach (var row in SplitRows(payload))
        {
            var parts = row.Split(',').Select(value => value.Trim()).ToArray();
            if (parts.Length < 3 || !parts[0].StartsWith("Test", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var indexText = parts[0].Replace("Test", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!int.TryParse(indexText, out var index))
            {
                continue;
            }

            var value = parts.Length >= 4 ? parts[2] : parts[1];
            var result = parts.Length >= 4 ? parts[3] : parts[2];
            var pointA = string.Empty;
            var pointB = string.Empty;

            if (parts.Length >= 4)
            {
                var points = parts[1].Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                pointA = points.ElementAtOrDefault(0) ?? string.Empty;
                pointB = points.ElementAtOrDefault(1) ?? string.Empty;
            }
            else if (parts[1].Contains(':'))
            {
                var valueParts = parts[1].Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                value = valueParts.ElementAtOrDefault(1) ?? value;
                var points = (valueParts.ElementAtOrDefault(0) ?? string.Empty).Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                pointA = points.ElementAtOrDefault(0) ?? string.Empty;
                pointB = points.ElementAtOrDefault(1) ?? string.Empty;
            }

            results.Add(new ParsedTestResult(index, $"Test{index}", value, result, pointA, pointB));
        }

        return results;
    }

    private static bool Contains(string payload, string value)
    {
        return (payload ?? string.Empty).Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitRows(string payload)
    {
        return (payload ?? string.Empty)
            .Replace("end", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(row => !int.TryParse(row, out _));
    }
}

