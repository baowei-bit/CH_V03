using System.Net;
using System.Net.Sockets;
using System.Text;
using CH_V03.Core.Configuration;

namespace CH_V03.Core.Communication;

internal sealed class TcpServerDeviceConnection : IDeviceConnection
{
    private readonly DeviceConnectionConfiguration _configuration;
    private readonly Encoding _encoding = Encoding.ASCII;
    private TcpListener? _listener;
    private TcpClient? _activeClient;
    private CancellationTokenSource? _cts;

    public TcpServerDeviceConnection(DeviceConnectionConfiguration configuration)
    {
        _configuration = configuration;
        Name = configuration.Name;
    }

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    public string Name { get; }

    public bool IsOpen => _listener is not null;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen)
        {
            return Task.CompletedTask;
        }

        var address = IPAddress.Parse(_configuration.IpAddress);
        _listener = new TcpListener(address, _configuration.Port);
        _listener.Start();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        StartAcceptLoop(_cts.Token);
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        _activeClient?.Close();
        _activeClient?.Dispose();
        _listener?.Stop();
        _activeClient = null;
        _listener = null;
        _cts?.Dispose();
        _cts = null;
        return Task.CompletedTask;
    }

    public async Task SendStringAsync(string data, CancellationToken cancellationToken = default)
    {
        if (_activeClient is null || !_activeClient.Connected)
        {
            return;
        }

        var bytes = _encoding.GetBytes(data);
        await _activeClient.GetStream().WriteAsync(bytes, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        DataReceived = null;
    }

    private void StartAcceptLoop(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested && _listener is not null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                    _activeClient?.Dispose();
                    _activeClient = client;
                    StartClientReadLoop(client, cancellationToken);
                }
                catch
                {
                    break;
                }
            }
        }, cancellationToken);
    }

    private void StartClientReadLoop(TcpClient client, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                try
                {
                    var stream = client.GetStream();
                    var length = await stream.ReadAsync(buffer, cancellationToken);
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
        }, cancellationToken);
    }

    private void RaiseReceived(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(text));
        }
    }
}
