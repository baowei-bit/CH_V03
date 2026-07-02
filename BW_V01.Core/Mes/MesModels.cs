namespace BW_V01.Core.Mes;

public sealed class MesPluginCapabilities
{
    public bool SupportsConnectionTest { get; init; }

    public bool SupportsLogin { get; init; }

    public bool SupportsPreCheck { get; init; }

    public bool SupportsResultUpload { get; init; }

    public bool SupportsYieldControl { get; init; }

    public bool SupportsOfflineCache { get; init; }
}

public sealed class MesPluginConfigField
{
    public string Key { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string DefaultValue { get; init; } = string.Empty;

    public bool IsSecret { get; init; }

    public bool IsRequired { get; init; }
}

public sealed class MesPluginOptions
{
    public Dictionary<string, string> Values { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string Get(string key, string defaultValue = "")
    {
        return Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }
}

public sealed class MesCheckRequest
{
    public string Barcode { get; init; } = string.Empty;

    public string ProdNo { get; init; } = string.Empty;

    public string StationNo { get; init; } = string.Empty;

    public string Retest { get; init; } = string.Empty;

    public string UserNo { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;
}

public sealed class MesUploadRequest
{
    public string Barcode { get; init; } = string.Empty;

    public string ProdNo { get; init; } = string.Empty;

    public string StationNo { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public string Remark { get; init; } = string.Empty;

    public string UserNo { get; init; } = string.Empty;

    public string Weight { get; init; } = string.Empty;

    public string PackNo { get; init; } = string.Empty;

    public string Retest { get; init; } = string.Empty;

    public DateTime StartTime { get; init; }

    public DateTime EndTime { get; init; }

    public IReadOnlyList<MesTestItem> TestItems { get; init; } = Array.Empty<MesTestItem>();
}

public sealed class MesTestItem
{
    public string Name { get; init; } = string.Empty;

    public string Value { get; init; } = string.Empty;

    public string Unit { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public string UpperLimit { get; init; } = string.Empty;

    public string LowerLimit { get; init; } = string.Empty;
}

public sealed class MesResult
{
    public bool IsSuccess { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;

    public string RawResponse { get; init; } = string.Empty;

    public static MesResult Success(string message, string code = "0", string token = "", string rawResponse = "")
    {
        return new MesResult
        {
            IsSuccess = true,
            Code = code,
            Message = message,
            Token = token,
            RawResponse = rawResponse
        };
    }

    public static MesResult Fail(string message, string code = "1", string rawResponse = "")
    {
        return new MesResult
        {
            IsSuccess = false,
            Code = code,
            Message = message,
            RawResponse = rawResponse
        };
    }
}
