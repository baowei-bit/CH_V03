using CH_V03.App.Services;
using CH_V03.App.ViewModels;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;
using CH_V03.Core.TestFlow;
using CH_V03.Core.ResultExport;
using CH_V03.Export.YiDongEap;
using CH_V03.Export.JiaLianYi;
using CH_V03.Export.SuzhouJiaLianYiLuEnZe;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException(name);
    }
    checks++;
    Console.WriteLine($"PASS: {name}");
}

void Reject(Action action, string expectedReason)
{
    try
    {
        action();
    }
    catch (FormatException exception)
    {
        Check(exception.Message.Contains(expectedReason, StringComparison.Ordinal), expectedReason);
        return;
    }
    throw new InvalidOperationException($"Expected rejection: {expectedReason}");
}

var configuration = new UserConfiguration();
var communicationDefaults = new CommunicationConfiguration();
Check(!communicationDefaults.LowerMachineHandshakeEnabled
    && !communicationDefaults.LowerMachineItemHandshakeEnabled
    && communicationDefaults.LowerMachineHandshakeCommand == "IsOpen"
    && communicationDefaults.LowerMachineHandshakeResponse == "1", "legacy devices keep handshake disabled until configured");
var handshakeSelection = new CommunicationConfiguration { LowerMachineItemHandshakeEnabled = true };
var selectionChanges = new List<string?>();
handshakeSelection.PropertyChanged += (_, args) =>
{
    if (handshakeSelection.LowerMachineHandshakeEnabled && handshakeSelection.LowerMachineItemHandshakeEnabled)
        throw new InvalidOperationException("Both connection handshakes were enabled during a notification");
    selectionChanges.Add(args.PropertyName);
};
handshakeSelection.LowerMachineHandshakeEnabled = true;
Check(handshakeSelection.LowerMachineHandshakeEnabled && !handshakeSelection.LowerMachineItemHandshakeEnabled
    && selectionChanges.Contains(nameof(CommunicationConfiguration.LowerMachineItemHandshakeEnabled)),
    "selecting IsOpen clears Itemnum and updates its checkbox");
selectionChanges.Clear();
handshakeSelection.LowerMachineItemHandshakeEnabled = true;
Check(handshakeSelection.LowerMachineItemHandshakeEnabled && !handshakeSelection.LowerMachineHandshakeEnabled
    && selectionChanges.Contains(nameof(CommunicationConfiguration.LowerMachineHandshakeEnabled)),
    "selecting Itemnum clears IsOpen and updates its checkbox");
var clonedSelection = ConfigurationStore.Clone(handshakeSelection);
Check(clonedSelection.LowerMachineItemHandshakeEnabled && !clonedSelection.LowerMachineHandshakeEnabled,
    "configuration clone preserves the selected handshake");
handshakeSelection.LowerMachineItemHandshakeEnabled = false;
Check(!handshakeSelection.LowerMachineHandshakeEnabled && !handshakeSelection.LowerMachineItemHandshakeEnabled,
    "both handshakes can be unchecked for port-only connection");
foreach (var legacyJson in new[]
{
    "{\"LowerMachineHandshakeEnabled\":true,\"LowerMachineItemHandshakeEnabled\":true}",
    "{\"LowerMachineItemHandshakeEnabled\":true,\"LowerMachineHandshakeEnabled\":true}"
})
{
    var loadedSelection = System.Text.Json.JsonSerializer.Deserialize<CommunicationConfiguration>(legacyJson)!;
    Check(loadedSelection.LowerMachineHandshakeEnabled ^ loadedSelection.LowerMachineItemHandshakeEnabled,
        "legacy double-enabled configuration loads only one handshake");
}
var authentication = new AuthenticationService();
var yidongSession = new UserSession { UserName = "YIDONG", IsAuthenticated = true, Role = UserRole.Operator };
Check(yidongSession.IsYidongAccount && yidongSession.CanManageSystem && yidongSession.CanManageTestItems
    && yidongSession.CanManageUsers && yidongSession.CanImportProjectProfile && yidongSession.CanViewAnalysis,
    "yidong has the same configuration and analysis permissions as admin");
