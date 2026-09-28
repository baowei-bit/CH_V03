using System.Text.Json;
using CH_V03.Core.Configuration;

namespace CH_V03.Core.Airtightness;

public enum AirtightnessCountingMode { SeparateProducts, JointProduct }
public enum AirtightnessScanMode { Manual, Automatic, NoScan }

public sealed class AirtightnessConfiguration
{
    public string InstrumentModel { get; set; } = "博仪 BYD-100Z-22";
    public string ProductName { get; set; } = "双通道气密检测";
    public decimal MinimumPressureKpa { get; set; } = 15;
    public decimal MaximumPressureKpa { get; set; } = 30;
    public decimal FillSeconds { get; set; } = 3;
    public decimal BalanceSeconds { get; set; } = 3;
    public decimal TestSeconds { get; set; } = 5;
    public decimal ExhaustSeconds { get; set; } = 1;
    public decimal MaximumLeakPa { get; set; } = 35;
    public AirtightnessCountingMode CountingMode { get; set; }
    /// <summary>手动扫码、自动接收扫码枪数据或不扫码。</summary>
    public AirtightnessScanMode ScanMode { get; set; } = AirtightnessScanMode.Manual;
    /// <summary>自动扫码时，是否由上位机向串口/TCP 扫码枪发送触发命令。</summary>
    public bool ScannerTriggerEnabled { get; set; } = true;
    /// <summary>扫码枪开始读码命令，支持 \r、\n、\t 转义字符。</summary>
    public string ScannerTriggerCommand { get; set; } = "start\\r\\n";
    /// <summary>扫码结束或超时时发送的停止命令；设备不需要时可留空。</summary>
    public string ScannerStopCommand { get; set; } = string.Empty;
    /// <summary>一次双通道扫码流程等待两支条码的最长时间（秒）。</summary>
    public int ScannerWaitTimeoutSeconds { get; set; } = 10;
    public DeviceConnectionConfiguration Scanner { get; set; } = new()
    {
        Name = "气密扫码枪",
        ConnectionType = ConnectionType.UsbPosHid,
        BaudRate = 9600
    };
    public AirtightnessInstrumentConfiguration Instrument { get; set; } = new();
    public AirtightnessMotionConfiguration Motion { get; set; } = new();
    public string RecordsDirectory { get; set; } = string.Empty;
    public string LogDirectory { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProductName)) throw new InvalidDataException("请填写产品名称。");
        if (MinimumPressureKpa <= 0 || MaximumPressureKpa < MinimumPressureKpa)
            throw new InvalidDataException("气压下限必须大于 0，且不能大于上限。");
        if (FillSeconds <= 0 || BalanceSeconds <= 0 || TestSeconds <= 0 || ExhaustSeconds <= 0)
            throw new InvalidDataException("各阶段时间必须大于 0 秒。");
        if (MaximumLeakPa < 0) throw new InvalidDataException("压降上限不能小于 0 Pa。");
        if (!Enum.IsDefined(CountingMode)) throw new InvalidDataException("统计模式无效。");
        if (!Enum.IsDefined(ScanMode)) throw new InvalidDataException("扫码模式无效。");
        if (ScannerWaitTimeoutSeconds is < 1 or > 600) throw new InvalidDataException("扫码等待超时必须在 1–600 秒之间。");
        Scanner ??= new DeviceConnectionConfiguration { Name = "气密扫码枪", ConnectionType = ConnectionType.UsbPosHid };
        Instrument ??= new AirtightnessInstrumentConfiguration();
        Motion ??= new AirtightnessMotionConfiguration();
        Instrument.Validate();
        Motion.Validate();
        if (Motion.Enabled && string.Equals(Instrument.PortName.Trim(), Motion.PortName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("当前气密仪与动作模块使用独立串口连接，请为动作控制模块选择不同于气密仪的串口号。");
        ValidateDirectory(RecordsDirectory, "测试记录保存路径");
        ValidateDirectory(LogDirectory, "日志保存路径");
    }

    private static void ValidateDirectory(string? directory, string label)
    {
        // Empty values retain the directories used by older configurations.
        if (string.IsNullOrWhiteSpace(directory)) return;
        if (!Path.IsPathFullyQualified(directory) || directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new InvalidDataException($"{label}请填写完整文件夹路径，或点击浏览选择。");
        _ = Path.GetFullPath(directory);
        if (File.Exists(directory)) throw new InvalidDataException($"{label}不能选择文件，请选择文件夹。");
    }
}

/// <summary>外接 Modbus RTU 数字量模块的双通道继电器控制配置。</summary>
public sealed class AirtightnessMotionConfiguration
{
    /// <summary>关闭时上位机绝不发送继电器开关命令。</summary>
    public bool Enabled { get; set; }
    /// <summary>仅在明确启用后，CH1/CH2 两个条码齐全时自动开启两个动作输出。</summary>
    public bool AutoOpenAfterBarcode { get; set; }
    public string PortName { get; set; } = "COM12";
    public int BaudRate { get; set; } = 38400;
    public byte StationAddress { get; set; } = 2;
    public int TimeoutMilliseconds { get; set; } = 1000;
    /// <summary>数字量模块的 Y 输出编号，1 对应协议线圈 0000H。</summary>
    public int Channel1Output { get; set; } = 1;
    public int Channel2Output { get; set; } = 2;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName)) throw new InvalidDataException("请填写动作控制模块串口号。");
        if (BaudRate <= 0) throw new InvalidDataException("动作控制模块波特率必须大于 0。");
        if (StationAddress is 0) throw new InvalidDataException("动作控制模块站号必须为 1–247。");
        if (TimeoutMilliseconds is < 200 or > 30000) throw new InvalidDataException("动作控制模块超时必须在 200–30000 ms 之间。");
        if (Channel1Output is < 1 or > 72 || Channel2Output is < 1 or > 72 || Channel1Output == Channel2Output)
            throw new InvalidDataException("CH1、CH2 输出编号必须在 1–72 之间且不能相同。");
    }
}

