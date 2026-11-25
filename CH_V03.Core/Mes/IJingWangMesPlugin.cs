namespace CH_V03.Core.Mes;

public interface IJingWangMesPlugin : IMesPlugin
{
    string CurrentEmployeeNo { get; }
    Task<MesResult> ChangeToolAsync(MesPluginOptions options, string barcode, int? usedLife, CancellationToken cancellationToken = default);
    Task<MesResult> EndShiftAsync(MesPluginOptions options, CancellationToken cancellationToken = default);
    Task<MesResult> RecordScanAsync(MesPluginOptions options, string barcode, string employeeNo, CancellationToken cancellationToken = default);
    Task<MesResult> RecordAlarmAsync(MesPluginOptions options, string code, string description, bool active, CancellationToken cancellationToken = default);
    Task<MesResult> CollectCraftAsync(MesPluginOptions options, IReadOnlyList<MesTestItem> items, DateTime collectedAt, CancellationToken cancellationToken = default);
    IReadOnlyList<JingWangPendingRecord> GetPendingRecords(MesPluginOptions options);
    Task<MesResult> RetryPendingDatabaseAsync(MesPluginOptions options, string recordId, CancellationToken cancellationToken = default);
    Task<MesResult> RetryPendingApisAsync(MesPluginOptions options, string recordId, CancellationToken cancellationToken = default);
    MesResult ArchivePendingRecord(MesPluginOptions options, string recordId);
}

public sealed record JingWangPendingRecord(string Id, string Barcode, string Status, DateTime CreatedAt, string Message,
    string Result = "", string CsvPath = "", string WorkOrder = "", string Lot = "");