Check(!new UserSession { UserName = "yidong", IsAuthenticated = false }.IsYidongAccount,
    "unauthenticated yidong does not receive account permissions");
Check(!new UserSession { UserName = "operator", IsAuthenticated = true }.CanManageSystem,
    "normal operator permissions remain unchanged");
Check(authentication.Login(configuration, "zuzhuang", "123456", out _), "diagnostic account login");
Check(authentication.CurrentSession.IsDiagnosticMode && !authentication.CurrentSession.CanManageUsers, "diagnostic mode without admin permissions");
Check(!authentication.Login(configuration, "zuzhuang", "wrong", out _), "wrong password rejected");
Check(authentication.Login(configuration, "operator", "123456", out _) && !authentication.CurrentSession.IsDiagnosticMode, "normal account retains normal mode");
Check(authentication.Login(configuration, "jialianyi", "jialianyi", out _) && authentication.CurrentSession.Role == UserRole.Engineer, "JiaLianYi engineer account is available");
configuration.Accounts.RemoveAll(account => account.UserName == "zuzhuang");
configuration.EnsureDefaultAccounts();
configuration.EnsureDefaultAccounts();
Check(configuration.Accounts.Count(account => account.UserName == "zuzhuang") == 1, "old configuration migration is idempotent");
Check(configuration.Accounts.Count(account => account.UserName == "adminEng") == 1, "English administrator account is added once");
Check(authentication.Login(configuration, "ADMINENG", "admin", out _) &&
      authentication.CurrentSession.Role == UserRole.Administrator &&
      authentication.CurrentSession.DisplayName == "Administrator", "English administrator login and permissions");
Check(authentication.Login(configuration, "admin", "admin", out _) &&
      authentication.CurrentSession.DisplayName == "管理员", "Chinese administrator remains unchanged");
configuration.Accounts.Single(account => account.UserName == "zuzhuang").Password = "changed";
configuration.EnsureDefaultAccounts();
Check(authentication.Login(configuration, "ZUZHUANG", "changed", out _) && authentication.CurrentSession.IsDiagnosticMode, "custom password preserved and account name is case insensitive");
authentication.Logout();
Check(!authentication.CurrentSession.IsDiagnosticMode, "logout disables diagnosis");

var scopedConfiguration = new AppConfiguration { User = configuration };
scopedConfiguration.User.Jly.RunMode = JiaLianYiRunMode.Debug;
scopedConfiguration.User.Jly.RequireNumericTestValues = true;
scopedConfiguration.User.Jly.RecordSecondTestYield = true;
scopedConfiguration.User.Jly.ShowFailResultPopup = true;
scopedConfiguration.User.Jly.UseSnTimeForTxtFileName = true;
scopedConfiguration.User.Jly.YieldUploadThreshold = 120;
scopedConfiguration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration = true;
Check(authentication.Login(configuration, "operator", "123456", out _), "operator enters account-scoped settings");
authentication.ApplyAccountMode(scopedConfiguration);
var ordinarySettings = new UserConfigurationViewModel(configuration, scopedConfiguration.Compatibility, false);
Check(!ordinarySettings.ShowJiaLianYiSpecialModes && !ordinarySettings.IsDebugMode,
    "non-JiaLianYi account hides special settings despite saved debug flags");
ordinarySettings.IsStandardPieceMode = true;
ordinarySettings.SaveCommand.Execute(null);
Check(configuration.Jly.RunMode == JiaLianYiRunMode.Debug && configuration.Jly.YieldUploadThreshold == 120,
    "ordinary account settings preserve inactive JiaLianYi choices");
Check(!TestFlowOptions.FromConfiguration(scopedConfiguration).RequireNumericTestValues,
    "ordinary accounts do not inherit JiaLianYi numeric-value restrictions");
