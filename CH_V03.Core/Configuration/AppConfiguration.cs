namespace CH_V03.Core.Configuration;

public sealed class AppConfiguration
{
    public IdentityConfiguration Identity { get; set; } = new();

    public CommunicationConfiguration Communication { get; set; } = new();

    public MesConfiguration Mes { get; set; } = new();

    public OutputConfiguration Output { get; set; } = new();

    public ExportPluginConfiguration Export { get; set; } = new();

    public CompatibilityConfiguration Compatibility { get; set; } = new();

    public PopupConfiguration Popup { get; set; } = new();

    /// <summary>客户端从中心更新服务器获取版本包的设置。</summary>
    public UpdateConfiguration Update { get; set; } = new();

    public TestItemConfigurationSet TestItems { get; set; } = new();

    public UserConfiguration User { get; set; } = new();

    public string WindowTitle
    {
        get
        {
            var identityTitle = string.Join(" - ", new[]
            {
                Identity.CustomerName,
                Identity.ProductName
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

            return string.IsNullOrWhiteSpace(identityTitle) ? "CeHao" : $"CeHao - {identityTitle}";
        }
    }

    /// <summary>
    /// 启用后启动时跳过登录窗口，并以 <see cref="AutoLoginUserName"/> 指定的账号进入系统。
    /// 未指定账号时为兼容旧配置，默认使用 operator 操作员账号。
    /// </summary>
    public bool DefaultOperatorLogin { get; set; }

    /// <summary>
    /// 自动登录账号。首次使用该账号手动登录成功后，会自动启用 <see cref="DefaultOperatorLogin"/>。
    /// </summary>
    public string AutoLoginUserName { get; set; } = string.Empty;
}

public sealed class IdentityConfiguration
{
    public string CustomerName { get; set; } = string.Empty;

    public string ProductName { get; set; } = "默认产品";

    public string TestType { get; set; } = "ICT";

    public string TestProgram { get; set; } = "FCT-V1.0";

    public string StationNo { get; set; } = string.Empty;

    public string MachineNo { get; set; } = string.Empty;

    public string BarcodeLength { get; set; } = "12";

    public string CodeSuffix { get; set; } = string.Empty;

    public bool ScanRuleEnabled { get; set; }

    public string ScanRulePrefix { get; set; } = string.Empty;

    /// <summary>
    /// 允许同一条码连续重复进行测试。关闭时，仅拦截与上一笔实际开始测试相同的条码。
    /// </summary>
    public bool AllowContinuousRepeatTest { get; set; }

    public bool BarcodePositionValidationEnabled { get; set; }

    public int BarcodeValidationPosition { get; set; } = 8;

    public string BarcodeValidationAllowedValues { get; set; } = "L,B";

    public bool BarcodeLimitRuleEnabled { get; set; }

    public int BarcodeLimitItemIndex { get; set; } = 12;

    public string BarcodeLimitUpperByValue { get; set; } = "L=630;B=630";

    public string BarcodeLimitLowerByValue { get; set; } = "L=550;B=480";
}

public sealed class CommunicationConfiguration : System.ComponentModel.INotifyPropertyChanged
{
    private bool _lowerMachineHandshakeEnabled;
    private bool _lowerMachineItemHandshakeEnabled;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public bool AutoConnectScannerOnStartup { get; set; }

    public bool AutoConnectLowerMachineOnStartup { get; set; }

    public bool ScannerTriggerEnabled { get; set; }

    public string ScannerTriggerCommand { get; set; } = "start\\r\\n";

    public string ScannerStopCommand { get; set; } = string.Empty;

    /// <summary>
    /// 下位机触发自动扫码后，等待扫码枪返回条码的最长时间（秒）。
    /// </summary>
    public int ScannerWaitTimeoutSeconds { get; set; } = 10;

    public bool TestItemRequestEnabled { get; set; }

    public string TestItemRequestCommand { get; set; } = "Itemnum\\r\\n";

    /// <summary>连接时发送握手命令并等待指定回复；启用时关闭 Itemnum 连接判据。</summary>
    public bool LowerMachineHandshakeEnabled
    {
        get => _lowerMachineHandshakeEnabled;
        set
        {
            if (_lowerMachineHandshakeEnabled == value) return;
            if (value) LowerMachineItemHandshakeEnabled = false;
            _lowerMachineHandshakeEnabled = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LowerMachineHandshakeEnabled)));
        }
    }

    /// <summary>以完整有效表头作为连接成功条件；启用时关闭 IsOpen 连接判据。</summary>
    public bool LowerMachineItemHandshakeEnabled
    {
        get => _lowerMachineItemHandshakeEnabled;
        set
        {
            if (_lowerMachineItemHandshakeEnabled == value) return;
            if (value) LowerMachineHandshakeEnabled = false;
            _lowerMachineItemHandshakeEnabled = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(LowerMachineItemHandshakeEnabled)));
        }
    }

    public string LowerMachineHandshakeCommand { get; set; } = "IsOpen";

    public string LowerMachineHandshakeResponse { get; set; } = "1";

    /// <summary>
    /// 下位机报文从开始接收到完整 end 的最长允许时间（秒）。
    /// </summary>
    public int LowerMachinePayloadTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// 未收到 end 时，下位机报文可保留的最大字符数；超过后清空，防止异常数据无限累积。
    /// </summary>
    public int LowerMachineReceiveBufferMaximumCharacters { get; set; } = 30000;

    /// <summary>
    /// 发送 Itemnum 后，未收到 Item1 首段时再次请求前的等待时间（秒）。
    /// </summary>
    public int TestItemRequestRetryIntervalSeconds { get; set; } = 5;

    public bool BarcodeSendDefaultOkCommand { get; set; }

    public string BarcodeSuccessCommandByValue { get; set; } = "L=DMOK1\\r\\n;B=DMOK2\\r\\n";

    public DeviceConnectionConfiguration Scanner { get; set; } = new()
    {
        Name = "扫码枪",
        ConnectionType = ConnectionType.SerialPort,
        PortName = "COM9",
        BaudRate = 9600
    };

    public DeviceConnectionConfiguration LowerMachine { get; set; } = new()
    {
        Name = "下位机",
        ConnectionType = ConnectionType.SerialPort,
        PortName = "COM11",
        BaudRate = 115200
    };
}

