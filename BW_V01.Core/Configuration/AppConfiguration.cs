namespace BW_V01.Core.Configuration;

public sealed class AppConfiguration
{
    public IdentityConfiguration Identity { get; set; } = new();

    public CommunicationConfiguration Communication { get; set; } = new();

    public MesConfiguration Mes { get; set; } = new();

    public OutputConfiguration Output { get; set; } = new();

    public ExportPluginConfiguration Export { get; set; } = new();

    public CompatibilityConfiguration Compatibility { get; set; } = new();

    public PopupConfiguration Popup { get; set; } = new();

    public TestItemConfigurationSet TestItems { get; set; } = new();

    public UserConfiguration User { get; set; } = new();

    public string WindowTitle => string.Join(" - ", new[]
    {
        Identity.CustomerName,
        Identity.ProductName
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class IdentityConfiguration
{
    public string CustomerName { get; set; } = "默认客户";

    public string ProductName { get; set; } = "默认产品";

    public string TestType { get; set; } = "ICT";

    public string TestProgram { get; set; } = "FCT-V1.0";

    public string StationNo { get; set; } = string.Empty;

    public string MachineNo { get; set; } = string.Empty;

    public string BarcodeLength { get; set; } = "12";

    public string CodeSuffix { get; set; } = string.Empty;
}

public sealed class CommunicationConfiguration
{
    public bool ScannerTriggerEnabled { get; set; }

    public string ScannerTriggerCommand { get; set; } = "start\\r\\n";

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
}

public enum ConnectionType
{
    SerialPort,
    TcpClient,
    TcpServer,
    Disabled
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

    public bool AllowOfflineCache { get; set; } = true;

    public Dictionary<string, string> PluginOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OutputConfiguration
{
    public string CsvDirectory { get; set; } = "D:\\VisionResult\\FPC\\ProData\\";

    public string DclDirectory { get; set; } = "D:\\VisionResult\\FPC\\ProOut\\";

    public string LogDirectory { get; set; } = "D:\\VisionResult\\FPC\\Log\\";

    public string CsvName { get; set; } = "F3409-0021-0021CS";
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
    public string ProjectProfileId { get; set; } = string.Empty;

    public string ProtocolAdapterId { get; set; } = "Default.Ict";

    public string ThirdPartyLibraryDirectory { get; set; } = "libs";
}

public sealed class PopupConfiguration
{
    public bool Enabled { get; set; } = true;

    public bool RequireConfirmation { get; set; }
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
    public bool AutoScan { get; set; } = true;

    public int ConsecutiveNgAlarmCount { get; set; } = 5;

    public string LoginJobNo { get; set; } = "admin";

    public string FixtureNo { get; set; } = string.Empty;

    public List<UserAccountConfiguration> Accounts { get; set; } = new()
    {
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
        }
    };

    public void EnsureDefaultAccounts()
    {
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
}

public sealed class UserAccountConfiguration
{
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




