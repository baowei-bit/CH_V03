using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CH_V03.Core.Configuration;
using OpenCvSharp;
using ZXing;
using ZXing.Common;

namespace CH_V03.App.Communication;

/// <summary>
/// 共享内窥镜画面，并在收到 CeHao 自动扫码触发后从最近画面中识别二维码。
/// Supereyes 设备作为标准 UVC 摄像头使用，不依赖原厂桌面程序。
/// </summary>
public sealed class EndoscopeScannerService : IAsyncDisposable
{
    private const int MinimumFusionFrameCount = 3;
    private const int MaximumFusionFrameCount = 12;
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly LibDmtxDecoder _dataMatrixDecoder = new();
    private long _frameVersion;
    private readonly BarcodeReaderGeneric _barcodeReader = new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            // 内窥镜入口只接受产线使用的二维码和 Data Matrix，
            // 避免画面纹理被误判成一维条码。
            PossibleFormats = new[]
            {
                BarcodeFormat.QR_CODE,
                BarcodeFormat.DATA_MATRIX
            }
        }
    };
    private DeviceConnectionConfiguration _configuration = new();
    private VideoCapture? _capture;
    private Mat? _latestFrame;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private bool _isOpen;
    private string _status = "未打开";

    public event EventHandler<BitmapSource>? FrameReceived;
    public event EventHandler<string>? BarcodeDetected;
    public event EventHandler<string>? StatusChanged;

    public bool IsOpen => _isOpen;
    public string Status => _status;
    public DeviceConnectionConfiguration Configuration => _configuration;
    public string LastQualityAdvice { get; private set; } = string.Empty;
    public string LastNoReadFramePath { get; private set; } = string.Empty;
    public string LastNoReadSaveError { get; private set; } = string.Empty;

    public static string DefaultNoReadImageDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(root))
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(root, "CeHao", "NoReadFrames");
        }
    }

    public void Configure(DeviceConnectionConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try { await OpenCoreAsync(cancellationToken); }
        finally { _lifecycleGate.Release(); }
    }

    private async Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        if (IsOpen)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await CloseCoreAsync();

        var capture = new VideoCapture(_configuration.CameraIndex, VideoCaptureAPIs.DSHOW);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            throw new InvalidOperationException($"无法打开内窥镜摄像头（设备编号 {_configuration.CameraIndex}）。请确认设备已连接且未被 Supereyes 原厂程序占用。");
        }

        capture.Set(VideoCaptureProperties.FrameWidth, Math.Max(160, _configuration.CameraWidth));
        capture.Set(VideoCaptureProperties.FrameHeight, Math.Max(120, _configuration.CameraHeight));
        capture.Set(VideoCaptureProperties.Fps, Math.Max(1, _configuration.CameraFrameRate));
        if (_configuration.CameraAutoFocus)
        {
            // UVC/DirectShow drivers return false when autofocus is unavailable.
            // Treat that as a capability limitation, never as a camera-open failure.
            capture.Set(VideoCaptureProperties.AutoFocus, 1);
        }

        var captureCts = new CancellationTokenSource();
        var captureToken = captureCts.Token;
        lock (_syncRoot)
        {
            _capture = capture;
            _captureCts = captureCts;
            _isOpen = true;
        }

        // Capture immutable session locals, never fields that Close/Restart can replace.
        _captureTask = Task.Run(() => CaptureLoopAsync(capture, captureToken), CancellationToken.None);
        SetStatus($"已打开：摄像头 {_configuration.CameraIndex}，{(int)capture.FrameWidth} × {(int)capture.FrameHeight}");
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await CloseCoreAsync();
            await OpenCoreAsync(cancellationToken);
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task TriggerScanAsync(CancellationToken cancellationToken = default)
    {
        await _decodeGate.WaitAsync(cancellationToken);
        try { await Task.Run(() => TriggerScanCoreAsync(cancellationToken), cancellationToken); }
        finally { _decodeGate.Release(); }
    }

    /// <summary>Offline diagnostic: shows results without sending a production scanner event.</summary>
    public async Task<string?> DecodeImageFileAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var frame = await Task.Run(() => Cv2.ImDecode(System.IO.File.ReadAllBytes(path), ImreadModes.Color), cancellationToken);
        if (frame.Empty()) throw new InvalidOperationException("无法读取图片，请选择有效的 JPG、PNG 或 BMP 文件。");
        return await DecodeSingleImageAsync(frame, false, cancellationToken);
    }

    /// <summary>Snapshot the latest captured frame before waiting for the decoder. Diagnostic only.</summary>
    public async Task<string?> DecodeCurrentFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Freeze the frame the operator chose. Restarting focus here can replace
        // a sharp preview with an intermediate blurred frame on UVC cameras.
        using var frame = CopyLatestFrame();
        if (frame is null || frame.Empty())
            throw new InvalidOperationException("尚未取得相机画面，请先应用参数并等待预览出现后再抓图。");
        return await DecodeSingleImageAsync(frame, true, cancellationToken);
    }

    private async Task<string?> DecodeSingleImageAsync(Mat frame, bool saveOnNoRead, CancellationToken cancellationToken)
    {
        await _decodeGate.WaitAsync(cancellationToken);
        try
        {
            var result = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return TryDecode(frame, out var text, cancellationToken) ? text : null;
            }, cancellationToken);
            if (result is null && saveOnNoRead)
            {
                await RecordNoReadAsync(frame, cancellationToken);
            }
            return result;
        }
        finally { _decodeGate.Release(); }
    }

    private async Task TriggerScanCoreAsync(CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            await OpenAsync(cancellationToken);
        }

        // Use the current frame, just like the manual preview scan. Do not
        // restart focus or replace a readable frame before decoding it.

        var timeout = TimeSpan.FromMilliseconds(Math.Max(300, _configuration.CameraScanTimeoutMilliseconds));
        var stopAt = DateTime.UtcNow + timeout;
        var previousBarcode = string.Empty;
        var consecutiveMatchCount = 0;
        using var fusedFrame = new Mat();
        using var lastRawFrame = new Mat();
        var fusedFrameCount = 0;
        long lastVersion = -1;
        while (DateTime.UtcNow < stopAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Mat? snapshot;
            lock (_syncRoot)
            {
                snapshot = _frameVersion == lastVersion ? null : _latestFrame?.Clone();
                lastVersion = _frameVersion;
            }
            using var frame = snapshot;
            if (frame is null || frame.Empty())
            {
                await Task.Delay(80, cancellationToken);
                continue;
            }
            frame.CopyTo(lastRawFrame);

            if (fusedFrameCount == 0)
            {
                frame.CopyTo(fusedFrame);
                fusedFrameCount = 1;
            }
            else if (fusedFrameCount < MaximumFusionFrameCount && TryMergeDpmFrame(fusedFrame, frame))
            {
                fusedFrameCount++;
            }

            var decodedThisCycle = false;
            if (TryDecode(frame, out var frameText, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetStatus($"识别成功：{frameText}");
                BarcodeDetected?.Invoke(this, frameText);
                return;
            }

            // 至少使用三帧的不同高光分布进行融合，避免以单帧反光作为码点依据。
            if (!decodedThisCycle && fusedFrameCount >= MinimumFusionFrameCount
                && TryDecode(fusedFrame, out var fusedText, cancellationToken))
            {
                decodedThisCycle = true;
                if (TryConfirmBarcode(fusedText, ref previousBarcode, ref consecutiveMatchCount))
                {
                    SetStatus($"识别成功（{fusedFrameCount} 帧融合）：{fusedText}");
                    BarcodeDetected?.Invoke(this, fusedText);
                    return;
                }
            }

            if (!decodedThisCycle)
            {
                previousBarcode = string.Empty;
                consecutiveMatchCount = 0;
            }

            await Task.Delay(80, cancellationToken);
        }

        await RecordNoReadAsync(lastRawFrame, cancellationToken);
        SetStatus($"未识别到码。{LastQualityAdvice}{(LastNoReadSaveError.Length > 0 ? " 原始帧保存失败：" + LastNoReadSaveError : string.Empty)}");
        BarcodeDetected?.Invoke(this, "NoRead");
    }

    private async Task RecordNoReadAsync(Mat originalFrame, CancellationToken cancellationToken)
    {
        LastNoReadFramePath = string.Empty;
        LastNoReadSaveError = string.Empty;
        try
        {
            LastQualityAdvice = originalFrame.Empty() ? "请检查相机画面是否正常。" : BuildQualityAdvice(originalFrame);
        }
        catch (OpenCVException)
        {
            LastQualityAdvice = "请检查码是否完整入镜、画面清晰度和照明。";
        }
        if (originalFrame.Empty()) return;

        var directory = string.IsNullOrWhiteSpace(_configuration.CameraNoReadImageDirectory)
            ? DefaultNoReadImageDirectory
            : _configuration.CameraNoReadImageDirectory.Trim();
        try
        {
            var path = System.IO.Path.Combine(directory,
                $"NoRead_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.png");
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                System.IO.Directory.CreateDirectory(directory);
                if (!Cv2.ImWrite(path, originalFrame))
                    throw new System.IO.IOException("图像编码或写入失败。");
            }, cancellationToken);
            LastNoReadFramePath = path;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { LastNoReadSaveError = ex.Message; }
    }

    private string BuildQualityAdvice(Mat originalFrame)
    {
        using var area = CropConfiguredRoi(originalFrame) ?? originalFrame.Clone();
        using var gray = ToGray(area);
        if (gray.Empty()) return "请检查相机画面。";
        Cv2.MeanStdDev(gray, out var mean, out var deviation);
        if (mean.Val0 < 55) return "画面偏暗，请增加补光或检查曝光。";
        using var highlights = new Mat();
        Cv2.Threshold(gray, highlights, 245, 255, ThresholdTypes.Binary);
        if (Cv2.CountNonZero(highlights) > gray.Rows * gray.Cols * 0.1)
            return "码区有较多过亮区域，请降低补光、调整曝光或改变照射角度。";
        if (mean.Val0 > 205) return "画面整体偏亮，请减弱补光或检查曝光设置。";
        if (deviation.Val0 < 25) return "码区对比度偏低，请调整照明角度并确认码面完整。";
        using var edges = new Mat();
        Cv2.Laplacian(gray, edges, MatType.CV_64F);
        Cv2.MeanStdDev(edges, out _, out var edgeDeviation);
        if (edgeDeviation.Val0 < 20) return "码区细节偏少，请微调手动对焦并固定工件。";
        return "请检查码是否完整入镜、大小足够，并尝试调整角度以避开反光。";
    }

    private static bool TryConfirmBarcode(string barcode, ref string previousBarcode, ref int consecutiveMatchCount)
    {
        if (string.Equals(barcode, previousBarcode, StringComparison.Ordinal))
        {
            consecutiveMatchCount++;
        }
        else
        {
            previousBarcode = barcode;
            consecutiveMatchCount = 1;
        }

        return consecutiveMatchCount >= 2;
    }

    private static bool TryMergeDpmFrame(Mat fusedFrame, Mat currentFrame)
    {
        if (fusedFrame.Size() != currentFrame.Size())
        {
            return false;
        }

        using var referenceGray = ToGray(fusedFrame);
        using var currentGray = ToGray(currentFrame);
        if (referenceGray.Empty() || currentGray.Empty())
        {
            return false;
        }

        using var transform = new Mat(2, 3, MatType.CV_32FC1);
        Cv2.SetIdentity(transform);
        using var alignedFrame = new Mat();
        try
        {
            Cv2.FindTransformECC(
                referenceGray,
                currentGray,
                transform,
                MotionTypes.Euclidean,
                new TermCriteria(CriteriaTypes.Count | CriteriaTypes.Eps, 30, 1e-5));
            Cv2.WarpAffine(
                currentFrame,
                alignedFrame,
                transform,
                fusedFrame.Size(),
                InterpolationFlags.Linear | InterpolationFlags.WarpInverseMap,
                BorderTypes.Replicate);

            // 高光区域像素偏亮。逐像素保留较暗值，可融合多帧中未被反光覆盖的刻痕。
            Cv2.Min(fusedFrame, alignedFrame, fusedFrame);
            return true;
        }
        catch (OpenCVException)
        {
            // 工件移动过大或画面纹理不足时跳过本帧，不把错位图像混入融合结果。
            return false;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try { await CloseCoreAsync(); }
        finally { _lifecycleGate.Release(); }
    }

    private async Task CloseCoreAsync()
    {
        CancellationTokenSource? cts;
        VideoCapture? capture;
        Mat? frame;
        lock (_syncRoot)
        {
            cts = _captureCts;
            _captureCts = null;
            capture = _capture;
            _capture = null;
            frame = _latestFrame;
            _latestFrame = null;
            _isOpen = false;
        }

        cts?.Cancel();
        // Read may still be using native memory. Join the old worker before disposal
        // and before a new session is allowed to publish frames.
        if (_captureTask is not null) await _captureTask.ConfigureAwait(false);
        _captureTask = null;
        capture?.Release();
        capture?.Dispose();
        frame?.Dispose();
        cts?.Dispose();
        SetStatus("已关闭");
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }

    private async Task CaptureLoopAsync(VideoCapture capture, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var frame = new Mat();
                if (!capture.Read(frame) || frame.Empty())
                {
                    await Task.Delay(30, cancellationToken);
                    continue;
                }

                lock (_syncRoot)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();
                    _frameVersion++;
                }
                FrameReceived?.Invoke(this, ToBitmapSource(frame));
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止预览。
        }
        catch (Exception ex)
        {
            SetStatus($"内窥镜画面异常：{ex.Message}");
        }
    }

    private Mat? CopyLatestFrame()
    {
        lock (_syncRoot)
        {
            return _latestFrame?.Clone();
        }
    }

    private void TryEnableAutoFocus()
    {
        lock (_syncRoot)
        {
            _capture?.Set(VideoCaptureProperties.AutoFocus, 1);
        }
    }

    private bool TryDecode(Mat frame, out string text, CancellationToken cancellationToken = default)
    {
        text = string.Empty;
        if (frame.Empty())
        {
            return false;
        }

        // 固定工位先识别已保存的选区；选区失效或码移出选区时仍保留全图兜底。
        using var roi = CropConfiguredRoi(frame);
        if (roi is not null && TryDecodeCore(roi, out text, cancellationToken)) return true;
        return TryDecodeCore(frame, out text, cancellationToken);
    }

    private Mat? CropConfiguredRoi(Mat frame)
    {
        var config = _configuration;
        if (config.CameraRoiWidth <= 0 || config.CameraRoiHeight <= 0
            || config.CameraRoiX < 0 || config.CameraRoiY < 0
            || config.CameraRoiX + config.CameraRoiWidth > 1.000001
            || config.CameraRoiY + config.CameraRoiHeight > 1.000001) return null;

        var marginX = config.CameraRoiWidth * 0.08;
        var marginY = config.CameraRoiHeight * 0.08;
        var left = Math.Clamp((int)Math.Floor((config.CameraRoiX - marginX) * frame.Width), 0, frame.Width - 1);
        var top = Math.Clamp((int)Math.Floor((config.CameraRoiY - marginY) * frame.Height), 0, frame.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling((config.CameraRoiX + config.CameraRoiWidth + marginX) * frame.Width), left + 1, frame.Width);
        var bottom = Math.Clamp((int)Math.Ceiling((config.CameraRoiY + config.CameraRoiHeight + marginY) * frame.Height), top + 1, frame.Height);
        if (right - left < 40 || bottom - top < 40) return null;
        using var view = new Mat(frame, new Rect(left, top, right - left, bottom - top));
        return view.Clone();
    }

    private bool TryDecodeCore(Mat frame, out string text, CancellationToken cancellationToken)
    {
        text = string.Empty;

        // 保留原始画面优先识别，正常清晰二维码不会额外增加处理时间。
        if (TryDecodeCandidate(frame, out text))
        {
            return true;
        }

        var dataMatrixText = _dataMatrixDecoder.Decode(frame, cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(dataMatrixText))
        {
            text = dataMatrixText;
            return true;
        }

        // 内窥镜画面常见轻微失焦、反光或亮度不均。原图失败后再使用增强图重试。
        var enhancedCandidates = CreateEnhancedCandidates(frame);
        try
        {
            foreach (var candidate in enhancedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryDecodeCandidate(candidate, out text))
                {
                    return true;
                }
            }
        }
        finally { foreach (var candidate in enhancedCandidates) candidate.Dispose(); }

        // 镭刻 DPM 码没有印刷码的纯黑白边界。DPM 路径会先消除金属背景、
        // 提取刻痕条纹并将同一单元内的纹理连接为完整模块，再仅重试 Data Matrix 解码。
        if (TryDecodeDpmDataMatrix(frame, out text))
        {
            return true;
        }

        // 贴纸或工件斜拍时，先检测近矩形码区并透视拉正，再按 Data Matrix 重试。
        if (TryDecodePerspectiveCorrectedDataMatrix(frame, out text))
        {
            return true;
        }

        return false;
    }

    private bool TryDecodeCandidate(Mat image, out string text)
    {
        var zxingSucceeded = TryDecodeWithZxing(image, out var zxingText);
        var openCvSucceeded = TryDecodeWithOpenCv(image, out var openCvText);

        // 两个识别器都给出内容但不一致时，判为低置信度结果，继续等待下一帧。
        if (zxingSucceeded
            && openCvSucceeded
            && !string.Equals(zxingText, openCvText, StringComparison.Ordinal))
        {
            text = string.Empty;
            return false;
        }

        text = openCvSucceeded ? openCvText : zxingText;
        return zxingSucceeded || openCvSucceeded;
    }

    private bool TryDecodeWithZxing(Mat image, out string text)
    {
        text = string.Empty;
        var channels = image.Channels();
        if (channels is not (1 or 3))
        {
            return false;
        }

        var bytes = new byte[checked(image.Rows * image.Cols * channels)];
        Marshal.Copy(image.Data, bytes, 0, bytes.Length);
        var format = channels == 1
            ? RGBLuminanceSource.BitmapFormat.Gray8
            : RGBLuminanceSource.BitmapFormat.BGR24;
        var source = new RGBLuminanceSource(bytes, image.Cols, image.Rows, format);
        var result = _barcodeReader.Decode(source);
        text = result?.Text?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool TryDecodeWithOpenCv(Mat image, out string text)
    {
        text = string.Empty;
        try
        {
            using var detector = new QRCodeDetector();
            using var straightQrCode = new Mat();
            text = detector.DetectAndDecode(image, out _, straightQrCode)?.Trim() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(text);
        }
        catch (OpenCVException)
        {
            // 个别摄像头格式或 OpenCV 版本不支持时，继续由 ZXing 完成识别。
            return false;
        }
    }

    private static IReadOnlyList<Mat> CreateEnhancedCandidates(Mat frame)
    {
        var gray = new Mat();
        switch (frame.Channels())
        {
            case 1:
                frame.CopyTo(gray);
                break;
            case 3:
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGRA2GRAY);
                break;
            default:
                gray.Dispose();
                return Array.Empty<Mat>();
        }

        var contrast = new Mat();
        using (var clahe = Cv2.CreateCLAHE(2.5, new Size(8, 8)))
        {
            clahe.Apply(gray, contrast);
        }

        var blurred = new Mat();
        var sharpened = new Mat();
        Cv2.GaussianBlur(contrast, blurred, new Size(0, 0), 1.2);
        Cv2.AddWeighted(contrast, 1.8, blurred, -0.8, 0, sharpened);
        blurred.Dispose();

        var adaptiveBinary = new Mat();
        Cv2.AdaptiveThreshold(
            sharpened,
            adaptiveBinary,
            255,
            AdaptiveThresholdTypes.GaussianC,
            ThresholdTypes.Binary,
            31,
            5);

        var enlarged = new Mat();
        Cv2.Resize(adaptiveBinary, enlarged, new Size(), 2, 2, InterpolationFlags.Cubic);

        return new[] { gray, contrast, sharpened, adaptiveBinary, enlarged };
    }

    private static IReadOnlyList<Mat> CreateDpmCandidates(Mat frame)
    {
        var gray = new Mat();
        switch (frame.Channels())
        {
            case 1:
                frame.CopyTo(gray);
                break;
            case 3:
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGRA2GRAY);
                break;
            default:
                gray.Dispose();
                return Array.Empty<Mat>();
        }

        using var denoised = new Mat();
        Cv2.BilateralFilter(gray, denoised, 7, 50, 50);

        using var backgroundKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(31, 31));
        using var blackHat = new Mat();
        Cv2.MorphologyEx(denoised, blackHat, MorphTypes.BlackHat, backgroundKernel);

        var engravingContrast = new Mat();
        Cv2.Normalize(blackHat, engravingContrast, 0, 255, NormTypes.MinMax);

        var engravingBinary = new Mat();
        Cv2.Threshold(engravingContrast, engravingBinary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        var engravingBinaryInverted = new Mat();
        Cv2.BitwiseNot(engravingBinary, engravingBinaryInverted);

        // 镭刻单元通常由多条平行刻痕组成。使用垂直闭运算填补水平刻痕间隙，
        // 把单元纹理还原为二维码解码器可采样的连续模块。
        using var hatchFillKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, 7));
        var filledEngraving = new Mat();
        Cv2.MorphologyEx(engravingBinary, filledEngraving, MorphTypes.Close, hatchFillKernel);

        var filledEngravingInverted = new Mat();
        Cv2.BitwiseNot(filledEngraving, filledEngravingInverted);

        var enlarged = new Mat();
        Cv2.Resize(filledEngravingInverted, enlarged, new Size(), 2, 2, InterpolationFlags.Nearest);

        gray.Dispose();
        return new[]
        {
            engravingContrast,
            engravingBinary,
            engravingBinaryInverted,
            filledEngraving,
            filledEngravingInverted,
            enlarged
        };
    }

    private bool TryDecodeDpmDataMatrix(Mat frame, out string text)
    {
        var candidates = CreateDpmCandidates(frame);
        try
        {
            foreach (var candidate in candidates)
            {
                if (TryDecodeDataMatrixWithZxing(candidate, out text))
                {
                    return true;
                }
            }
        }
        finally { foreach (var candidate in candidates) candidate.Dispose(); }

        text = string.Empty;
        return false;
    }

    private static bool TryDecodeDataMatrixWithZxing(Mat image, out string text)
    {
        text = string.Empty;
        var channels = image.Channels();
        if (channels is not (1 or 3))
        {
            return false;
        }

        var bytes = new byte[checked(image.Rows * image.Cols * channels)];
        Marshal.Copy(image.Data, bytes, 0, bytes.Length);
        var format = channels == 1
            ? RGBLuminanceSource.BitmapFormat.Gray8
            : RGBLuminanceSource.BitmapFormat.BGR24;
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new[] { BarcodeFormat.DATA_MATRIX }
            }
        };
        var source = new RGBLuminanceSource(bytes, image.Cols, image.Rows, format);
        var result = reader.Decode(source);
        text = result?.Text?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(text);
    }

    private bool TryDecodePerspectiveCorrectedDataMatrix(Mat frame, out string text)
    {
        text = string.Empty;
        using var gray = ToGray(frame);
        if (gray.Empty())
        {
            return false;
        }

        using var blurred = new Mat();
        using var edges = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);
        Cv2.Canny(blurred, edges, 40, 120);
        Cv2.FindContours(edges, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        var minimumArea = frame.Width * frame.Height * 0.015;
        var candidates = contours
            .Select(contour => CreateQuadrilateralCandidate(contour, minimumArea))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .OrderByDescending(candidate => candidate.Score)
            .Take(6);

        foreach (var candidate in candidates)
        {
            using var corrected = WarpPerspective(frame, candidate.Points);
            if (corrected.Empty())
            {
                continue;
            }

            if (TryDecodeDataMatrixWithZxing(corrected, out text)
                || TryDecodeDpmDataMatrix(corrected, out text))
            {
                return true;
            }
        }

        text = string.Empty;
        return false;
    }

    private static QuadrilateralCandidate? CreateQuadrilateralCandidate(Point[] contour, double minimumArea)
    {
        var perimeter = Cv2.ArcLength(contour, true);
        if (perimeter <= 0)
        {
            return null;
        }

        var approximation = Cv2.ApproxPolyDP(contour, perimeter * 0.02, true);
        if (approximation.Length != 4 || !Cv2.IsContourConvex(approximation))
        {
            return null;
        }

        var area = Math.Abs(Cv2.ContourArea(approximation));
        if (area < minimumArea)
        {
            return null;
        }

        var bounds = Cv2.BoundingRect(approximation);
        if (bounds.Width < 80 || bounds.Height < 80)
        {
            return null;
        }

        var aspectRatio = bounds.Width / (double)bounds.Height;
        if (aspectRatio is < 0.45 or > 2.2)
        {
            return null;
        }

        // 接近正方形的区域更可能是 Data Matrix 码区；仍允许矩形码。
        var score = area / (1 + Math.Abs(Math.Log(aspectRatio)));
        return new QuadrilateralCandidate(OrderCornerPoints(approximation), score);
    }

    private static Mat WarpPerspective(Mat frame, Point[] sourcePoints)
    {
        var topWidth = Distance(sourcePoints[0], sourcePoints[1]);
        var bottomWidth = Distance(sourcePoints[3], sourcePoints[2]);
        var leftHeight = Distance(sourcePoints[0], sourcePoints[3]);
        var rightHeight = Distance(sourcePoints[1], sourcePoints[2]);
        var width = Math.Clamp((int)Math.Round(Math.Max(topWidth, bottomWidth)), 160, 1600);
        var height = Math.Clamp((int)Math.Round(Math.Max(leftHeight, rightHeight)), 160, 1600);

        var destinationPoints = new[]
        {
            new Point2f(0, 0),
            new Point2f(width - 1, 0),
            new Point2f(width - 1, height - 1),
            new Point2f(0, height - 1)
        };
        var source = sourcePoints.Select(point => new Point2f(point.X, point.Y)).ToArray();
        using var transform = Cv2.GetPerspectiveTransform(source, destinationPoints);
        var corrected = new Mat();
        Cv2.WarpPerspective(frame, corrected, transform, new Size(width, height), InterpolationFlags.Cubic);
        return corrected;
    }

    private static Point[] OrderCornerPoints(Point[] points)
    {
        var topLeft = points.MinBy(point => point.X + point.Y);
        var bottomRight = points.MaxBy(point => point.X + point.Y);
        var topRight = points.MaxBy(point => point.X - point.Y);
        var bottomLeft = points.MinBy(point => point.X - point.Y);
        return new[] { topLeft, topRight, bottomRight, bottomLeft };
    }

    private static double Distance(Point first, Point second)
    {
        var horizontal = first.X - second.X;
        var vertical = first.Y - second.Y;
        return Math.Sqrt(horizontal * horizontal + vertical * vertical);
    }

    private static Mat ToGray(Mat frame)
    {
        var gray = new Mat();
        switch (frame.Channels())
        {
            case 1:
                frame.CopyTo(gray);
                break;
            case 3:
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
                break;
            case 4:
                Cv2.CvtColor(frame, gray, ColorConversionCodes.BGRA2GRAY);
                break;
            default:
                gray.Dispose();
                return new Mat();
        }

        return gray;
    }

    private sealed record QuadrilateralCandidate(Point[] Points, double Score);

    private static BitmapSource ToBitmapSource(Mat frame)
    {
        var stride = checked(frame.Cols * frame.Channels());
        var bytes = new byte[checked(stride * frame.Rows)];
        Marshal.Copy(frame.Data, bytes, 0, bytes.Length);
        var image = BitmapSource.Create(frame.Cols, frame.Rows, 96, 96, PixelFormats.Bgr24, null, bytes, stride);
        image.Freeze();
        return image;
    }

    private void SetStatus(string value)
    {
        _status = value;
        StatusChanged?.Invoke(this, value);
    }
}
