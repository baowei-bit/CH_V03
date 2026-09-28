using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CH_V03.Core.Configuration;
using CH_V03.Core.Mes;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;
using CH_V03.Export.YiDongEap;
using CH_V03.Mes.YiDongEap;

var outputDirectory = Path.Combine(Path.GetTempPath(), "CeHao-YiDongEapSmoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(outputDirectory);

try
{
    var port = ReservePort();
    using var server = new FakeYiDongEapServer(port, expectedRequestCount: 4);
    server.Start();

    var mesPlugin = new YiDongEapMesPlugin();
    var options = new MesPluginOptions
    {
        Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Endpoint"] = $"http://127.0.0.1:{port}/api/Ats/TimsDataTrans",
            ["TimeoutSeconds"] = "5",
            ["CustomerCode"] = "JXYD",
            ["Language"] = "S",
            ["FactoryNo"] = "0",
            ["ApiUser"] = "sa",
            ["ClientIp"] = "127.0.0.1",
            ["LotCardNo"] = "LOT-20260724",
            ["TerminalNo"] = "ICT-01",
            ["EquipmentCode"] = "EQP-001",
            ["FixtureNo"] = "FIX-001",
            ["BarcodeType"] = "SN",
            ["DeductMaterial"] = "Y",
            ["MaterialDetails"] = "1:10,2:11",
            ["EnableFixtureCount"] = "Y",
            ["FixtureCount"] = "1"
        }
    };

    Assert((await mesPlugin.TestConnectionAsync(options)).IsSuccess, "MES configuration validation");
    Assert((await mesPlugin.CheckBeforeTestAsync(options, new MesCheckRequest { Barcode = "SN-0001" })).IsSuccess, "MES pre-check");
    Assert((await mesPlugin.UploadResultAsync(options, new MesUploadRequest
    {
        Barcode = "SN-0001",
        Result = "PASS",
        StartTime = new DateTime(2026, 7, 24, 10, 0, 0),
        EndTime = new DateTime(2026, 7, 24, 10, 0, 2)
    })).IsSuccess, "MES station report and fixture count");

    Assert(server.Requests.Count == 3, "MES request count");
    Assert(server.GetInterfaceType(0) == "TIMS_CHECK_LOTCARD_CARRIER_SN_STATION", "pre-check interface type");
    Assert(server.GetInterfaceType(1) == "TIMS_LOTCARD_CARRIER_SN_STATION", "station report interface type");
    Assert(server.GetInterfaceType(2) == "TIMS_RECEIVE_MOLD_FIXTRUE_COUNT", "fixture count interface type");
    Assert(server.Requests[0].Contains("\"LOTCARD_NO\":\"LOT-20260724\"", StringComparison.Ordinal), "pre-check LOT mapping");
    Assert(server.Requests[1].Contains("\"STATUS\":\"OK\"", StringComparison.Ordinal), "PASS to OK mapping");
    Assert(server.Requests[1].Contains("\"FID\":\"1\",\"QTY\":\"10\"", StringComparison.Ordinal), "material detail mapping");
    Assert(server.Requests[2].Contains("\"TEST_COUNT\":\"1\"", StringComparison.Ordinal), "fixture count mapping");

    options.Values["NoBarcodeMode"] = "Y";
    Assert((await mesPlugin.UploadResultAsync(options, new MesUploadRequest
    {
        Barcode = "NOSCAN20260808120000",
        Result = "PASS"
    })).IsSuccess, "no-barcode fixture upload");
    await server.WaitForRequestsAsync();
    Assert(server.Requests.Count == 4, "no-barcode request count");
    Assert(server.GetInterfaceType(3) == "TIMS_RECEIVE_MOLD_FIXTRUE_COUNT", "no-barcode skips station report");

    var csvPlugin = new YiDongEapRawCsvExportPlugin();
    var configuration = new AppConfiguration();
    configuration.Identity.TestProgram = "ICT-PROG-01";
    configuration.Identity.ProductName = "MODEL-A";
    configuration.Identity.MachineNo = "EQP-001";
    configuration.Identity.StationNo = "ICT-01";
    configuration.User.LoginJobNo = "OP-01";
    configuration.User.FixtureNo = "FIX-001";
    var snapshot = new TestFlowSnapshot
    {
        Barcode = "SN-0001",
        IsPass = true,
        StartTime = new DateTime(2026, 7, 24, 10, 0, 0),
        EndTime = new DateTime(2026, 7, 24, 10, 0, 2),
        Items = new[]
        {
            new TestItemResult { Index = 1, ItemName = "R1", PointA = "A1", PointB = "B1", LowerLimit = "9", UpperLimit = "11", Unit = "Kohm", TestValue = "9.983", Result = "OK" },
            new TestItemResult { Index = 2, ItemName = "C1", PointA = "A2", PointB = "B2", LowerLimit = "0.7", UpperLimit = "1.3", Unit = "uF", TestValue = "0.867", Result = "OK" }
        }
    };
    var exportResult = await csvPlugin.ExportAsync(new ResultExportContext
    {
        Configuration = configuration,
        Snapshot = snapshot,
        PluginOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutputFormat"] = "CSV",
            ["CsvDirectory"] = outputDirectory,
            ["ProgramId"] = "ICT-PROG-01",
            ["DeviceId"] = "EQP-001",
            ["FixtureId"] = "FIX-001",
            ["LineId"] = "LINE-01",
            ["BoardId"] = "B1",
            ["SectionName"] = "CompTest",
            ["CompTestIndexes"] = "1",
            ["FunTestIndexes"] = "2",
            ["FileSuffix"] = "RIGHT",
            ["Operator"] = "OP-01"
        }
    });

    Assert(exportResult.IsSuccess && File.Exists(exportResult.FilePath), "CSV export");
    Assert(Path.GetFileName(exportResult.FilePath) == "SN-0001_20260724100002000_RIGHT.csv", "per-unit CSV filename");
    var bytes = await File.ReadAllBytesAsync(exportResult.FilePath);
    Assert(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "CSV UTF-8 with BOM");
    var csv = Encoding.UTF8.GetString(bytes);
    Assert(csv.Contains("\r\n\r\n", StringComparison.Ordinal), "CSV blank-line sections");
    Assert(csv.Contains("\"DateTime\",\"2026-07-24 10:00:02\",\"\"", StringComparison.Ordinal), "CSV key-value header");
    Assert(csv.Contains("\"CompTest:\"", StringComparison.Ordinal), "CSV CompTest section");
    Assert(csv.Contains("\"FunTest:\"", StringComparison.Ordinal), "CSV FunTest section");
    Assert(csv.Contains("\"Step\",\"BX\",\"Descrip\",\"Module\",\"Type\",\"Function\",\"Delay\",\"Test\",\"Dev%\",\"-%\",\"+%\",\"Result\",\"Info\"", StringComparison.Ordinal), "CSV FunTest header");
    Assert(csv.Contains("\"1\",\"B1\",\"A1\",\"\",\"R1\"", StringComparison.Ordinal), "CSV test row");
    Assert(csv.Contains("\"1\",\"B1\",\"A1\",\"\",\"R1\",\"9~11\",\"\",\"A1\",\"B1\"", StringComparison.Ordinal), "CSV CompTest point limits");
    Assert(csv.Contains("\"2\",\"B1\",\"C1\",\"\",\"\",\"\",\"\",\"0.867\",\"\",\"B2\",\"A2\",\"OK\",\"A2 - B2\"", StringComparison.Ordinal), "CSV FunTest point limits");
    Assert(csv.Contains("\"9.983\"", StringComparison.Ordinal), "CSV measured value");
    Assert(csv.EndsWith("\"LogEnd\"\r\n", StringComparison.Ordinal), "CSV terminator");

    var shortSnapshot = new TestFlowSnapshot
    {
        Barcode = "SN-SHORT",
        IsPass = false,
        StartTime = new DateTime(2026, 7, 24, 10, 2, 0),
        EndTime = new DateTime(2026, 7, 24, 10, 2, 2),
        Items = new[]
        {
            new TestItemResult { Index = 1, ItemName = "SHORT", PointA = "1；3；5", PointB = "2；4；6", TestValue = "0；0；0", Result = "NG" }
        }
    };
    var shortExportResult = await csvPlugin.ExportAsync(new ResultExportContext
    {
        Configuration = configuration,
        Snapshot = shortSnapshot,
        PluginOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutputFormat"] = "CSV",
            ["CsvDirectory"] = outputDirectory,
            ["ShortTestIndexes"] = "1"
        }
    });
    Assert(shortExportResult.IsSuccess, "SHORT CSV export");
    csv = await File.ReadAllTextAsync(shortExportResult.FilePath);
    Assert(csv.Contains("\"ShortFailCount\",\"3\"", StringComparison.Ordinal), "SHORT point-pair count");
    Assert(csv.Contains("=\"\"1 - 2\"\"", StringComparison.Ordinal), "first SHORT point pair is exported as text without apostrophe");
    Assert(csv.Contains("=\"\"3 - 4\"\"", StringComparison.Ordinal), "second SHORT point pair is exported as text without apostrophe");
    Assert(csv.Contains("=\"\"5 - 6\"\"", StringComparison.Ordinal), "third SHORT point pair is exported as text without apostrophe");
    Assert(!csv.Contains("\"CompTest:\"", StringComparison.Ordinal), "SHORT does not enter the CompTest detail section");

    var secondExportResult = await csvPlugin.ExportAsync(new ResultExportContext
    {
        Configuration = configuration,
        Snapshot = new TestFlowSnapshot
        {
            Barcode = "SN-0002",
            IsPass = false,
            StartTime = new DateTime(2026, 7, 24, 10, 1, 0),
            EndTime = new DateTime(2026, 7, 24, 10, 1, 2),
            Items = snapshot.Items
        },
        PluginOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutputFormat"] = "CSV",
            ["CsvDirectory"] = outputDirectory
        }
    });
    Assert(secondExportResult.IsSuccess && secondExportResult.FilePath != exportResult.FilePath, "per-unit CSV has an independent path");
    csv = await File.ReadAllTextAsync(secondExportResult.FilePath);
    Assert(csv.Split("\"DateTime\"", StringSplitOptions.None).Length == 2, "per-unit CSV contains one test record");

    var horizontalDirectory = Path.Combine(outputDirectory, "horizontal");
    var verticalDirectory = Path.Combine(outputDirectory, "vertical");
    var dualCsvResult = await csvPlugin.ExportAsync(new ResultExportContext
    {
        Configuration = configuration,
        Snapshot = snapshot,
        PluginOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutputFormat"] = "CSV",
            ["ExportHorizontalCsv"] = "true",
            ["HorizontalCsvDirectory"] = horizontalDirectory,
            ["HorizontalCsvName"] = "YiDongHorizontal",
            ["ExportVerticalCsv"] = "true",
            ["CsvDirectory"] = verticalDirectory
        }
    });
    var horizontalPath = Path.Combine(horizontalDirectory, "YiDongHorizontal_20260724.csv");
    var verticalPath = Path.Combine(verticalDirectory, "SN-0001_20260724100002000_RIGHT.csv");
    Assert(dualCsvResult.IsSuccess && File.Exists(horizontalPath) && File.Exists(verticalPath), "horizontal and vertical CSV export");
    var horizontalCsv = await File.ReadAllTextAsync(horizontalPath);
    Assert(horizontalCsv.StartsWith("SerialNumber,Test Pass/Fail Status,errCode", StringComparison.Ordinal), "horizontal CSV header");
    Assert(horizontalCsv.Contains("high_point----->", StringComparison.Ordinal), "horizontal CSV point row");
    Assert(horizontalCsv.Contains("SN-0001,PASS", StringComparison.Ordinal), "horizontal CSV result row");

    var txtResult = await csvPlugin.ExportAsync(new ResultExportContext
    {
        Configuration = configuration,
        Snapshot = snapshot,
        PluginOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OutputFormat"] = "HYTM_TXT",
            ["CsvDirectory"] = outputDirectory,
            ["Operator"] = "0001/A",
            ["BoardId"] = "3Y01M2F0000770B0",
            ["TestFilePath"] = @"D:\M1测试文件\3Y01M2F0000770B0\3Y01M2F0000770B0.hyt",
            ["EquipmentNo"] = "ICT0001"
        }
    });
    Assert(txtResult.IsSuccess && File.Exists(txtResult.FilePath), "HYTM TXT export");
    Assert(Path.GetFileName(txtResult.FilePath) == "SN-0001_20260724100002_PASS.txt", "HYTM TXT filename");
    var txt = await File.ReadAllTextAsync(txtResult.FilePath);
    Assert(txt.Contains("HYTM1_1_1 Statistic\r\n", StringComparison.Ordinal), "HYTM TXT title");
    Assert(txt.Contains("Time:20260724 10:00:02 20260724100000", StringComparison.Ordinal), "HYTM TXT time");
    Assert(txt.Contains("OperatorName:0001/A", StringComparison.Ordinal), "HYTM TXT operator");
    Assert(txt.Contains("BoardID:3Y01M2F0000770B0", StringComparison.Ordinal), "HYTM TXT board");
    Assert(txt.Contains("Barcode=SN-0001", StringComparison.Ordinal), "HYTM TXT barcode");
    Assert(txt.Contains("Group_Status=PASS", StringComparison.Ordinal), "HYTM TXT result");
    Assert(txt.Contains("EQUIPMENT_NO:ICT0001", StringComparison.Ordinal), "HYTM TXT equipment");

    Console.WriteLine("YiDong EAP smoke test passed.");
    Console.WriteLine($"Validated MES requests: {server.Requests.Count}");
    Console.WriteLine($"Validated CSV: {exportResult.FilePath}");
}
finally
{
    Directory.Delete(outputDirectory, recursive: true);
}