public sealed class DeviceConnectionConfiguration
{
    public string Name { get; set; } = string.Empty;

    public ConnectionType ConnectionType { get; set; } = ConnectionType.SerialPort;

    public string PortName { get; set; } = string.Empty;

    public int BaudRate { get; set; } = 9600;

    public string IpAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 0;

    public int TimeoutMilliseconds { get; set; } = 3000;

    /// <summary>标准 USB 摄像头序号（内窥镜通常以 UVC 摄像头形式出现）。</summary>
    public int CameraIndex { get; set; }

    public int CameraWidth { get; set; } = 1280;

    public int CameraHeight { get; set; } = 720;

    public int CameraFrameRate { get; set; } = 30;

    /// <summary>一次触发后等待有效画面的最长时间（毫秒）。</summary>
    public int CameraScanTimeoutMilliseconds { get; set; } = 3000;

    /// <summary>归一化扫码选区；宽高为 0 表示识别整幅画面。</summary>
    public double CameraRoiX { get; set; }
    public double CameraRoiY { get; set; }
    public double CameraRoiWidth { get; set; }
    public double CameraRoiHeight { get; set; }

    /// <summary>NoRead 时保存摄像头原始帧的目录。空值使用文档目录下的 CeHao\NoReadFrames。</summary>
    public string CameraNoReadImageDirectory { get; set; } = string.Empty;

    /// <summary>扫描前请求 UVC 摄像头自动对焦；驱动不支持时自动忽略。</summary>
    public bool CameraAutoFocus { get; set; } = true;

    /// <summary>触发自动对焦后等待镜头与画面稳定的时间。</summary>
    public int CameraAutoFocusSettleMilliseconds { get; set; } = 700;
}

public enum ConnectionType
{
    SerialPort = 0,
    TcpClient = 1,
    TcpServer = 2,
    Disabled = 3,
    UsbPosHid = 4,
    CameraQr = 5
}

public sealed class MesConfiguration
{
    public bool Enabled { get; set; }

    public bool OnlineMode { get; set; }

    public bool EnableYieldControlUploadToMes { get; set; }

    public double YieldControlThreshold { get; set; } = 90;

    public string SelectedPlugin { get; set; } = "None";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8080/swiftmom/api/";

