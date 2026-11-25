using System.Text.Json;
using CH_V03.App.Airtightness;
using CH_V03.App.Services;
using CH_V03.Core.Airtightness;
using CH_V03.Core.Configuration;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    count++;
    Console.WriteLine("PASS " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
    { Check(true, name); return; }
    throw new Exception("Expected rejection: " + name);
}

var root = Path.Combine(Path.GetTempPath(), "cehao-airtightness-tests-" + Guid.NewGuid().ToString("N"));
var store = new AirtightnessStore(Path.Combine(root, "airtightness"));
var configuration = store.LoadConfiguration();
Check(configuration.MinimumPressureKpa == 15 && configuration.MaximumPressureKpa == 30 && configuration.MaximumLeakPa == 35,
    "requested pressure and leak defaults");
Check(configuration.FillSeconds == 3 && configuration.BalanceSeconds == 3 && configuration.TestSeconds == 5 && configuration.ExhaustSeconds == 1,
    "requested stage durations");
var accounts = store.LoadAccounts();
var electrical = new UserConfiguration();
var electricalJson = JsonSerializer.Serialize(electrical);
var catalog = AirtightnessStore.BuildLoginCatalog(electrical, accounts);
Check(JsonSerializer.Serialize(electrical) == electricalJson, "login catalog does not mutate electrical settings");
var auth = new AuthenticationService();
Check(auth.Login(catalog, "qimi", "123456", out _) && auth.CurrentSession.Module == TestModule.Airtightness, "gas operator routing");
var gasOperator = auth.CurrentSession;
Check(!gasOperator.CanManageSystem, "gas operator cannot configure");
Check(!auth.Login(catalog, "qimi", "wrong", out _), "wrong password rejected");
Check(auth.Login(catalog, "QIMIADMIN", "admin", out _) && auth.CurrentSession.Module == TestModule.Airtightness && auth.CurrentSession.CanManageSystem,
    "gas administrator routing, case insensitive");
var gasAdmin = auth.CurrentSession;
Check(auth.Login(catalog, "admin", "admin", out _) && auth.CurrentSession.Module == TestModule.Electrical, "existing administrator remains electrical");
Reject(() => new AirtightnessViewModel(auth.CurrentSession, store), "electrical administrator denied gas page");
Reject(() => new AirtightnessViewModel(UserSession.Guest, store), "guest denied gas page");
Reject(() => AirtightnessStore.BuildLoginCatalog(electrical, new[] { new UserAccountConfiguration { UserName = "ADMIN" } }), "cross-module duplicate account rejected");
var opModel = new AirtightnessViewModel(gasOperator, store);
Reject(() => opModel.SaveConfiguration(configuration), "operator cannot bypass disabled configuration button");
var model = new AirtightnessViewModel(gasAdmin, store);
var invalid = ConfigurationStore.Clone(configuration);
invalid.MinimumPressureKpa = 31;
Reject(() => model.SaveConfiguration(invalid), "reversed pressure range rejected");
invalid = ConfigurationStore.Clone(configuration); invalid.TestSeconds = 0;
Reject(() => model.SaveConfiguration(invalid), "zero test duration rejected");
invalid = ConfigurationStore.Clone(configuration); invalid.CountingMode = (AirtightnessCountingMode)99;
Reject(() => model.SaveConfiguration(invalid), "undefined counting mode rejected");

