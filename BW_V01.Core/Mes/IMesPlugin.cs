namespace BW_V01.Core.Mes;

public interface IMesPlugin
{
    string Id { get; }

    string Name { get; }

    MesPluginCapabilities Capabilities { get; }

    IReadOnlyList<MesPluginConfigField> GetConfigFields();

    Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default);

    Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default);

    Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default);

    Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default);
}