    public string UserNo { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string StationNo { get; set; } = string.Empty;

    public string PackNo { get; set; } = string.Empty;

    public string Tooling { get; set; } = string.Empty;

    public string ProdNo { get; set; } = string.Empty;

    /// <summary>弘信 MES 主界面输入的产能/过站流程单号。</summary>
    public string HongXinProductionFlowNo { get; set; } = string.Empty;

    public bool AllowOfflineCache { get; set; } = true;

    public Dictionary<string, string> PluginOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OutputConfiguration : System.ComponentModel.INotifyPropertyChanged
{
    private string _csvDirectory = "D:\\VisionResult\\FPC\\ProData\\";
    private string _dclDirectory = "D:\\VisionResult\\FPC\\ProOut\\";
    private string _logDirectory = "D:\\VisionResult\\FPC\\Log\\";
    private string _customerPathRootDirectory = "D:\\VisionResult\\FPC\\CORR\\";
    private string _singlePieceCsvDirectory = "D:\\VisionResult\\FPC\\SingleCsv\\";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string CsvDirectory
    {
        get => _csvDirectory;
        set => SetProperty(ref _csvDirectory, value);
    }

    /// <summary>
    /// 启用后，产品 CSV 按测试结果写入 CsvDirectory 下的 PASS 或 FAIL 子目录。
    /// </summary>
    public bool ClassifyCsvByResult { get; set; }

    public string DclDirectory
    {
        get => _dclDirectory;
        set => SetProperty(ref _dclDirectory, value);
    }

    public string LogDirectory
    {
        get => _logDirectory;
        set => SetProperty(ref _logDirectory, value);
    }

    public bool CustomerPathSaveEnabled { get; set; }

    public string CustomerPathRootDirectory
    {
        get => _customerPathRootDirectory;
        set => SetProperty(ref _customerPathRootDirectory, value);
    }

    public string CsvName { get; set; } = "F3409-0021-0021CS";

    public bool SinglePieceCsvEnabled { get; set; }

    public string SinglePieceCsvDirectory
    {
        get => _singlePieceCsvDirectory;
        set => SetProperty(ref _singlePieceCsvDirectory, value);
    }

    public string SinglePieceCsvFileName { get; set; } = "{Result}_{SN}_{Time}.csv";

    private void SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ExportPluginConfiguration
{
    public List<string> EnabledPluginIds { get; set; } = new()
    {
        "Default.Csv",
        "Default.Dcl",
        "Default.CsvMes"
    };

    public Dictionary<string, Dictionary<string, string>> PluginOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> GetOptions(string pluginId)
    {
        return PluginOptions.TryGetValue(pluginId, out var options)
            ? new Dictionary<string, string>(options, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class CompatibilityConfiguration
{
    /// <summary>
    /// 启用昆山嘉联益现场的精简配置界面，并锁定对应的 MES / 导出插件。
    /// </summary>
    public bool EnableKunshanJiaLianYiDedicatedConfiguration { get; set; }

    public string ProjectProfileId { get; set; } = string.Empty;

    public string ProtocolAdapterId { get; set; } = "Default.Ict";

    public string ThirdPartyLibraryDirectory { get; set; } = "libs";

    public List<string> VisibleConfigurationFields { get; set; } = new();

    public bool IsFieldVisible(string fieldKey)
    {
        return VisibleConfigurationFields.Count == 0
            || VisibleConfigurationFields.Contains(fieldKey, StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class PopupConfiguration
{
    public bool Enabled { get; set; } = true;

    public bool RequireConfirmation { get; set; }

    /// <summary>
    /// 将运行消息提醒置于主界面中央，并使用更大的显示尺寸。
    /// </summary>
    public bool CenterAndEnlarge { get; set; }

    /// <summary>
    /// 使用红色警示配色显示运行消息提醒。
    /// </summary>
    public bool RedAlert { get; set; }
}

/// <summary>
/// 更新服务器配置。部署时只需将 ServerUrl 改为服务器地址，例如
/// http://192.168.1.20:5080；为空时不检查更新。
/// </summary>
public sealed class UpdateConfiguration
{
    public bool CheckOnStartup { get; set; } = true;

    public string ServerUrl { get; set; } = string.Empty;

    public string Channel { get; set; } = "stable";

    public int CheckTimeoutSeconds { get; set; } = 5;
}

public sealed class TestItemConfigurationSet
{
    public List<TestItemConfiguration> Items { get; set; } = new()
    {
        new TestItemConfiguration
        {
            Order = 1,
            ItemName = "J1_1-SHORT",
            UpperLimit = "10",
            LowerLimit = "0",
            Unit = "ohm",
            PointA = "1",
            PointB = "1"
        },
        new TestItemConfiguration
        {
            Order = 2,
            ItemName = "R1",
            UpperLimit = "11.000",
            LowerLimit = "9.000",
            Unit = "Kohm",
            PointA = "1",
            PointB = "1"
        },
        new TestItemConfiguration
        {
            Order = 3,
            ItemName = "U1-ID",
            UpperLimit = "0x38",
            LowerLimit = "0x38",
            Unit = "HEX",
            PointA = "1",
            PointB = "1"
        }
    };

    public void EnsureDefaultItems()
    {
        if (Items.Count == 0)
        {
            Items.AddRange(new TestItemConfigurationSet().Items);
        }

        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index].Order <= 0)
            {
                Items[index].Order = index + 1;
            }
        }
    }
}

public sealed class TestItemConfiguration
{
    public int Order { get; set; }

    public string ItemName { get; set; } = string.Empty;

    public string UpperLimit { get; set; } = string.Empty;

    public string LowerLimit { get; set; } = string.Empty;

    public string Unit { get; set; } = string.Empty;

    public string PointA { get; set; } = string.Empty;

    public string PointB { get; set; } = string.Empty;
}

public sealed class UserConfiguration
{
    /// <summary>Hide the marked special-item measurements on the live test screen.</summary>
    public bool HideSpecialItemTestValues { get; set; }

    /// <summary>
    /// 嘉联益现场专用的测试及 MES 上报规则。
    /// </summary>
    public JiaLianYiConfiguration Jly { get; set; } = new();

    public bool AutoScan { get; set; } = true;

    public bool JingWangKeyboardSn { get; set; }

    public bool ManualScanMode { get; set; }

    public bool NoScanMode { get; set; }

    /// <summary>
    /// 手动扫码时，先完成条码校验和 MES 卡关；收到下位机 DM 后才放行测试。
    /// </summary>
    public bool ScanAheadEnabled { get; set; }

    /// <summary>
    /// 是否在应用重启后继续保留当天的 PASS/FAIL 统计。
    /// </summary>
    public bool SaveDailyQuantity { get; set; }

    /// <summary>
    /// 最近一次保存的当天统计。仅在 <see cref="SaveDailyQuantity"/> 启用时使用。
    /// </summary>
    public DailyYieldStatistics DailyYieldStatistics { get; set; } = new();

    public int ConsecutiveNgAlarmCount { get; set; } = 5;

    public bool PreferJingWangLogin { get; set; }

    public string LoginJobNo { get; set; } = "admin";

    public string FixtureNo { get; set; } = string.Empty;

    public List<UserAccountConfiguration> Accounts { get; set; } = new()
    {
        new UserAccountConfiguration
        {
            UserName = "zuzhuang",
            DisplayName = "组装诊断",
            Password = "123456",
            Role = UserRole.Technician
        },
        new UserAccountConfiguration
        {
            UserName = "operator",
            DisplayName = "操作员",
            Password = "123456",
            Role = UserRole.Operator
        },
        new UserAccountConfiguration
        {
            UserName = "engineer",
            DisplayName = "工程师",
            Password = "123",
            Role = UserRole.Engineer
        },
        new UserAccountConfiguration
        {
            UserName = "admin",
            DisplayName = "管理员",
            Password = "admin",
            Role = UserRole.Administrator
        },
        new UserAccountConfiguration
        {
            UserName = "adminEng",
            DisplayName = "Administrator",
            Password = "admin",
            Role = UserRole.Administrator
        },
        new UserAccountConfiguration
        {
            UserName = "huaqin",
            DisplayName = "华勤管理员",
            Password = "admin",
            Role = UserRole.Administrator
        },
        new UserAccountConfiguration
        {
            UserName = "jialianyi",
            DisplayName = "嘉联益工程师",
            Password = "jialianyi",
            Role = UserRole.Engineer
        },
        CreateJingWangAccount(),
        CreateYiDongAccount()
    };

    public void EnsureDefaultAccounts()
    {
        if (!Accounts.Any(account => string.Equals(account.UserName, "jingwang", StringComparison.OrdinalIgnoreCase)))
            Accounts.Add(CreateJingWangAccount());
        if (!Accounts.Any(account => string.Equals(account.UserName, "yidong", StringComparison.OrdinalIgnoreCase)))
        {
            Accounts.Add(CreateYiDongAccount());
        }
        if (!Accounts.Any(account => string.Equals(account.UserName, "zuzhuang", StringComparison.OrdinalIgnoreCase)))
        {
            Accounts.Add(new UserAccountConfiguration
            {
                UserName = "zuzhuang",
                DisplayName = "组装诊断",
                Password = "123456",
                Role = UserRole.Technician
            });
        }
        UpsertDefaultAccount(new UserAccountConfiguration
        {
            UserName = "operator",
            DisplayName = "操作员",
            Password = "123456",
            Role = UserRole.Operator
        });
        UpsertDefaultAccount(new UserAccountConfiguration
        {
            UserName = "engineer",
            DisplayName = "工程师",
            Password = "123",
            Role = UserRole.Engineer
        });
        UpsertDefaultAccount(new UserAccountConfiguration
        {
            UserName = "admin",
            DisplayName = "管理员",
            Password = "admin",
            Role = UserRole.Administrator
        });
        UpsertDefaultAccount(new UserAccountConfiguration
        {
            UserName = "adminEng",
            DisplayName = "Administrator",
            Password = "admin",
            Role = UserRole.Administrator
        });
        UpsertDefaultAccount(new UserAccountConfiguration
        {
            UserName = "huaqin",
            DisplayName = "华勤管理员",
            Password = "admin",
            Role = UserRole.Administrator
        });
        UpsertDefaultAccount(new UserAccountConfiguration
        {
            UserName = "jialianyi",
            DisplayName = "嘉联益工程师",
            Password = "jialianyi",
            Role = UserRole.Engineer
        });
    }

    private void UpsertDefaultAccount(UserAccountConfiguration account)
    {
        var existing = Accounts.FirstOrDefault(item =>
            string.Equals(item.UserName, account.UserName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            Accounts.Add(account);
            return;
        }

        existing.DisplayName = account.DisplayName;
        existing.Password = account.Password;
        existing.Role = account.Role;
    }

    private static UserAccountConfiguration CreateJingWangAccount() => new()
    {
        UserName = "jingwang", Password = "jingwang", DisplayName = "景旺管理员", Role = UserRole.Administrator
    };

    private static UserAccountConfiguration CreateYiDongAccount() => new()
    {
        UserName = "yidong",
        DisplayName = "yidong 联合测试",
        Password = "yidong",
        Role = UserRole.Operator
    };
}

public sealed class JiaLianYiConfiguration
{
    /// <summary>嘉联益运行模式。调试模式下导出的 CSV 文件名追加 _debug。</summary>
    public JiaLianYiRunMode RunMode { get; set; } = JiaLianYiRunMode.StandardPiece;

    /// <summary>兼容旧配置：False 为标准片模式；True 为生产模式。</summary>
    public bool TestPieceMode
    {
        get => RunMode == JiaLianYiRunMode.TestPiece;
        set
        {
            if (value)
            {
                RunMode = JiaLianYiRunMode.TestPiece;
            }
            else if (RunMode == JiaLianYiRunMode.TestPiece)
            {
                RunMode = JiaLianYiRunMode.StandardPiece;
            }
        }
    }

    public bool DebugMode => RunMode == JiaLianYiRunMode.Debug;

    /// <summary>生产模式下，良率小于或等于此值时不上传 MES。</summary>
    public double YieldUploadThreshold { get; set; } = 90;

    public bool UseSnTimeForTxtFileName { get; set; }

    public bool ShowFailResultPopup { get; set; }

    public bool RequireNumericTestValues { get; set; }

    public bool RecordSecondTestYield { get; set; }
}

public enum JiaLianYiRunMode
{
    StandardPiece,
    TestPiece,
    Debug
}

public sealed class DailyYieldStatistics
{
    /// <summary>
    /// 统计所属日期，格式为 yyyy-MM-dd。
    /// </summary>
    public string Date { get; set; } = string.Empty;

    public int PassCount { get; set; }

    public int FailCount { get; set; }
}

public sealed class UserAccountConfiguration
{
    public TestModule Module { get; set; } = TestModule.Electrical;

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Operator;
}

public enum UserRole
{
    Operator,
    Technician,
    Engineer,
    Administrator
}

public enum TestModule
{
    Electrical,
    Airtightness
}