var results = new AirtightnessResultStore(store.RecordsDirectory);
var cycle = Guid.NewGuid();
var now = DateTimeOffset.Now;
var r1 = new AirtightnessChannelResult(1, 15, 35, true);
var r2 = new AirtightnessChannelResult(2, 30, 35.01m, true);
results.Record(cycle, now, "qimi", configuration, r1);
results.Record(cycle, now, "qimi", configuration, r1);
var stats = AirtightnessStatistics.Calculate(results.LoadAll(), configuration.CountingMode);
Check(stats.Total == 1 && stats.Pass == 1, "inclusive 15 kPa and 35 Pa; retransmission counted once");
results.Record(cycle, now, "qimi", configuration, r2);
stats = AirtightnessStatistics.Calculate(results.LoadAll(), configuration.CountingMode);
Check(stats.Total == 2 && stats.Pass == 1 && stats.Fail == 1, "separate products: mixed result counts one pass and one fail");
Reject(() => results.Record(cycle, now, "qimi", configuration, r1 with { LeakPa = 10 }), "conflicting duplicate rejected");
Reject(() => results.Record(Guid.NewGuid(), now, "qimi", configuration, r1 with { Channel = 3 }), "invalid channel rejected");
var changed = ConfigurationStore.Clone(configuration); changed.MaximumLeakPa = 40;
Reject(() => results.Record(cycle, now, "qimi", changed, r1), "mid-cycle recipe change rejected");
var outOfRange = new AirtightnessCycle { Recipe = configuration };
Check(!outOfRange.IsPass(r1 with { PressureKpa = 14.99m }) && !outOfRange.IsPass(r1 with { PressureKpa = 30.01m }), "pressure outside limits fails");
Check(outOfRange.IsPass(r1 with { PressureKpa = 30 }) && !outOfRange.IsPass(r1 with { InstrumentPass = false }), "30 kPa inclusive and instrument NG never becomes pass");
configuration.CountingMode = AirtightnessCountingMode.JointProduct;
var joint = Guid.NewGuid();
results.Record(joint, now, "qimi", configuration, r2);
stats = AirtightnessStatistics.Calculate(results.LoadAll(), configuration.CountingMode);
Check(stats.Total == 0 && stats.Channel2Fail == 1, "joint mode: missing channel never finishes product, reversed arrival supported");
results = new AirtightnessResultStore(store.RecordsDirectory);
results.Record(joint, now, "qimi", configuration, r1);
stats = AirtightnessStatistics.Calculate(results.LoadAll(), configuration.CountingMode);
Check(stats.Total == 1 && stats.Pass == 0 && stats.Fail == 1, "joint result survives restart and one NG fails product");
joint = Guid.NewGuid();
results.Record(joint, now, "qimi", configuration, r1);
results.Record(joint, now, "qimi", configuration, r2 with { LeakPa = 35 });
stats = AirtightnessStatistics.Calculate(results.LoadAll(), configuration.CountingMode);
Check(stats.Total == 2 && stats.Pass == 1 && stats.Fail == 1 && stats.Yield == 50, "joint mode both pass counts one product");
model.SaveConfiguration(configuration);
var restarted = new AirtightnessViewModel(gasAdmin, new AirtightnessStore(store.DirectoryPath));
Check(restarted.Statistics.Total == 2 && restarted.Statistics.Pass == 1, "settings and quantity restore after restart");
configuration.CountingMode = AirtightnessCountingMode.SeparateProducts;
restarted.SaveConfiguration(configuration);
Check(restarted.Statistics.Total == 2 && restarted.Statistics.Fail == 1 && restarted.History.Count == 1, "switching mode restores its statistics and history without mixing");
Check(results.LoadAll().Count == 3 && JsonSerializer.Serialize(electrical) == electricalJson, "all gas history retained, electrical config unchanged");
var resultFiles = Directory.EnumerateFiles(store.RecordsDirectory, "*.json").Count();
Check(resultFiles == 3 && !Directory.EnumerateFiles(store.DirectoryPath, "*.tmp", SearchOption.AllDirectories).Any(), "one atomic file per cycle, no temp artifacts");
var originalRecordsDirectory = store.RecordsDirectory;
var custom = ConfigurationStore.Clone(configuration);
custom.RecordsDirectory = Path.Combine(root, "自定义 记录");
custom.LogDirectory = Path.Combine(root, "自定义 日志");
restarted.SaveConfiguration(custom);
Check(restarted.Statistics.Total == 0 && restarted.RecordsDirectory == custom.RecordsDirectory, "new directory loaded immediately without mixing old records");
var customResultStore = new AirtightnessResultStore(store.RecordsDirectory);
customResultStore.Record(Guid.NewGuid(), now, "qimi", custom, r1);
restarted.Refresh();
Check(restarted.Statistics.Total == 1 && Directory.EnumerateFiles(custom.RecordsDirectory, "*.json").Count() == 1, "new results written and read in selected directory");
var csvResultStore = new AirtightnessResultStore(Path.Combine(root, "csv-time-check"));
var csvCycle = Guid.NewGuid();
csvResultStore.RecordCompletedCycle(csvCycle, now, "qimi", custom, "CH1-001", "CH2-001", r1, r2);
var csvPath = csvResultStore.GetCsvFilePath(now);
var csvLines = File.ReadAllLines(csvPath);
Check(csvLines[0].Contains("测试当前时间", StringComparison.Ordinal) &&
      csvLines.Any(line => line.StartsWith(csvCycle.ToString("N") + ",", StringComparison.Ordinal) &&
                           System.Text.RegularExpressions.Regex.IsMatch(line, "^" + csvCycle.ToString("N") + @",\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},")),
      "completed-cycle CSV includes current test time to seconds");