Check(ResultExportFileName.ApplyTestPieceSuffix(scopedConfiguration, "piece.csv") == "piece.csv",
    "ordinary accounts do not inherit JiaLianYi debug file naming");
Check(authentication.Login(configuration, "JIALIANYI", "jialianyi", out _) && authentication.CurrentSession.IsJiaLianYiAccount,
    "JiaLianYi account scope is case insensitive");
authentication.ApplyAccountMode(scopedConfiguration);
var dedicatedSettings = new UserConfigurationViewModel(configuration, scopedConfiguration.Compatibility, false);
Check(dedicatedSettings.ShowJiaLianYiSpecialModes && dedicatedSettings.IsDebugMode,
    "JiaLianYi login restores the saved special settings");
Check(TestFlowOptions.FromConfiguration(scopedConfiguration).RequireNumericTestValues
    && ResultExportFileName.ApplyTestPieceSuffix(scopedConfiguration, "piece.csv") == "piece_debug.csv",
    "JiaLianYi login activates numeric checking and debug file naming");
dedicatedSettings.SaveCommand.Execute(null);
Check(configuration.Jly.YieldUploadThreshold == 100, "active JiaLianYi threshold validation is retained");
Check(authentication.LoginAsDefaultOperator(configuration, out _), "switch back to ordinary automatic login");
authentication.ApplyAccountMode(scopedConfiguration);
Check(!scopedConfiguration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration
    && !new UserConfigurationViewModel(configuration, scopedConfiguration.Compatibility, true).ShowJiaLianYiSpecialModes,
    "switching away hides special settings even for account managers");
var importedDedicatedConfiguration = ConfigurationStore.Clone(scopedConfiguration);
importedDedicatedConfiguration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration = true;
authentication.ApplyAccountMode(importedDedicatedConfiguration);
Check(!importedDedicatedConfiguration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration,
    "ordinary login overrides a customer profile's persisted JiaLianYi flag");
Check(authentication.LoginAsConfiguredUser(configuration, "jialianyi", out _) && authentication.CurrentSession.IsJiaLianYiAccount,
    "configured automatic JiaLianYi login activates the same scope");
authentication.ApplyAccountMode(scopedConfiguration);
authentication.Logout();
authentication.ApplyAccountMode(scopedConfiguration);
Check(!scopedConfiguration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration && !authentication.CurrentSession.IsJiaLianYiAccount,
    "logout removes the JiaLianYi account scope");

var adapter = new DefaultIctProtocolAdapter();
var flow = new TestFlowService(adapter);
Check(flow.LoadTestItems("Item1,Voltage,5,0,V;1;end").Items.Count == 1, "valid header with count trailer");
Reject(() => flow.LoadTestItems("Item1,Voltage;end"), "至少 5 个字段");
Check(flow.Snapshot().Items.Single().ItemName == "Voltage", "malformed header preserves previous items");
Check(flow.LoadTestItems("Item1,Voltage,5,0,V;Item3,Current,2,0,A;end").Message.Contains("连续"), "non-contiguous header explains reason");
Reject(() => adapter.ParseResults("Test1,5;end"), "至少 3 个字段");
Reject(() => adapter.ParseResults("TestX,5,OK;end"), "正整数");
Reject(() => flow.CompleteTest(";end"), "未解析到有效测试结果");
Reject(() => flow.CompleteTest("Test1,5,OK;Test1,5,OK;end"), "重复");
Check(flow.Snapshot().IsPass is null, "invalid results never become PASS");
Check(flow.CompleteTest("Test1,5,OK;1;end").IsPass == true, "valid compact result remains compatible");
Check(adapter.ParseResults("Test1,1-2-0^3-4-0,NG;1;end").Count == 1, "short-circuit result remains compatible");
var compoundPointResults = adapter.ParseResults("Test12,19&31-NA&NA:10000,OK;Test157,99&101-NA&NA:0,NG;157;end");
Check(compoundPointResults.Count == 2
    && compoundPointResults[0].Index == 12 && compoundPointResults[0].Value == "19&31:10000"
    && compoundPointResults[0].PointA == string.Empty && compoundPointResults[0].PointB == string.Empty
    && compoundPointResults[0].Result == "OK"
    && compoundPointResults[1].Index == 157 && compoundPointResults[1].Value == "99&101:0"
    && compoundPointResults[1].PointA == string.Empty && compoundPointResults[1].PointB == string.Empty
    && compoundPointResults[1].Result == "NG",
    "compound point result displays left pair and measurement without replacing header points");
