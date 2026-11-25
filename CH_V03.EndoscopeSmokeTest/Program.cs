using CH_V03.App.Communication;
using OpenCvSharp;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ZXing;
using ZXing.Common;

if (args.Length == 1 && args[0] == "--roi-regression")
{
    var folder = Path.Combine(Path.GetTempPath(), "CeHaoCameraRoiTest_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        static Mat MakeQr(string value)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new EncodingOptions { Width = 180, Height = 180, Margin = 2 }
            };
            var pixels = writer.Write(value);
            using var bgra = new Mat(pixels.Height, pixels.Width, MatType.CV_8UC4);
            Marshal.Copy(pixels.Pixels, 0, bgra.Data, pixels.Pixels.Length);
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }

        using var source = new Mat(260, 540, MatType.CV_8UC3, Scalar.White);
        using (var left = MakeQr("ROI-LEFT"))
        using (var right = MakeQr("OUTSIDE-RIGHT"))
        using (var leftTarget = new Mat(source, new Rect(25, 40, 180, 180)))
        using (var rightTarget = new Mat(source, new Rect(335, 40, 180, 180)))
        {
            left.CopyTo(leftTarget);
            right.CopyTo(rightTarget);
        }
        var sourcePath = Path.Combine(folder, "two-codes.png");
        Cv2.ImWrite(sourcePath, source);
        await using (var roiScanner = new EndoscopeScannerService())
        {
            var settings = new CH_V03.Core.Configuration.DeviceConnectionConfiguration
            {
                CameraRoiX = 0.025, CameraRoiY = 0.08,
                CameraRoiWidth = 0.39, CameraRoiHeight = 0.84,
                CameraNoReadImageDirectory = folder
            };
            var persisted = CH_V03.Core.Configuration.ConfigurationStore.Clone(settings);
            if (persisted.CameraRoiWidth != settings.CameraRoiWidth || persisted.CameraNoReadImageDirectory != folder)
                throw new Exception("ROI or NoRead path was not retained by configuration serialization");
            roiScanner.Configure(persisted);
            var selected = await roiScanner.DecodeImageFileAsync(sourcePath);
            if (selected != "ROI-LEFT") throw new Exception($"ROI priority failed: {selected ?? "NoRead"}");
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EndoscopeScannerService).GetField("_latestFrame", flags)!.SetValue(roiScanner, source.Clone());
            typeof(EndoscopeScannerService).GetField("_isOpen", flags)!.SetValue(roiScanner, true);
            var productionResults = new List<string>();
            roiScanner.BarcodeDetected += (_, code) => productionResults.Add(code);
            await roiScanner.TriggerScanAsync();
            if (productionResults.Count != 1 || productionResults[0] != "ROI-LEFT")
                throw new Exception("Production trigger did not prefer saved ROI");
        }
        Console.WriteLine("PASS saved ROI is preferred by preview and production trigger");

        using var rawBlank = new Mat(240, 320, MatType.CV_8UC3, new Scalar(80, 90, 100));
        await using (var noReadScanner = new EndoscopeScannerService())
        {
            noReadScanner.Configure(new CH_V03.Core.Configuration.DeviceConnectionConfiguration
            {
                CameraRoiX = .25, CameraRoiY = .25, CameraRoiWidth = .5, CameraRoiHeight = .5,
                CameraNoReadImageDirectory = folder, CameraScanTimeoutMilliseconds = 300
            });
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(EndoscopeScannerService).GetField("_latestFrame", flags)!.SetValue(noReadScanner, rawBlank.Clone());
            if (await noReadScanner.DecodeCurrentFrameAsync() is not null) throw new Exception("Blank camera frame decoded unexpectedly");
            using var saved = Cv2.ImRead(noReadScanner.LastNoReadFramePath);
            using var difference = new Mat();
            Cv2.Absdiff(saved, rawBlank, difference);
            var totalDifference = Cv2.Sum(difference);
            if (saved.Size() != rawBlank.Size() || totalDifference.Val0 + totalDifference.Val1 + totalDifference.Val2 != 0)
                throw new Exception("NoRead file is not the full unprocessed camera frame");
            Console.WriteLine("PASS manual NoRead saves the full raw frame");

            typeof(EndoscopeScannerService).GetField("_isOpen", flags)!.SetValue(noReadScanner, true);
            var scanEvents = new List<string>();
            noReadScanner.BarcodeDetected += (_, code) => scanEvents.Add(code);
            await noReadScanner.TriggerScanAsync();
            if (scanEvents.Count != 1 || scanEvents[0] != "NoRead" || !File.Exists(noReadScanner.LastNoReadFramePath))
                throw new Exception("Production NoRead did not save a frame and report NoRead");
            if (string.IsNullOrWhiteSpace(noReadScanner.LastQualityAdvice))
                throw new Exception("Production NoRead did not produce a quality suggestion");
            Console.WriteLine("PASS production NoRead saves a frame and reports NoRead");

            var blockedDirectory = Path.Combine(folder, "occupied-by-file");
            File.WriteAllText(blockedDirectory, "test");
            noReadScanner.Configuration.CameraNoReadImageDirectory = blockedDirectory;
            scanEvents.Clear();
            await noReadScanner.TriggerScanAsync();
            if (scanEvents.Count != 1 || scanEvents[0] != "NoRead"
                || string.IsNullOrWhiteSpace(noReadScanner.LastNoReadSaveError))
                throw new Exception("Save failure suppressed NoRead or lost the error");
            Console.WriteLine("PASS save failure still reports NoRead and exposes the error");
        }
    }
    finally
    {
        var resolved = Path.GetFullPath(folder);
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(resolved).StartsWith("CeHaoCameraRoiTest_", StringComparison.Ordinal)
            && Directory.Exists(resolved))
            Directory.Delete(resolved, recursive: true);
    }
    return;
}

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
