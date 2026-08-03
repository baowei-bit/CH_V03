namespace CH_V01.Core.Communication;

public interface IDeviceConnection : IAsyncDisposable
{
    event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    string Name { get; }

    bool IsOpen { get; }

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    Task SendStringAsync(string data, CancellationToken cancellationToken = default);
}

public sealed class DeviceDataReceivedEventArgs : EventArgs
{
    public DeviceDataReceivedEventArgs(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