static int ReservePort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static void Assert(bool condition, string testName)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Smoke test failed: {testName}");
    }
}

internal sealed class FakeYiDongEapServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly int _expectedRequestCount;
    private Task? _loopTask;

    public FakeYiDongEapServer(int port, int expectedRequestCount)
    {
        _expectedRequestCount = expectedRequestCount;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public List<string> Requests { get; } = new();

    public void Start()
    {
        _listener.Start();
        _loopTask = Task.Run(ListenAsync);
    }

    public async Task WaitForRequestsAsync()
    {
        if (_loopTask is not null)
        {
            await _loopTask;
        }
    }

    public string GetInterfaceType(int index)
    {
        using var document = JsonDocument.Parse(Requests[index]);
        return document.RootElement.GetProperty("Header").GetProperty("sInterfaceType").GetString() ?? string.Empty;
    }

    private async Task ListenAsync()
    {
        while (!_cancellation.IsCancellationRequested && Requests.Count < _expectedRequestCount)
        {
            using var client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            _ = await reader.ReadLineAsync();
            var contentLength = 0;
            while (await reader.ReadLineAsync() is { Length: > 0 } header)
            {
                if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    _ = int.TryParse(header["Content-Length:".Length..].Trim(), out contentLength);
                }
            }

            var body = new char[contentLength];
            var read = 0;
            while (read < body.Length)
            {
                var count = await reader.ReadAsync(body.AsMemory(read, body.Length - read), _cancellation.Token);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            Requests.Add(new string(body, 0, read));
            const string responseBody = "{\"code\":\"0\",\"message\":\"OK\",\"data\":[]}";
            var response = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(responseBody)}\r\nConnection: close\r\n\r\n{responseBody}");
            await stream.WriteAsync(response, _cancellation.Token);
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Stop();
        _cancellation.Dispose();
    }
}
