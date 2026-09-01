using System.Net;
using System.Net.Sockets;
using System.Text;
using CH_V01.Core.Configuration;
using CH_V01.Core.Mes;
using CH_V01.Core.ResultExport;
using CH_V01.Core.TestFlow;
using CH_V01.Export.Bomin;
using CH_V01.Mes.Bomin;

var tempRoot = Path.Combine(Path.GetTempPath(), "CH_V01_BominSmoke_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    Smoke.Assert(await TestFlowAndExportAsync(tempRoot), "test flow and export");
    Smoke.Assert(await TestMesPluginAsync(), "MES plugin HTTP calls");
    Console.WriteLine("Bomin migration smoke test passed.");
    Console.WriteLine(tempRoot);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Bomin migration smoke test failed.");
    Console.Error.WriteLine(ex);
    return 1;
}

static async Task<bool> TestFlowAndExportAsync(string tempRoot)
{
    var configuration = BuildConfiguration(tempRoot);
    var flow = new TestFlowService(new DefaultIctProtocolAdapter());
    flow.LoadConfiguredTestItems(BuildTestItems());

    var barcode = "1234567L9ABC";
    var accepted = flow.AcceptBarcode(barcode, TestFlowOptions.FromConfiguration(configuration));
    if (accepted.State != TestFlowState.Ready)
    {
        throw new InvalidOperationException("Barcode should be accepted.");
    }

    flow.UpdateItemLimits(12, "550", "630");
    flow.StartTest();
    var snapshot = flow.CompleteTest(BuildPassPayload());
    if (snapshot.IsPass != true)
    {
        throw new InvalidOperationException("PASS payload should produce PASS snapshot.");
    }

    Smoke.Assert(snapshot.Items[0].Result == "OK", "test item result comes from lower machine");
    Smoke.Assert(snapshot.Items[0].PointA == "1", "original point-pair payload keeps point A");
    Smoke.Assert(snapshot.Items[0].PointB == "1", "original point-pair payload keeps point B");
    Smoke.Assert(snapshot.Items[0].TestValue == "5", "original point-pair payload keeps its test value");

    var originalCompactResult = new DefaultIctProtocolAdapter().ParseResults("Test1,5.000,OK;1;end").Single();
    Smoke.Assert(originalCompactResult.Value == "5.000", "original compact payload keeps its test value");
    Smoke.Assert(originalCompactResult.Result == "OK", "original compact payload keeps its result");
    Smoke.Assert(originalCompactResult.PointA == string.Empty && originalCompactResult.PointB == string.Empty,
        "original compact payload has no point pair");

    flow.StartTest();
    var multiShortSnapshot = flow.CompleteTest("Test1,1-2-0^3-4-0^5-6-0,NG;1;end");
    Smoke.Assert(multiShortSnapshot.IsPass == false, "multi-point SHORT NG produces a FAIL snapshot");
    Smoke.Assert(multiShortSnapshot.Items[0].PointA == "1；3；5", "multi-point SHORT high points are combined");
    Smoke.Assert(multiShortSnapshot.Items[0].PointB == "2；4；6", "multi-point SHORT low points are combined");
    Smoke.Assert(multiShortSnapshot.Items[0].TestValue == "0；0；0", "multi-point SHORT resistance values are combined");
    Smoke.Assert(multiShortSnapshot.Items[0].Result == "NG", "multi-point SHORT result comes from lower machine");

    flow.StartTest();
    var singleShortSnapshot = flow.CompleteTest("Test1,1-2-0^,NG;1;end");
    Smoke.Assert(singleShortSnapshot.IsPass == false, "single SHORT NG produces a FAIL snapshot");
    Smoke.Assert(singleShortSnapshot.Items[0].PointA == "1", "single SHORT high point is parsed");
    Smoke.Assert(singleShortSnapshot.Items[0].PointB == "2", "single SHORT low point is parsed");
    Smoke.Assert(singleShortSnapshot.Items[0].TestValue == "0", "single SHORT resistance is parsed");

    flow.StartTest();
    var outOfLimitOkSnapshot = flow.CompleteTest(BuildOutOfLimitOkPayload());
    Smoke.Assert(outOfLimitOkSnapshot.IsPass == true, "lower machine OK result is not overridden by upper-machine limits");
    Smoke.Assert(outOfLimitOkSnapshot.Items[0].Result == "OK", "out-of-limit item keeps lower-machine OK result");

    var exportPlugin = new BominExportPlugin();
    var context = new ResultExportContext
    {
        Configuration = configuration,
        Snapshot = snapshot,
        PluginOptions = configuration.Export.GetOptions(exportPlugin.Id)
    };

    var result = await exportPlugin.ExportAsync(context);
    if (!result.IsSuccess)
    {
        throw new InvalidOperationException(result.Message);
    }

    var expectedCsv = Path.Combine(tempRoot, "ProData", "9", "L", "CSV", "F3409-0021-0021CS_" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
    var expectedDcl = Path.Combine(tempRoot, "ProOut", barcode + ".dcl");
    var expectedCsvMes = Path.Combine(tempRoot, "ProOut", DateTime.Now.ToString("yyyyMMdd") + ".csv");

    Smoke.Assert(File.Exists(expectedCsv), "CSV path includes barcode 9/L/CSV folders");
    Smoke.Assert(File.Exists(expectedDcl), "DCL file created by barcode");
    Smoke.Assert(File.Exists(expectedCsvMes), "CsvMes file created");

    var csv = await File.ReadAllTextAsync(expectedCsv);
    var dcl = await File.ReadAllTextAsync(expectedDcl, Encoding.Default);
    var csvMes = await File.ReadAllTextAsync(expectedCsvMes);

    Smoke.Assert(csv.Contains("SerialNumber,Test Pass/Fail Status", StringComparison.Ordinal), "CSV header");
    Smoke.Assert(csv.Contains(barcode + ",PASS", StringComparison.Ordinal), "CSV result row");
    Smoke.Assert(dcl.Contains("// Header_Data", StringComparison.Ordinal), "DCL header");
    Smoke.Assert(dcl.Contains("LED1-nm", StringComparison.Ordinal), "DCL component data");
    Smoke.Assert(csvMes.Contains("SerialNumber,StartTime,Test Pass/Fail Status", StringComparison.Ordinal), "CsvMes header");
    return true;
}

static async Task<bool> TestMesPluginAsync()
{
    await using var server = new LocalHttpMesServer();
    await server.StartAsync();

    var plugin = new BominMesPlugin();
    var options = new MesPluginOptions
    {
        Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LoginUrl"] = server.Url("login"),
            ["PreCheckUrl"] = server.Url("prevalidation"),
            ["UploadUrl"] = server.Url("records"),
            ["TimeoutSeconds"] = "5",
            ["ModifyUser"] = "operator01",
            ["Password"] = "pwd",
            ["Role"] = "0",
            ["TestType"] = "FCT",
            ["ProductName"] = "BominProduct",
            ["Program"] = "BominProduct",
            ["IpAddress"] = "10.1.226.219",
            ["ToolNumber"] = "FS00035535-0001",
            ["SlotName"] = "1",
            ["SubSlotName"] = "A"
        }
    };

    var login = await plugin.LoginAsync(options, new MesCheckRequest { UserNo = "operator01", Password = "pwd" });
    var check = await plugin.CheckBeforeTestAsync(options, new MesCheckRequest { Barcode = "1234567L9ABC", UserNo = "operator01" });
    var upload = await plugin.UploadResultAsync(options, new MesUploadRequest
    {
        Barcode = "1234567L9ABC",
        Result = "PASS",
        UserNo = "operator01",
        StartTime = DateTime.Now.AddSeconds(-2),
        EndTime = DateTime.Now,
        TestItems = new[]
        {
            new MesTestItem { Name = "LED1-nm", Value = "600", Unit = "mcd", Result = "OK", LowerLimit = "550", UpperLimit = "630" }
        }
    });

    Smoke.Assert(login.IsSuccess, "MES login");
    Smoke.Assert(check.IsSuccess, "MES pre-check");
    Smoke.Assert(upload.IsSuccess, "MES upload");
    Smoke.Assert(server.Requests.Count == 3, "MES request count");
    Smoke.Assert(server.Requests.Any(request => request.RequestLine.Contains("prevalidation", StringComparison.OrdinalIgnoreCase)
        && request.RequestLine.Contains("barcode=1234567L9ABC", StringComparison.Ordinal)), "pre-check query barcode");
    if (!server.Requests.Any(request => request.Body.Contains("\"barcode\":\"1234567L9ABC\"", StringComparison.Ordinal)))
    {
        Console.Error.WriteLine("Captured MES requests:");
        foreach (var request in server.Requests)
        {
            Console.Error.WriteLine(request.RequestLine);
            Console.Error.WriteLine(request.Body);
        }
    }

    Smoke.Assert(server.Requests.Any(request => request.Body.Contains("\"barcode\":\"1234567L9ABC\"", StringComparison.Ordinal)), "upload JSON barcode");
    return true;
}

