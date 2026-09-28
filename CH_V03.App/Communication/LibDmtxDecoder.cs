using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;

namespace CH_V03.App.Communication;

/// <summary>Local ECC200 decoding. No Python runtime or sample-specific coordinates.</summary>
public sealed class LibDmtxDecoder
{
    public string? Decode(Mat frame, int budgetMilliseconds = 2500, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (frame.Empty()) return null;
        var clock = Stopwatch.StartNew();
        using var gray = new Mat();
        if (frame.Channels() == 1) frame.CopyTo(gray);
        else Cv2.CvtColor(frame, gray, frame.Channels() == 4 ? ColorConversionCodes.BGRA2GRAY : ColorConversionCodes.BGR2GRAY);
        foreach (var candidate in Candidates(gray))
        {
            using (candidate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = budgetMilliseconds - (int)clock.ElapsedMilliseconds;
                if (remaining <= 0) break;
                var result = Read(candidate, Math.Min(remaining, candidate.Width > 1000 ? 200 : 70));
                if (result is not null) return result;
            }
        }
        return null;
    }

    private static IEnumerable<Mat> Candidates(Mat gray)
    {
        yield return gray.Clone();
        // Camera frames can have very different module sizes from a displayed
        // screenshot. Try a small scale pyramid before expensive region searches.
        foreach (double scale in new[] { .5, 1.5, 2.0 })
        {
            if (Math.Max(gray.Width, gray.Height) * scale > 2200) continue;
            var resized = new Mat();
            Cv2.Resize(gray, resized, new Size(), scale, scale,
                scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Cubic);
            yield return resized;
        }
        using (var contrast = new Mat())
        using (var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8)))
        {
            clahe.Apply(gray, contrast);
            yield return contrast.Clone();
            using var reduced = new Mat();
            Cv2.Resize(contrast, reduced, new Size(), .5, .5, InterpolationFlags.Area);
            yield return reduced.Clone();
        }
        // Bright etched modules need local contrast detection: global Otsu often
        // merges the entire dark part and misses the small symbol inside it.
        var etchedClock = Stopwatch.StartNew();
        foreach (var bounds in FindEtchedRegions(gray, 45).Concat(FindEtchedRegions(gray, 65)).Distinct().OrderByDescending(r => r.Width * r.Height).Take(4))
        {
            if (etchedClock.ElapsedMilliseconds >= 1100) break;
            using var crop = new Mat(gray, bounds);
            foreach (var candidate in EnhanceEtched(crop))
            {
                if (etchedClock.ElapsedMilliseconds >= 1100) { candidate.Dispose(); break; }
                yield return candidate;
            }
        }
        // Connected dark modules suggest a code area; padded bounds retain the quiet zone.
        using var mask = new Mat();
        Cv2.Threshold(gray, mask, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 9));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var rectangles = contours.Select(Cv2.BoundingRect)
            .Where(r => r.Width > 60 && r.Height > 60 && r.Width < gray.Width * .6 && r.Height < gray.Height * .6 && r.Width / (double)r.Height is > .5 and < 2)
            .OrderByDescending(r => r.Width * r.Height).Take(4).ToArray();
        foreach (var r in rectangles)
        {
            var pad = Math.Max(15, Math.Min(r.Width, r.Height) / 8);
            var bounds = new Rect(Math.Max(0, r.X-pad), Math.Max(0, r.Y-pad), 1, 1);
            bounds.Width = Math.Min(gray.Width, r.Right+pad)-bounds.X;
            bounds.Height = Math.Min(gray.Height, r.Bottom+pad)-bounds.Y;
            using var crop = new Mat(gray, bounds);
            foreach (var candidate in Enhance(crop)) yield return candidate;
        }
        foreach (var candidate in Enhance(gray)) yield return candidate;
    }

    private static IEnumerable<Rect> FindEtchedRegions(Mat gray, int threshold)
    {
        using var local = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(21, 21));
        using var join = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 9));
        Cv2.MorphologyEx(gray, local, MorphTypes.TopHat, kernel);
        Cv2.Threshold(local, local, threshold, 255, ThresholdTypes.Binary);
        Cv2.MorphologyEx(local, local, MorphTypes.Close, join);
        Cv2.FindContours(local, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        return contours.Select(Cv2.BoundingRect)
            .Where(r => r.Width >= 45 && r.Height >= 45 && r.Width < gray.Width * .4 && r.Height < gray.Height * .4
                && r.Width / (double)r.Height is > .65 and < 1.5)
            .OrderByDescending(r => r.Width * r.Height / (1 + Math.Abs(Math.Log(r.Width / (double)r.Height))))
            .Take(4)
            .Select(r =>
            {
                int pad = Math.Max(12, Math.Min(r.Width, r.Height) / 6);
                int x = Math.Max(0, r.X - pad), y = Math.Max(0, r.Y - pad);
                return new Rect(x, y, Math.Min(gray.Width, r.Right + pad) - x, Math.Min(gray.Height, r.Bottom + pad) - y);
            }).ToArray();
    }

    private static IEnumerable<Mat> EnhanceEtched(Mat crop)
    {
        // Downsampling can remove interpolation noise; enlargement is not always better.
        foreach (double scale in new[] { .5, 1.0 })
        {
            using var normalized = new Mat();
            Cv2.Resize(crop, normalized, new Size(), scale, scale, InterpolationFlags.Lanczos4);
            Cv2.Normalize(normalized, normalized, 0, 255, NormTypes.MinMax);
            yield return normalized.Clone();
            foreach (int threshold in new[] { 170, 150, 190, 130, 110, 90 })
            {
                using var binary = new Mat();
                Cv2.Threshold(normalized, binary, threshold, 255, ThresholdTypes.Binary);
                yield return binary.Clone();
                foreach (int angle in new[] { 15, -15 })
                {
                    using var transform = Cv2.GetRotationMatrix2D(new Point2f(binary.Width / 2f, binary.Height / 2f), angle, 1);
                    int side = (int)Math.Ceiling(Math.Sqrt((double)binary.Width * binary.Width + (double)binary.Height * binary.Height));
                    transform.Set(0, 2, transform.At<double>(0, 2) + (side - binary.Width) / 2.0);
                    transform.Set(1, 2, transform.At<double>(1, 2) + (side - binary.Height) / 2.0);
                    var rotated = new Mat();
                    Cv2.WarpAffine(binary, rotated, transform, new Size(side, side), InterpolationFlags.Cubic, BorderTypes.Constant, Scalar.White);
                    yield return rotated;
                }
            }
        }
    }

    private static IEnumerable<Mat> Enhance(Mat gray)
    {
        yield return gray.Clone();
        foreach (var threshold in new[] {90, 110, 130})
        {
            var binary = new Mat();
            Cv2.Threshold(gray, binary, threshold, 255, ThresholdTypes.Binary);
            yield return binary;
        }
        foreach (var angle in new[] {30, -30, 90})
        {
            using var transform = Cv2.GetRotationMatrix2D(new Point2f(gray.Width/2f, gray.Height/2f), angle, 1);
            var side = (int)Math.Ceiling(Math.Sqrt((double)gray.Width*gray.Width + (double)gray.Height*gray.Height));
            transform.Set(0, 2, transform.At<double>(0, 2)+(side-gray.Width)/2.0);
            transform.Set(1, 2, transform.At<double>(1, 2)+(side-gray.Height)/2.0);
            using var rotated = new Mat();
            Cv2.WarpAffine(gray, rotated, transform, new Size(side, side), InterpolationFlags.Nearest, BorderTypes.Constant, new Scalar(200));
            Cv2.Normalize(rotated, rotated, 0, 255, NormTypes.MinMax);
            var binary = new Mat();
            Cv2.Threshold(rotated, binary, 110, 255, ThresholdTypes.Binary);
            yield return binary;
        }
    }

    private static string? Read(Mat gray, int timeout)
    {
        using var contiguous = gray.Clone();
        IntPtr image = IntPtr.Zero, decoder = IntPtr.Zero;
        try
        {
            image = dmtxImageCreate(contiguous.Data, gray.Width, gray.Height, 300);
            if (image == IntPtr.Zero) throw new InvalidOperationException("无法创建 Data Matrix 图像。");
            decoder = dmtxDecodeCreate(image, 1);
            if (decoder == IntPtr.Zero) throw new InvalidOperationException("无法创建 Data Matrix 解码器。");
            var until = dmtxTimeAdd(dmtxTimeNow(), timeout);
            while (true)
            {
                var region = dmtxRegionFindNext(decoder, ref until);
                if (region == IntPtr.Zero) return null;
                IntPtr message = IntPtr.Zero;
                try
                {
                    message = dmtxDecodeMatrixRegion(decoder, region, -1);
                    if (message == IntPtr.Zero) continue;
                    var value = Marshal.PtrToStructure<Message>(message);
                    if (value.Output == IntPtr.Zero || value.OutputIndex <= 0 || (nuint)value.OutputIndex > value.OutputSize) continue;
                    var bytes = new byte[value.OutputIndex];
                    Marshal.Copy(value.Output, bytes, 0, bytes.Length);
                    return Encoding.UTF8.GetString(bytes).Trim();
                }
                finally
                {
                    if (message != IntPtr.Zero) dmtxMessageDestroy(ref message);
                    dmtxRegionDestroy(ref region);
                }
            }
        }
        finally
        {
            if (decoder != IntPtr.Zero) dmtxDecodeDestroy(ref decoder);
            if (image != IntPtr.Zero) dmtxImageDestroy(ref image);
        }
    }

    // ABI of the bundled Windows libdmtx 0.7.4 (size_t = 64 bit; unsigned long = 32 bit).
    [StructLayout(LayoutKind.Sequential)] private struct Message
    {
        public nuint ArraySize, CodeSize, OutputSize;
        public int OutputIndex, PadCount;
        public IntPtr Array, Code, Output;
    }
    [StructLayout(LayoutKind.Sequential)] private struct DmtxTime { public ulong Seconds; public uint Microseconds; }
    private const string Library = "libdmtx-64.dll";
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr dmtxImageCreate(IntPtr pixels, int width, int height, int packing);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int dmtxImageDestroy(ref IntPtr image);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr dmtxDecodeCreate(IntPtr image, int scale);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int dmtxDecodeDestroy(ref IntPtr decoder);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern DmtxTime dmtxTimeNow();
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern DmtxTime dmtxTimeAdd(DmtxTime time, int milliseconds);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr dmtxRegionFindNext(IntPtr decoder, ref DmtxTime until);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern IntPtr dmtxDecodeMatrixRegion(IntPtr decoder, IntPtr region, int corrections);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int dmtxRegionDestroy(ref IntPtr region);
    [DllImport(Library, CallingConvention=CallingConvention.Cdecl)] private static extern int dmtxMessageDestroy(ref IntPtr message);
}