var compoundPointFlow = new TestFlowService(adapter);
var compoundHeader = string.Join(';', Enumerable.Range(1, 12)
    .Select(index => $"Item{index},Item{index},20000,0,Ohm,A{index},B{index}")) + ";12;end";
compoundPointFlow.LoadTestItems(compoundHeader);
var compoundPayload = string.Join(';', Enumerable.Range(1, 11)
    .Select(index => $"Test{index},1000,OK")) + ";Test12,19&31-NA&NA:10000,OK;12;end";
var compoundSnapshot = compoundPointFlow.CompleteTest(compoundPayload);
Check(compoundSnapshot.IsPass == true && compoundSnapshot.Items[11].TestValue == "19&31:10000"
    && compoundSnapshot.Items[11].PointA == "A12" && compoundSnapshot.Items[11].PointB == "B12",
    "compound point result appears in test value while configured header points remain intact");
Check(flow.AcceptBarcode("NoRead", new TestFlowOptions()).Message.Contains("NoRead"), "scanner NoRead has explicit reason");
Check(flow.AcceptBarcode("123", new TestFlowOptions()).Message.Contains("长度"), "barcode rule failure is distinct from timeout");
var handshake = new LowerMachineHandshakeReplyParser("1");
Check(handshake.Add("1\r\n").Status == HandshakeReplyStatus.Accepted, "IsOpen reply 1 is accepted");
Check(new LowerMachineHandshakeReplyParser("1").Add("0").Status == HandshakeReplyStatus.Rejected, "IsOpen reply 0 is rejected");
Check(new LowerMachineHandshakeReplyParser("1").Add("10").Status == HandshakeReplyStatus.Rejected, "only standalone reply 1 succeeds");
var fragmentedHandshake = new LowerMachineHandshakeReplyParser("OK");
Check(fragmentedHandshake.Add("O").Status == HandshakeReplyStatus.Pending
    && fragmentedHandshake.Add("K").Status == HandshakeReplyStatus.Accepted, "handshake reply reassembles serial chunks");
var combinedHandshake = new LowerMachineHandshakeReplyParser("1").Add("1Item1,Voltage,5,0,V;1;end");
Check(combinedHandshake.Status == HandshakeReplyStatus.Accepted
    && combinedHandshake.RemainingPayload == "Item1,Voltage,5,0,V;1;end", "handshake reply preserves following Item header");
Check(SpecialTestValue.IsSpecial("s03 ref") && SpecialTestValue.IsSpecial("SWIPE PHASE1 DIFF")
    && !SpecialTestValue.IsSpecial("U101-ID"), "only marked item names are special");