static AppConfiguration BuildConfiguration(string tempRoot)
{
    var configuration = new AppConfiguration
    {
        Identity =
        {
            CustomerName = "博敏",
            ProductName = "BominProduct",
            TestType = "FCT",
            TestProgram = "FCT-V1.0",
            MachineNo = "10.1.226.219",
            BarcodeLength = "12",
            BarcodePositionValidationEnabled = true,
            BarcodeValidationPosition = 8,
            BarcodeValidationAllowedValues = "L,B",
            BarcodeLimitRuleEnabled = true,
            BarcodeLimitItemIndex = 12,
            BarcodeLimitUpperByValue = "L=630;B=630",
            BarcodeLimitLowerByValue = "L=550;B=480"
        },
        Communication =
        {
            BarcodeSendDefaultOkCommand = false,
            BarcodeSuccessCommandByValue = "L=DMOK1\\r\\n;B=DMOK2\\r\\n"
        },
        Output =
        {
            CsvDirectory = Path.Combine(tempRoot, "ProData"),
            DclDirectory = Path.Combine(tempRoot, "ProOut"),
            SinglePieceCsvDirectory = Path.Combine(tempRoot, "ProYield"),
            CsvName = "F3409-0021-0021CS"
        },
        User =
        {
            FixtureNo = "FS00035535-0001",
            LoginJobNo = "operator01"
        },
        Mes =
        {
            Tooling = "FS00035535-0001"
        }
    };

    configuration.Export.EnabledPluginIds.Clear();
    configuration.Export.EnabledPluginIds.Add("Bomin.Export");
    configuration.Export.PluginOptions["Bomin.Export"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CsvPassDirectory"] = Path.Combine(tempRoot, "ProData"),
        ["CsvFailDirectory"] = Path.Combine(tempRoot, "ProYield"),
        ["DclDirectory"] = Path.Combine(tempRoot, "ProOut"),
        ["CsvMesDirectory"] = Path.Combine(tempRoot, "ProOut"),
        ["CsvName"] = "F3409-0021-0021CS",
        ["UseBarcodeSubfolders"] = "true",
        ["CsvLeafDirectory"] = "CSV",
        ["TesterId"] = "FS00035535-0001",
        ["Config"] = "111",
        ["Holder"] = "A",
        ["Slot"] = "1",
        ["TestType"] = "FCT",
        ["SwVersion"] = "FCT-V1.0"
    };

    return configuration;
}

