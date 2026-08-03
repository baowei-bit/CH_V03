namespace CH_V01.Core.Communication;

internal sealed class DisabledDeviceConnection : IDeviceConnection
{
    public DisabledDeviceConnection(string name)
    {
        Name = name;
    }

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived
    {
        add { }
        remove { }
    }

    public string Name { get; }

    public bool IsOpen => false;

    public Task OpenAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendStringAsync(string data, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