var csvCheckDirectory = Path.Combine(Path.GetTempPath(), "cehao-special-value-" + Guid.NewGuid().ToString("N"));
try
{
    var csvConfiguration = new AppConfiguration();
    csvConfiguration.Output.CsvDirectory = csvCheckDirectory;
    csvConfiguration.Output.SinglePieceCsvDirectory = csvCheckDirectory;
    csvConfiguration.Output.SinglePieceCsvEnabled = true;
    var csvSnapshot = new TestFlowSnapshot
    {
        Barcode = "SN123",
        IsPass = false,
        StartTime = DateTime.Now.AddSeconds(-2),
        EndTime = DateTime.Now,
        Items =
        [
            new TestItemResult { Index = 1, ItemName = "s03 ref", TestValue = "SECRET742", Result = "NG" },
            new TestItemResult { Index = 2, ItemName = "U101-ID", TestValue = "VISIBLE38", Result = "OK" }
        ]
    };
    var hiddenContext = new ResultExportContext
    {
        Configuration = csvConfiguration,
        Snapshot = csvSnapshot,
        PluginOptions = new Dictionary<string, string> { [SpecialTestValue.OptionKey] = "true" }
    };
    var defaultCsv = await new CsvResultExporter().ExportAsync(hiddenContext);
    var singleCsv = await new SinglePieceCsvResultExporter().ExportAsync(hiddenContext);
    Check(defaultCsv.IsSuccess && singleCsv.IsSuccess
        && !File.ReadAllText(defaultCsv.FilePath).Contains("SECRET742", StringComparison.Ordinal)
        && !File.ReadAllText(singleCsv.FilePath).Contains("SECRET742", StringComparison.Ordinal)
        && File.ReadAllText(defaultCsv.FilePath).Contains("VISIBLE38", StringComparison.Ordinal),
        "default and single-piece CSV hide only special measurements");
    var visibleContext = new ResultExportContext { Configuration = csvConfiguration, Snapshot = csvSnapshot };
    Check(SpecialTestValue.CsvValue(visibleContext, csvSnapshot.Items[0]) == "SECRET742"
        && csvSnapshot.Items[0].TestValue == "SECRET742", "unchecked setting preserves raw measurement");
    var customerPlugins = new IResultExportPlugin[]
    {
        new YiDongEapRawCsvExportPlugin(),
        new JiaLianYiExportPlugin(),
        new SuzhouJiaLianYiLuEnZeExportPlugin()
    };
    foreach (var plugin in customerPlugins)
    {
        var pluginDirectory = Path.Combine(csvCheckDirectory, plugin.Id);
        var options = new Dictionary<string, string>
        {
            [SpecialTestValue.OptionKey] = "true",
            ["OutputFormat"] = "CSV",
            ["CsvDirectory"] = pluginDirectory,
            ["DclDirectory"] = pluginDirectory,
            ["DclEnabled"] = "false",
            ["CompTestIndexes"] = "1,2"
        };
        var pluginContext = new ResultExportContext { Configuration = csvConfiguration, Snapshot = csvSnapshot, PluginOptions = options };
        var pluginResult = await plugin.ExportAsync(pluginContext);
        var csvPath = pluginResult.FilePath.Split(';')[0].Trim();
        Check(pluginResult.IsSuccess && File.Exists(csvPath)
            && !File.ReadAllText(csvPath).Contains("SECRET742", StringComparison.Ordinal)
            && File.ReadAllText(csvPath).Contains("VISIBLE38", StringComparison.Ordinal),
            plugin.Name + " hides only special CSV measurements");
        if (plugin is YiDongEapRawCsvExportPlugin)
        {
            var horizontalDirectory = Path.Combine(csvCheckDirectory, "yidong-horizontal");
            options["ExportHorizontalCsv"] = "true";
            options["ExportVerticalCsv"] = "false";
            options["HorizontalCsvDirectory"] = horizontalDirectory;
            var horizontalResult = await plugin.ExportAsync(pluginContext);
            var horizontalCsv = File.ReadAllText(horizontalResult.FilePath);
            Check(horizontalResult.IsSuccess
                && !horizontalCsv.Contains("SECRET742", StringComparison.Ordinal)
                && horizontalCsv.Contains("VISIBLE38", StringComparison.Ordinal),
                "YiDong horizontal CSV hides only special measurements");
        }
    }
}
finally
{
    if (Directory.Exists(csvCheckDirectory)) Directory.Delete(csvCheckDirectory, recursive: true);
}
Console.WriteLine($"Completed {checks} checks.");
