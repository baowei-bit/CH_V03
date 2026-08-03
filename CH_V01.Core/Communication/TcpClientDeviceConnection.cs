using System.Net.Sockets;
using System.Text;
using CH_V01.Core.Configuration;

namespace CH_V01.Core.Communication;

internal sealed class TcpClientDeviceConnection : IDeviceConnection
{
    private readonly DeviceConnectionConfiguration _configuration;
    private readonly Encoding _encoding = Encoding.ASCII;
    private TcpClient? _client;
    private CancellationTokenSource? _readLoopCts;

    public TcpClientDeviceConnection(DeviceConnectionConfiguration configuration)
    {
        _configuration = configuration;
        Name = configuration.Name;
    }

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    public string Name { get; }

    public bool IsOpen => _client?.Connected == true;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen)
        {
            return;
        }

        _client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_configuration.TimeoutMilliseconds);
        await _client.ConnectAsync(_configuration.IpAddress, _configuration.Port, timeoutCts.Token);
        StartReadLoop(_client, timeoutCts.Token);
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _readLoopCts?.Cancel();
        _client?.Close();
        _client?.Dispose();
        _client = null;
        _readLoopCts?.Dispose();
        _readLoopCts = null;
        return Task.CompletedTask;
    }

    public async Task SendStringAsync(string data, CancellationToken cancellationToken = default)
    {
        if (_client is null || !_client.Connected)
        {
            return;
        }

        var bytes = _encoding.GetBytes(data);
        await _client.GetStream().WriteAsync(bytes, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        DataReceived = null;
    }

    private void StartReadLoop(TcpClient client, CancellationToken cancellationToken)
    {
        _readLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _readLoopCts.Token;
        _ = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            while (!token.IsCancellationRequested && client.Connected)
            {
                try
                {
                    var stream = client.GetStream();
                    var length = await stream.ReadAsync(buffer, token);
                    if (length <= 0)
                    {
                        break;
                    }

                    RaiseReceived(_encoding.GetString(buffer, 0, length));
                }
                catch
                {
                    break;
                }
            }
        }, token);
    }

    private void RaiseReceived(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(text));
        }
    }
}
