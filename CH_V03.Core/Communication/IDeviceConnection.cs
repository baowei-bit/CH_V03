namespace CH_V03.Core.Communication;

public interface IDeviceConnection : IAsyncDisposable
{
    event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    string Name { get; }

    bool IsOpen { get; }

    Task OpenAsync(CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);

    Task SendStringAsync(string data, CancellationToken cancellationToken = default);
}

public interface IDeviceConnectionDiagnostics
{
    event EventHandler<DeviceCommunicationErrorEventArgs>? CommunicationError;
}

public sealed class DeviceDataReceivedEventArgs : EventArgs
{
    public DeviceDataReceivedEventArgs(string text)
    {
        Text = text;
    }

    public string Text { get; }
}

public sealed class DeviceCommunicationErrorEventArgs : EventArgs
{
    public DeviceCommunicationErrorEventArgs(string message)
    {
        Message = message;
    }

    public string Message { get; }
}