/// <summary>BYZ 气密仪的 Modbus RTU 连接参数。默认值来自厂家协议 V1.5。</summary>
public sealed class AirtightnessInstrumentConfiguration
{
    public string PortName { get; set; } = "COM10";
    public int BaudRate { get; set; } = 9600;
    public byte StationAddress { get; set; } = 1;
    public int TimeoutMilliseconds { get; set; } = 3000;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName)) throw new InvalidDataException("请填写气密仪串口号。");
        if (BaudRate <= 0) throw new InvalidDataException("气密仪波特率必须大于 0。");
        if (StationAddress is 0) throw new InvalidDataException("气密仪站号必须为 1–247。");
        if (TimeoutMilliseconds is < 200 or > 30000) throw new InvalidDataException("气密仪超时必须在 200–30000 ms 之间。");
    }
}

public sealed class AirtightnessStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public AirtightnessStore(string directory) => DirectoryPath = directory;
    public string DirectoryPath { get; }
    public string ConfigurationFile => Path.Combine(DirectoryPath, "settings.json");
    public string AccountsFile => Path.Combine(DirectoryPath, "accounts.json");
    public string RecordsDirectory => GetRecordsDirectory(LoadConfiguration());
    public string GetRecordsDirectory(AirtightnessConfiguration configuration) => ResolveDirectory(configuration.RecordsDirectory, "records");
    public string GetLogDirectory(AirtightnessConfiguration configuration) => ResolveDirectory(configuration.LogDirectory, "logs");

    private string ResolveDirectory(string? configured, string fallback) => Path.GetFullPath(
        string.IsNullOrWhiteSpace(configured) ? Path.Combine(DirectoryPath, fallback) : configured);

    public AirtightnessConfiguration LoadConfiguration()
    {
        var value = File.Exists(ConfigurationFile)
            ? JsonSerializer.Deserialize<AirtightnessConfiguration>(File.ReadAllText(ConfigurationFile))
                ?? throw new InvalidDataException("气密配置为空。")
            : new AirtightnessConfiguration();
        value.Validate();
        return value;
    }

    public void SaveConfiguration(AirtightnessConfiguration configuration)
    {
        configuration.Validate();
        EnsureWritableDirectory(GetRecordsDirectory(configuration));
        EnsureWritableDirectory(GetLogDirectory(configuration));
        WriteAtomic(ConfigurationFile, configuration);
    }

    private static void EnsureWritableDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var probe = Path.Combine(directory, $".cehao-write-check-{Guid.NewGuid():N}.tmp");
        using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        stream.WriteByte(0);
        stream.Flush(true);
    }

    public List<UserAccountConfiguration> LoadAccounts()
    {
        if (!File.Exists(AccountsFile))
        {
            WriteAtomic(AccountsFile, new[]
            {
                new UserAccountConfiguration { UserName = "qimi", DisplayName = "气密操作员", Password = "123456", Role = UserRole.Operator, Module = TestModule.Airtightness },
                new UserAccountConfiguration { UserName = "qimiadmin", DisplayName = "气密管理员", Password = "admin", Role = UserRole.Administrator, Module = TestModule.Airtightness }
            });
        }
        var accounts = JsonSerializer.Deserialize<List<UserAccountConfiguration>>(File.ReadAllText(AccountsFile))
            ?? throw new InvalidDataException("气密账号文件为空。");
        foreach (var account in accounts) account.Module = TestModule.Airtightness;
        return accounts;
    }

    public static UserConfiguration BuildLoginCatalog(UserConfiguration electrical, IEnumerable<UserAccountConfiguration> airtightness)
    {
        var catalog = ConfigurationStore.Clone(electrical);
        foreach (var account in catalog.Accounts) account.Module = TestModule.Electrical;
        foreach (var source in airtightness)
        {
            var account = ConfigurationStore.Clone(source);
            account.Module = TestModule.Airtightness;
            catalog.Accounts.Add(account);
        }
        if (catalog.Accounts.Any(a => string.IsNullOrWhiteSpace(a.UserName)) ||
            catalog.Accounts.GroupBy(a => a.UserName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new InvalidDataException("电测与气密账号名称必须非空且不能重复，请检查账号配置。");
        return catalog;
    }

    public static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