Check(Directory.EnumerateFiles(originalRecordsDirectory, "*.json").Count() == 3, "original records retained after directory change");
restarted.AddLog("自定义路径日志检查");
Check(File.ReadAllText(Path.Combine(custom.LogDirectory, $"{DateTime.Now:yyyy-MM-dd}.log")).Contains("自定义路径日志检查"), "logs written to selected directory immediately");
var customRestarted = new AirtightnessViewModel(gasAdmin, new AirtightnessStore(store.DirectoryPath));
Check(customRestarted.Statistics.Total == 1 && customRestarted.LogDirectory == custom.LogDirectory, "custom paths survive restart");
var badPath = ConfigurationStore.Clone(custom);
badPath.RecordsDirectory = "relative-folder";
Reject(() => customRestarted.SaveConfiguration(badPath), "relative paths rejected");
badPath = ConfigurationStore.Clone(custom);
badPath.LogDirectory = store.ConfigurationFile;
Reject(() => customRestarted.SaveConfiguration(badPath), "file cannot be selected as directory");
Check(store.LoadConfiguration().LogDirectory == custom.LogDirectory, "failed validation preserves previously saved directories");
customRestarted.SaveConfiguration(configuration);
Check(customRestarted.Statistics.Total == 2, "empty legacy paths return to original records without migration");
var simRecipe = new AirtightnessConfiguration();
Check(AirtightnessSimulation.Sample(simRecipe, 0, false).Channel1 == 0, "waveform begins at zero pressure");
Check(AirtightnessSimulation.Sample(simRecipe, 4, false).Stage == "平衡" && AirtightnessSimulation.Sample(simRecipe, 7, false).Stage == "测试", "waveform uses configured stage boundaries");
Check(AirtightnessSimulation.Sample(simRecipe, 12, false).Channel2 == 0 && AirtightnessSimulation.Sample(simRecipe, 12, true).Leak2 > 35, "exhaust returns to zero and fail scenario exceeds limit");
simRecipe.FillSeconds = simRecipe.BalanceSeconds = simRecipe.TestSeconds = simRecipe.ExhaustSeconds = 0.05m;
customRestarted.SaveConfiguration(simRecipe);
var totalBeforeSimulation = customRestarted.Statistics.Total;
var beforeFiles = Directory.EnumerateFiles(store.RecordsDirectory, "*.json").Count();
await customRestarted.RunSimulationAsync();
Check(customRestarted.Channel1State == "模拟 PASS" && customRestarted.SimulationHistory.Count == 1, "simulation completes and has a separate history entry");
Check(customRestarted.PressureSamples.Count >= 2 && customRestarted.PressureSamples.Last().Channel1 == 0, "simulation produces waveform through final exhaust");
await customRestarted.RunSimulationAsync();
Check(customRestarted.Channel2State == "模拟 FAIL" && customRestarted.SimulationSummary.Contains("PASS 1 / FAIL 1"), "second simulation demonstrates one failing independent product");
simRecipe.CountingMode = AirtightnessCountingMode.JointProduct;
customRestarted.SaveConfiguration(simRecipe);
await customRestarted.RunSimulationAsync();
Check(customRestarted.SimulationSummary.Contains("PASS（1件）"), "joint simulation counts one passing product");
await customRestarted.RunSimulationAsync();
Check(customRestarted.SimulationSummary.Contains("FAIL（1件）"), "joint simulation fails when one channel fails");
var simulationCount = customRestarted.SimulationHistory.Count;
var running = customRestarted.RunSimulationAsync();
Check(customRestarted.IsSimulating && !customRestarted.CanConfigure && !customRestarted.CanSwitchAccount, "configuration and account switch locked during simulation");
Reject(() => customRestarted.SaveConfiguration(simRecipe), "simulation prevents programmatic recipe edits");
customRestarted.StopSimulation();
await running;
Check(!customRestarted.IsSimulating && customRestarted.SimulationHistory.Count == simulationCount, "cancelled simulation produces no completed result");
Check(Directory.EnumerateFiles(store.RecordsDirectory, "*.json").Count() == beforeFiles && customRestarted.Statistics.Total == totalBeforeSimulation, "simulation never writes production results or increments production counts");
Console.WriteLine($"Passed {count} checks. Test artifacts: {root}");
