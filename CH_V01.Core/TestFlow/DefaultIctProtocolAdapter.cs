namespace CH_V01.Core.TestFlow;

public sealed class DefaultIctProtocolAdapter : ITestProtocolAdapter
{
    private static readonly System.Text.RegularExpressions.Regex ItemRowPattern = new(
        @"^Item(?<index>[1-9]\d*)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

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
        return string.Equals((payload ?? string.Empty).Trim(), "DM", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsItemPayload(string payload)
    {
        var normalized = (payload ?? string.Empty).Trim();
        return normalized.StartsWith("Item1,", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith(";end", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsResultPayload(string payload)
    {
        var normalized = (payload ?? string.Empty).Trim();
        return normalized.StartsWith("Test1,", StringComparison.OrdinalIgnoreCase)
            && normalized.EndsWith(";end", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<TestItemResult> ParseItems(string payload)
    {
        var items = new List<TestItemResult>();
        foreach (var row in SplitRows(payload))
        {
            var parts = row.Split(',').Select(value => value.Trim()).ToArray();
            var itemMatch = ItemRowPattern.Match(parts.ElementAtOrDefault(0) ?? string.Empty);
            if (parts.Length < 5 || !itemMatch.Success || !int.TryParse(itemMatch.Groups["index"].Value, out var index))
            {
                continue;
            }

            items.Add(new TestItemResult
            {
                Index = index,
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
                (pointA, pointB) = ParsePointPair(parts[1]);
            }
            else if (parts[1].Contains(':'))
            {
                var valueParts = parts[1].Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                value = valueParts.ElementAtOrDefault(1) ?? value;
                (pointA, pointB) = ParsePointPair(valueParts.ElementAtOrDefault(0) ?? string.Empty);
            }

            results.Add(new ParsedTestResult(index, string.Empty, value, result, pointA, pointB));
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

    private static (string PointA, string PointB) ParsePointPair(string value)
    {
        var normalized = (value ?? string.Empty).Trim().Trim('(', ')');
        var points = normalized.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (points.ElementAtOrDefault(0) ?? string.Empty, points.ElementAtOrDefault(1) ?? string.Empty);
    }
}