static IReadOnlyList<TestItemConfiguration> BuildTestItems()
{
    return Enumerable.Range(1, 12)
        .Select(index => new TestItemConfiguration
        {
            Order = index,
            ItemName = index == 12 ? "LED1-nm" : $"Item{index}",
            LowerLimit = index == 12 ? "550" : "0",
            UpperLimit = index == 12 ? "630" : "10",
            Unit = index == 12 ? "mcd" : "ohm"
        })
        .ToList();
}

static string BuildPassPayload()
{
    return string.Join(';', Enumerable.Range(1, 12).Select(index =>
    {
        var value = index == 12 ? "600" : "5";
        return $"Test{index},1-1,{value},OK";
    })) + ";12;end";
}

static string BuildOutOfLimitOkPayload()
{
    return string.Join(';', Enumerable.Range(1, 12).Select(index =>
    {
        var value = index == 12 ? "999" : "99";
        return $"Test{index},1-1,{value},OK";
    })) + ";12;end";
}

internal static class Smoke
{
    public static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Assertion failed: " + message);
        }
    }
}

internal sealed class LocalHttpMesServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;

    public List<CapturedRequest> Requests { get; } = new();

    public int Port { get; private set; }

    public Task StartAsync()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptLoopAsync();
        return Task.CompletedTask;
    }

    public string Url(string path)
    {
        return $"http://127.0.0.1:{Port}/{path}";
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask;
            }
            catch
            {
                // The listener is intentionally stopped during disposal.
            }
        }

        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client), _cts.Token);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var clientScope = client;
        using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync() ?? string.Empty;
        var contentLength = 0;
        var isChunked = false;
        string? header;
        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
        {
            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(header["Content-Length:".Length..].Trim(), out var parsed))
            {
                contentLength = parsed;
            }
            else if (header.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase)
                && header.Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                isChunked = true;
            }
        }

        var body = string.Empty;
        if (isChunked)
        {
            body = await ReadChunkedBodyAsync(reader);
        }
        else if (contentLength > 0)
        {
            var buffer = new char[contentLength];
            var totalRead = 0;
            while (totalRead < contentLength)
            {
                var read = await reader.ReadBlockAsync(buffer, totalRead, contentLength - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            body = new string(buffer, 0, totalRead);
        }

        lock (Requests)
        {
            Requests.Add(new CapturedRequest(requestLine, body));
        }

        var json = "{\"isSuccess\":true,\"message\":\"OK\"}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await client.GetStream().WriteAsync(response);
        await client.GetStream().WriteAsync(bytes);
    }

    private static async Task<string> ReadChunkedBodyAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        while (true)
        {
            var sizeLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(sizeLine))
            {
                continue;
            }

            var semicolon = sizeLine.IndexOf(';');
            if (semicolon >= 0)
            {
                sizeLine = sizeLine[..semicolon];
            }

            if (!int.TryParse(sizeLine, System.Globalization.NumberStyles.HexNumber, null, out var size))
            {
                break;
            }

            if (size == 0)
            {
                await reader.ReadLineAsync();
                break;
            }

            var buffer = new char[size];
            var totalRead = 0;
            while (totalRead < size)
            {
                var read = await reader.ReadBlockAsync(buffer, totalRead, size - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            builder.Append(buffer, 0, totalRead);
            await reader.ReadLineAsync();
        }

        return builder.ToString();
    }
}

internal sealed record CapturedRequest(string RequestLine, string Body);
