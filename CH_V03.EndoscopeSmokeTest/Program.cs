using CH_V03.App.Communication;
using OpenCvSharp;
using System.IO;
using System.Diagnostics;
using System.Reflection;

if (args.Length < 2) throw new ArgumentException("参数：预期码值 图片路径 [图片路径…]");
await using var scanner = new EndoscopeScannerService();
int events = 0;
scanner.BarcodeDetected += (_, _) => events++;
try { await scanner.DecodeCurrentFrameAsync(); throw new Exception("Missing frame not reported"); }
catch (InvalidOperationException ex) when (ex.Message.Contains("尚未取得")) { }
var latestFrame = typeof(EndoscopeScannerService).GetField("_latestFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
var gate = (SemaphoreSlim)typeof(EndoscopeScannerService).GetField("_decodeGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scanner)!;
foreach (var file in args.Skip(1))
{
    // Exercise the native enhancement branch with its production time budget,
    // not only the outer service (which may succeed through another decoder).
    using (var original = Cv2.ImRead(file))
    {
        if (original.Empty()) throw new Exception($"Unreadable fixture: {file}");
        var nativeTimer = Stopwatch.StartNew();
        var native = new LibDmtxDecoder().Decode(original);
        if (native != args[0]) throw new Exception($"Native regression: {file}: {native ?? "NoRead"}");
        Console.WriteLine($"PASS native default budget: {nativeTimer.ElapsedMilliseconds} ms");
    }
    var timer = Stopwatch.StartNew();
    var actual = await scanner.DecodeImageFileAsync(file);
    if (actual != args[0]) throw new Exception($"{file}: expected {args[0]}, actual {actual ?? "NoRead"}");
    Console.WriteLine($"PASS {Path.GetFileName(file)}: {actual} ({timer.ElapsedMilliseconds} ms)");

    // Block decoding, capture a snapshot, then replace the live image with a blank.
    // The manual call must still read the original snapshot once and emit no production event.
    using var source = Cv2.ImRead(file);
    (latestFrame.GetValue(scanner) as Mat)?.Dispose();
    latestFrame.SetValue(scanner, source.Clone());
    await gate.WaitAsync();
    Task<string?> snapshot;
    try
    {
        snapshot = scanner.DecodeCurrentFrameAsync();
        (latestFrame.GetValue(scanner) as Mat)?.Dispose();
        latestFrame.SetValue(scanner, new Mat(240, 320, MatType.CV_8UC3, Scalar.White));
    }
    finally { gate.Release(); }
    if (await snapshot != actual) throw new Exception("Manual snapshot changed while waiting for decoder");
    if (scanner.IsOpen) throw new Exception("Manual snapshot unexpectedly opened the camera");
    Console.WriteLine("PASS manual snapshot matches file, even after the live frame changes");
}
using var blank = new Mat(240, 320, MatType.CV_8UC3, Scalar.White);
var decoder = new LibDmtxDecoder();
if (decoder.Decode(blank, 300) is not null) throw new Exception("Blank image falsely decoded");
using var cts = new CancellationTokenSource();
cts.Cancel();
try { await scanner.DecodeImageFileAsync(args[1], cts.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { }
try { await scanner.DecodeCurrentFrameAsync(cts.Token); throw new Exception("Snapshot cancellation ignored"); }
catch (OperationCanceledException) { }
if (events != 0) throw new Exception("Offline image decoding emitted a production barcode event");
Console.WriteLine("PASS blank image, cancellation, and offline event isolation");

// A single readable camera frame must produce the same result as the preview.
// Do not advance _frameVersion: the former two-frame confirmation returned NoRead.
await using (var production = new EndoscopeScannerService())
{
    production.Configure(new CH_V03.Core.Configuration.DeviceConnectionConfiguration
    { CameraAutoFocus = false, CameraScanTimeoutMilliseconds = 500 });
    using var input = Cv2.ImRead(args[1]);
    latestFrame.SetValue(production, input.Clone());
    typeof(EndoscopeScannerService).GetField("_isOpen", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(production, true);
    var results = new List<string>();
    production.BarcodeDetected += (_, value) => results.Add(value);
    await production.TriggerScanAsync();
    if (results.Count != 1 || results[0] != args[0])
        throw new Exception("Production rejected a single readable frame: " + string.Join(",", results));
    Console.WriteLine("PASS production single frame matches preview and emits exactly one result");
}

// Reproduce an immediate close while the capture worker has not exited yet.
// No physical camera is needed: the barrier represents an in-flight native Read.
for (var iteration = 0; iteration < 30; iteration++)
{
    await using var lifecycle = new EndoscopeScannerService();
    var flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var workerDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var source = new CancellationTokenSource();
    using var capture = new VideoCapture();
    typeof(EndoscopeScannerService).GetField("_captureCts", flags)!.SetValue(lifecycle, source);
    typeof(EndoscopeScannerService).GetField("_captureTask", flags)!.SetValue(lifecycle, workerDone.Task);
    typeof(EndoscopeScannerService).GetField("_capture", flags)!.SetValue(lifecycle, capture);
    var close = lifecycle.CloseAsync();
    var secondClose = lifecycle.CloseAsync();
    if (!source.IsCancellationRequested || close.IsCompleted || secondClose.IsCompleted || capture.IsDisposed)
        throw new Exception("Close disposed capture before the worker stopped, or failed to serialize");
    workerDone.SetResult();
    await Task.WhenAll(close, secondClose).WaitAsync(TimeSpan.FromSeconds(3));
    if (!capture.IsDisposed) throw new Exception("Capture was not disposed after the worker stopped");
}
Console.WriteLine("PASS 30 concurrent close cycles: cancel, join, dispose ordering");
