using System.Net;
using System.Net.Sockets;
using System.Text;

var options = ServerOptions.Parse(args);
using var server = new LowerMachineTcpServer(options);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    server.RequestStop();
};

await server.RunAsync();

internal sealed class LowerMachineTcpServer : IDisposable
{
    private static readonly Encoding WireEncoding = Encoding.ASCII;
    private readonly ServerOptions _options;
    private readonly object _clientLock = new();
    private TcpClient? _activeClient;
    private int _activeClientId;
    private CancellationTokenSource _cts = new();

    public LowerMachineTcpServer(ServerOptions options)
    {
        _options = options;
    }

    public async Task RunAsync()
    {
        var listener = new TcpListener(_options.Address, _options.Port);
        listener.Start();

        Console.WriteLine("BW_V01 lower-machine TCP test server");
        Console.WriteLine($"Listening: {_options.Address}:{_options.Port}");
        Console.WriteLine("Set BW_V01 lower-machine connection type to TcpClient and connect to this endpoint.");
        Console.WriteLine("Type /help to list commands. Press Ctrl+C to exit.");
        Console.WriteLine();

        var acceptTask = AcceptLoopAsync(listener, _cts.Token);

        while (!_cts.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line is null)
            {
                RequestStop();
                break;
            }

            await HandleCommandAsync(line);
        }

        listener.Stop();
        await acceptTask;
        CloseActiveClient();
    }

    public void RequestStop()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        RequestStop();
        CloseActiveClient();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                var remoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                var clientId = ReplaceActiveClient(client);
                Console.WriteLine($"[{Now()}] Client #{clientId} connected: {remoteEndPoint}");
                Console.WriteLine($"[{Now()}] Type /items, /pass, /fail, /demo or /send <payload> to send data.");
                _ = ReceiveLoopAsync(client, clientId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Now()}] Accept failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task ReceiveLoopAsync(TcpClient client, int clientId, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var length = await client.GetStream().ReadAsync(buffer, cancellationToken);
                if (length <= 0)
                {
                    break;
                }

                var text = WireEncoding.GetString(buffer, 0, length);
                Console.WriteLine($"[{Now()}] Received from BW_V01 #{clientId}: {EscapeForConsole(text)}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{Now()}] Read failed: {ex.GetType().Name}: {ex.Message}");
                break;
            }
        }

        if (IsActiveClient(clientId, client))
        {
            CloseActiveClient(clientId);
        }
        else
        {
            client.Close();
            client.Dispose();
        }

        Console.WriteLine($"[{Now()}] Client #{clientId} disconnected");
    }

    private async Task HandleCommandAsync(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        if (string.Equals(trimmed, "/quit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "/exit", StringComparison.OrdinalIgnoreCase))
        {
            RequestStop();
            return;
        }

        if (string.Equals(trimmed, "/help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return;
        }

        if (string.Equals(trimmed, "/demo", StringComparison.OrdinalIgnoreCase))
        {
            await RunDemoAsync();
            return;
        }

        var payload = ResolvePayload(trimmed);
        if (payload is null)
        {
            Console.WriteLine("Unknown command. Type /help to list commands.");
            return;
        }

        await SendAsync(payload);
    }

    private static string? ResolvePayload(string command)
    {
        return command.ToLowerInvariant() switch
        {
            "/start" => "DM\r\n",
            "/items" => "Item1,J1_1-SHORT,10,0,ohm,1,1;Item2,R1,11.000,9.000,Kohm,1,1;Item3,U1-ID,0x38,0x38,HEX,1,1;3;end",
            "/pass" => "Test1,1-1,5.000,OK;Test2,1-1,9.983,OK;Test3,1-1,0x38,OK;3;end",
            "/fail" => "Test1,1-1,5.000,OK;Test2,1-1,12.600,NG;Test3,1-1,0x38,OK;3;end",
            _ when command.StartsWith("/send ", StringComparison.OrdinalIgnoreCase) => Unescape(command[6..]),
            _ => null
        };
    }

    private async Task SendAsync(string payload)
    {
        var active = GetActiveClient();
        if (active.Client is null)
        {
            Console.WriteLine("No BW_V01 client is connected.");
            return;
        }

        try
        {
            var bytes = WireEncoding.GetBytes(payload);
            await active.Client.GetStream().WriteAsync(bytes, _cts.Token);
            Console.WriteLine($"[{Now()}] Sent to client #{active.ClientId}: {EscapeForConsole(payload)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Now()}] Send failed: {ex.GetType().Name}: {ex.Message}");
            CloseActiveClient(active.ClientId);
        }
    }

    private async Task RunDemoAsync()
    {
        Console.WriteLine($"[{Now()}] Demo: send start signal.");
        await SendAsync("DM\r\n");
        await Task.Delay(500, _cts.Token);

        Console.WriteLine($"[{Now()}] Demo: send item list.");
        await SendAsync("Item1,J1_1-SHORT,10,0,ohm,1,1;Item2,R1,11.000,9.000,Kohm,1,1;Item3,U1-ID,0x38,0x38,HEX,1,1;3;end");
        await Task.Delay(500, _cts.Token);

        Console.WriteLine($"[{Now()}] Demo: send PASS result.");
        await SendAsync("Test1,1-1,5.000,OK;Test2,1-1,9.983,OK;Test3,1-1,0x38,OK;3;end");
    }

    private int ReplaceActiveClient(TcpClient client)
    {
        lock (_clientLock)
        {
            _activeClient?.Close();
            _activeClient?.Dispose();
            _activeClient = client;
            _activeClientId++;
            return _activeClientId;
        }
    }

    private (TcpClient? Client, int ClientId) GetActiveClient()
    {
        lock (_clientLock)
        {
            return (_activeClient, _activeClientId);
        }
    }

    private bool IsActiveClient(int clientId, TcpClient client)
    {
        lock (_clientLock)
        {
            return clientId == _activeClientId && ReferenceEquals(_activeClient, client);
        }
    }

    private void CloseActiveClient(int? clientId = null)
    {
        lock (_clientLock)
        {
            if (clientId is not null && clientId.Value != _activeClientId)
            {
                return;
            }

            _activeClient?.Close();
            _activeClient?.Dispose();
            _activeClient = null;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Commands:");
        Console.WriteLine("  /start       Send start signal: DM\\r\\n");
        Console.WriteLine("  /items       Send item list. BW_V01 should load 3 test items.");
        Console.WriteLine("  /pass        Send PASS result.");
        Console.WriteLine("  /fail        Send FAIL result.");
        Console.WriteLine("  /demo        Send /start, /items and /pass with short delays.");
        Console.WriteLine("  /send <text> Send custom payload. Supports \\r and \\n escape text.");
        Console.WriteLine("  /quit        Exit.");
        Console.WriteLine();
    }

    private static string Unescape(string text)
    {
        return text.Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal);
    }

    private static string EscapeForConsole(string text)
    {
        return text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private static string Now()
    {
        return DateTime.Now.ToString("HH:mm:ss");
    }
}

internal sealed record ServerOptions(IPAddress Address, int Port)
{
    public static ServerOptions Parse(string[] args)
    {
        var address = IPAddress.Loopback;
        var port = 15000;

        if (args.Length >= 1 && int.TryParse(args[0], out var parsedPort))
        {
            port = parsedPort;
        }

        if (args.Length >= 2 && IPAddress.TryParse(args[1], out var parsedAddress))
        {
            address = parsedAddress;
        }

        return new ServerOptions(address, port);
    }
}
