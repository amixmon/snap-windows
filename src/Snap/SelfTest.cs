using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snap.Capture;
using Snap.Interop;
using Bitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Snap;

/// <summary>Deterministic local checks. All image/video pixels are generated in memory.</summary>
internal static class SelfTest
{
    internal static int Run()
    {
        var directory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "artifacts", "self-test"));
        var report = new List<string>
        {
            "Snap self-test",
            $"Started: {DateTimeOffset.UtcNow:O}",
            "Image source: synthetic pixels only; no desktop image is captured."
        };
        var failures = 0;
        var passed = 0;
        try
        {
            Directory.CreateDirectory(directory);
            RunCase("Initial area is the centered quarter of a display", () =>
                Equal(new Rect(720, 405, 480, 270), SelectionGeometry.Initial(new CaptureRect(0, 0, 1920, 1080))));

            RunCase("Initial area preserves negative display coordinates", () =>
                Equal(new Rect(-1200, -675, 480, 270), SelectionGeometry.Initial(new CaptureRect(-1920, -1080, 1920, 1080))));

            RunCase("Clamp limits size, position, and minimum dimensions", () =>
            {
                var desktop = new Rect(0, 0, 1920, 1080);
                Equal(desktop, SelectionGeometry.Clamp(new Rect(-200, -100, 2500, 1500), desktop));
                Equal(new Rect(1919, 1079, 1, 1), SelectionGeometry.Clamp(new Rect(3000, 2000, 0, 0), desktop));
                Equal(new Rect(-1920, -1080, 500, 300), SelectionGeometry.Clamp(
                    new Rect(-2200, -1300, 500, 300), new Rect(-1920, -1080, 1920, 1080)));
            });

            RunCase("Reverse dragging creates the expected rectangle", () =>
                Equal(new Rect(200, 150, 400, 250), SelectionGeometry.Drag(new Rect(100, 100, 20, 20),
                    new Point(600, 400), new Point(200, 150), DragEdge.New, new Rect(0, 0, 1000, 800))));

            RunCase("Moving preserves size at desktop and negative-coordinate boundaries", () =>
            {
                Equal(new Rect(800, 680, 200, 120), SelectionGeometry.Drag(new Rect(100, 100, 200, 120),
                    new Point(150, 140), new Point(3000, 4000), DragEdge.Move, new Rect(0, 0, 1000, 800)));
                Equal(new Rect(-1920, -1080, 200, 100), SelectionGeometry.Drag(new Rect(-1800, -900, 200, 100),
                    new Point(-1700, -850), new Point(-5000, -5000), DragEdge.Move, new Rect(-1920, -1080, 1920, 1080)));
            });

            RunCase("Edge resize and hit testing handle corners and crossed edges", () =>
            {
                var original = new Rect(100, 100, 200, 100);
                var bounds = new Rect(0, 0, 1000, 800);
                Equal(new Rect(50, 40, 250, 160), SelectionGeometry.Drag(original,
                    new Point(100, 100), new Point(50, 40), DragEdge.Left | DragEdge.Top, bounds));
                Equal(new Rect(50, 100, 50, 100), SelectionGeometry.Drag(original,
                    new Point(300, 150), new Point(50, 150), DragEdge.Right, bounds));
                Require(SelectionGeometry.HitTest(original, new Point(101, 101), 4) == (DragEdge.Left | DragEdge.Top),
                    "The top-left corner did not expose both resize edges.");
                Require(SelectionGeometry.HitTest(original, new Point(200, 150), 4) == DragEdge.Move,
                    "The selection interior was not movable.");
                Require(SelectionGeometry.HitTest(original, new Point(500, 500), 4) == DragEdge.New,
                    "The exterior did not create a new selection.");
            });

            RunCase("Pixel crop rounds coordinates and stays within the physical desktop", () =>
            {
                var desktop = new CaptureRect(-1920, -1080, 3840, 2160);
                Require(SelectionGeometry.Pixels(new Rect(-1920.6, -1080.6, 5000, 4000), desktop) == desktop,
                    "An oversized crop did not clamp to the physical desktop.");
                Require(SelectionGeometry.Pixels(new Rect(-10.4, -5.6, 20.2, 30.2), desktop) == new CaptureRect(-10, -6, 20, 31),
                    "Fractional crop coordinates were not rounded consistently.");
                Require(SelectionGeometry.Pixels(new Rect(5000, 5000, 10, 10), desktop) == new CaptureRect(1919, 1079, 1, 1),
                    "An out-of-range crop did not keep the minimum one-pixel area.");
            });

            RunCase("Invalid capture dimensions fail before any screen API is called", () =>
            {
                // These are guard-path calls only. ValidateBounds throws before
                // bitmap allocation or access to a desktop device context.
                Throws<ArgumentOutOfRangeException>(() => { using var bitmap = DesktopCapture.CaptureBitmap(new CaptureRect(0, 0, 0, 10)); });
                Throws<ArgumentOutOfRangeException>(() => { using var bitmap = DesktopCapture.CaptureBitmap(new CaptureRect(0, 0, 10, -1)); });
                Throws<OverflowException>(() => { using var bitmap = DesktopCapture.CaptureBitmap(new CaptureRect(int.MaxValue, 0, 10, 10)); });
            });

            RunCase("Synthetic PNG saves and reloads with opaque red top and blue bottom", () =>
                CheckPng(Path.Combine(directory, "reference.png")));

            RunCase("Synthetic H.264 recording starts, stops, and finalizes an MP4", () =>
                CheckRecording(Path.Combine(directory, "sample.mp4"), report));

            RunCase("Windows decodes the MP4 at the correct size and orientation", () =>
                CheckDecodedRecording(Path.Combine(directory, "sample.mp4"), Path.Combine(directory, "decoded-frame.png"), report));

            report.Add($"Result: {passed} passed; {failures} failed.");
        }
        catch (Exception error)
        {
            failures++;
            report.Add("FAIL self-test infrastructure");
            report.Add(error.ToString());
        }

        try
        {
            File.WriteAllLines(Path.Combine(directory, "results.txt"), report, new UTF8Encoding(false));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        return failures == 0 ? 0 : 1;

        void RunCase(string name, Action test)
        {
            try
            {
                test();
                passed++;
                report.Add("PASS " + name);
            }
            catch (Exception error)
            {
                failures++;
                report.Add("FAIL " + name);
                report.Add(error.ToString());
            }
        }
    }

    private static void CheckPng(string path)
    {
        using var bitmap = CreateFrame(320, 180, 0);
        var pixels = bitmap.LockBits(new DrawingRectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        BitmapSource snapshot;
        try
        {
            snapshot = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32,
                null, pixels.Scan0, checked(pixels.Stride * bitmap.Height), pixels.Stride);
            snapshot.Freeze();
        }
        finally
        {
            bitmap.UnlockBits(pixels);
        }
        DesktopCapture.SavePng(snapshot, path);
        BitmapSource loaded;
        using (var stream = File.OpenRead(path))
        {
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            loaded = decoder.Frames[0];
            loaded.Freeze();
        }
        Require(loaded.PixelWidth == 320 && loaded.PixelHeight == 180, "PNG dimensions changed during saving.");
        var converted = new FormatConvertedBitmap(loaded, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(new Int32Rect(4, 4, 1, 1), pixel, 4, 0);
        Require(pixel[0] == 0 && pixel[1] == 0 && pixel[2] == 255 && pixel[3] == 255,
            "PNG top-left should be opaque red.");
        converted.CopyPixels(new Int32Rect(4, 175, 1, 1), pixel, 4, 0);
        Require(pixel[0] == 255 && pixel[1] == 0 && pixel[2] == 0 && pixel[3] == 255,
            "PNG bottom-left should be opaque blue.");
    }

    private static void CheckRecording(string path, List<string> report)
    {
        // This specific path is generated by the self-test and is replaced on reruns.
        if (File.Exists(path))
            File.Delete(path);
        var frames = 0;
        var includePointerWasForwarded = true;
        var dimensionsWereEven = true;
        using var recorder = new ScreenRecorder((bounds, includePointer) =>
        {
            includePointerWasForwarded &= includePointer;
            dimensionsWereEven &= bounds == new CaptureRect(-400, -200, 320, 180);
            return CreateFrame(bounds.Width, bounds.Height, Interlocked.Increment(ref frames));
        });

        Require(!recorder.IsRecording && recorder.Elapsed == TimeSpan.Zero, "Recorder should initially be idle.");
        Throws<ArgumentOutOfRangeException>(() => recorder.StartAsync(new CaptureRect(0, 0, 1, 180), false, path));
        Require(frames == 0, "Invalid dimensions should not request a frame.");

        recorder.StartAsync(new CaptureRect(-400, -200, 321, 181), true, path).GetAwaiter().GetResult();
        Require(recorder.IsRecording, "StartAsync returned before recording became active.");
        Thread.Sleep(500);
        recorder.StopAsync().GetAwaiter().GetResult();
        recorder.StopAsync().GetAwaiter().GetResult();
        Require(!recorder.IsRecording, "Recorder remained active after StopAsync.");
        Require(recorder.Elapsed > TimeSpan.Zero, "Elapsed recording time was not recorded.");
        Require(frames > 1, "The recorder did not produce multiple synthetic frames.");
        Require(includePointerWasForwarded, "The pointer option was not forwarded to the frame provider.");
        Require(dimensionsWereEven, "Odd dimensions were not cropped to an even H.264 frame.");
        var elapsed = recorder.Elapsed;
        Thread.Sleep(30);
        Require(recorder.Elapsed == elapsed, "Elapsed time continued advancing after recording stopped.");
        Throws<IOException>(() => recorder.StartAsync(new CaptureRect(0, 0, 320, 180), false, path));

        var boxes = ReadMp4Boxes(path);
        Require(boxes.Contains("ftyp"), "The output does not contain an MP4 file-type box.");
        Require(boxes.Contains("moov"), "The output lacks a finalized MP4 movie box.");
        Require(boxes.Contains("mdat"), "The output does not contain encoded video samples.");
        report.Add($"  Recording: {frames} frames provided; elapsed {elapsed.TotalMilliseconds:F0} ms; {new FileInfo(path).Length} bytes.");
        report.Add("  MP4 top-level boxes: " + string.Join(", ", boxes));
        report.Add("  Playback reference: red TOP, blue BOTTOM, moving white square.");
    }

    private static void CheckDecodedRecording(string path, string framePath, List<string> report)
    {
        // The synchronous source reader uses Windows' H.264 decoder and software
        // RGB conversion. It needs neither a player window nor a dispatcher pump.
        const uint videoStream = 0xfffffffc;
        IntPtr reader = IntPtr.Zero;
        IntPtr attributes = IntPtr.Zero;
        IntPtr desiredType = IntPtr.Zero;
        IntPtr actualType = IntPtr.Zero;
        IntPtr sample = IntPtr.Zero;
        IntPtr buffer = IntPtr.Zero;
        bool comInitialized = false;
        bool mediaInitialized = false;
        try
        {
            MediaFoundation.Check(MediaFoundation.CoInitializeEx(IntPtr.Zero, 2), "Initialize self-test decoder COM");
            comInitialized = true;
            MediaFoundation.Check(MediaFoundation.MFStartup(MediaFoundation.Version, 0), "Initialize self-test media decoder");
            mediaInitialized = true;
            MediaFoundation.Check(MediaFoundation.MFCreateAttributes(out attributes, 1), "Create decoder attributes");
            MediaFoundation.SetUInt32(attributes, new Guid("fb394f3d-ccf1-42ee-bbb3-f9b845d5681d"), 1);
            MediaFoundation.Check(MFCreateSourceReaderFromURL(path, attributes, out reader), "Open generated MP4 for decoding");
            MediaFoundation.Check(MediaFoundation.Method<SetStreamSelectionMethod>(reader, 4)(reader, 0xfffffffe, 0),
                "Disable unselected streams");
            MediaFoundation.Check(MediaFoundation.Method<SetStreamSelectionMethod>(reader, 4)(reader, videoStream, 1),
                "Select the MP4 video stream");
            MediaFoundation.Check(MediaFoundation.MFCreateMediaType(out desiredType), "Create decoded RGB32 media type");
            MediaFoundation.SetGuid(desiredType, MediaFoundation.MajorType, MediaFoundation.Video);
            MediaFoundation.SetGuid(desiredType, MediaFoundation.Subtype, MediaFoundation.Rgb32);
            MediaFoundation.Check(MediaFoundation.Method<SetReaderMediaTypeMethod>(reader, 7)
                (reader, videoStream, IntPtr.Zero, desiredType), "Request decoded RGB32 video");

            for (var attempt = 0; attempt < 64; attempt++)
            {
                MediaFoundation.Check(MediaFoundation.Method<ReadSampleMethod>(reader, 9)
                    (reader, videoStream, 0, out _, out var flags, out _, out sample), "Decode an MP4 frame");
                Require((flags & 1) == 0, "The Windows source reader reported a decoder error.");
                if (sample != IntPtr.Zero)
                    break;
                Require((flags & 2) == 0, "The MP4 ended before the Windows decoder produced a frame.");
            }
            Require(sample != IntPtr.Zero, "The Windows decoder did not produce a frame.");
            MediaFoundation.Check(MediaFoundation.Method<GetReaderMediaTypeMethod>(reader, 6)
                (reader, videoStream, out actualType), "Read decoded frame format");
            var frameSizeKey = MediaFoundation.FrameSize;
            MediaFoundation.Check(MediaFoundation.Method<GetUInt64Method>(actualType, 8)
                (actualType, in frameSizeKey, out var dimensions), "Read decoded frame dimensions");
            var storageWidth = checked((int)(dimensions >> 32));
            var storageHeight = checked((int)(dimensions & uint.MaxValue));
            // H.264 decoders can expose macroblock-aligned storage (for example
            // 320 × 192) and declare the original 320 × 180 visible aperture.
            var aperture = GetDisplayAperture(actualType, storageWidth, storageHeight);
            var width = aperture.Width;
            var height = aperture.Height;
            Require(width == 320 && height == 180, $"Decoded display dimensions should be 320 × 180, received {width} × {height}.");
            MediaFoundation.Check(MediaFoundation.Method<GetPointerMethod>(sample, 41)(sample, out buffer),
                "Get decoded frame buffer");
            var storagePixels = CopyDecodedRgb32(buffer, actualType, storageWidth, storageHeight);
            var pixels = new byte[checked(width * height * 4)];
            for (var row = 0; row < height; row++)
                Buffer.BlockCopy(storagePixels, ((aperture.Y + row) * storageWidth + aperture.X) * 4,
                    pixels, row * width * 4, width * 4);
            var snapshot = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
            snapshot.Freeze();
            DesktopCapture.SavePng(snapshot, framePath);

            var top = (4 * width + 4) * 4;
            var bottom = ((height - 5) * width + 4) * 4;
            report.Add($"  Decoded display: {width} × {height} (storage {storageWidth} × {storageHeight}); top RGB({pixels[top + 2]}, {pixels[top + 1]}, {pixels[top]}); " +
                $"bottom RGB({pixels[bottom + 2]}, {pixels[bottom + 1]}, {pixels[bottom]}).");
            // H.264 and YUV conversion are lossy, so test dominant color with a
            // generous tolerance rather than requiring an exact RGB round trip.
            Require(pixels[top + 2] > 180 && pixels[top + 1] < 80 && pixels[top] < 80,
                "The decoded video's top should be red; the image may be vertically flipped or have swapped color channels.");
            Require(pixels[bottom] > 180 && pixels[bottom + 1] < 80 && pixels[bottom + 2] < 80,
                "The decoded video's bottom should be blue; the image may be vertically flipped or have swapped color channels.");
        }
        finally
        {
            MediaFoundation.Release(ref buffer);
            MediaFoundation.Release(ref sample);
            MediaFoundation.Release(ref actualType);
            MediaFoundation.Release(ref desiredType);
            MediaFoundation.Release(ref reader);
            MediaFoundation.Release(ref attributes);
            try
            {
                if (mediaInitialized)
                    MediaFoundation.Check(MediaFoundation.MFShutdown(), "Shut down self-test media decoder");
            }
            finally
            {
                if (comInitialized)
                    MediaFoundation.CoUninitialize();
            }
        }
    }

    private static Int32Rect GetDisplayAperture(IntPtr type, int storageWidth, int storageHeight)
    {
        var areas = new[]
        {
            new Guid("d7388766-18fe-48c6-a177-ee894867c8c4"), // MF_MT_MINIMUM_DISPLAY_APERTURE
            new Guid("66758743-7e5f-400d-980a-aa8596c85696")  // MF_MT_GEOMETRIC_APERTURE
        };
        var memory = Marshal.AllocHGlobal(16); // MFVideoArea: two MFOffsets and one SIZE.
        try
        {
            foreach (var area in areas)
            {
                var key = area;
                var hr = MediaFoundation.Method<GetBlobMethod>(type, 15)(type, in key, memory, 16, out var size);
                if (hr < 0)
                    continue;
                Require(size == 16, "The decoder returned an invalid display aperture.");
                var xFraction = unchecked((ushort)Marshal.ReadInt16(memory, 0));
                var yFraction = unchecked((ushort)Marshal.ReadInt16(memory, 4));
                var x = Marshal.ReadInt16(memory, 2);
                var y = Marshal.ReadInt16(memory, 6);
                var width = Marshal.ReadInt32(memory, 8);
                var height = Marshal.ReadInt32(memory, 12);
                Require(xFraction == 0 && yFraction == 0, "The synthetic video should have an integer-aligned display aperture.");
                Require(x >= 0 && y >= 0 && width > 0 && height > 0 &&
                    (long)x + width <= storageWidth && (long)y + height <= storageHeight,
                    "The decoded display aperture lies outside the frame storage.");
                return new Int32Rect(x, y, width, height);
            }
            return new Int32Rect(0, 0, storageWidth, storageHeight);
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static byte[] CopyDecodedRgb32(IntPtr buffer, IntPtr mediaType, int width, int height)
    {
        var iid2D = new Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb");
        IntPtr buffer2D = IntPtr.Zero;
        bool locked = false;
        var rowBytes = checked(width * 4);
        var pixels = new byte[checked(rowBytes * height)];
        try
        {
            var queryResult = Marshal.QueryInterface(buffer, ref iid2D, out buffer2D);
            IntPtr scanline;
            int pitch;
            if (queryResult >= 0)
            {
                // Lock2D returns the top row and its signed pitch. This preserves
                // the decoder's declared orientation, including bottom-up DIBs.
                MediaFoundation.Check(MediaFoundation.Method<Lock2DMethod>(buffer2D, 3)
                    (buffer2D, out scanline, out pitch), "Lock decoded 2D frame");
                locked = true;
            }
            else
            {
                MediaFoundation.Check(queryResult == unchecked((int)0x80004002) ? 0 : queryResult, "Query decoded 2D buffer");
                var strideKey = MediaFoundation.DefaultStride;
                var strideResult = MediaFoundation.Method<GetUInt32Method>(mediaType, 7)(mediaType, in strideKey, out var stride);
                if (strideResult >= 0)
                    pitch = unchecked((int)stride);
                else
                    MediaFoundation.Check(MFGetStrideForBitmapInfoHeader(22, (uint)width, out pitch), "Calculate decoded RGB32 stride");
                MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.LockBufferMethod>(buffer, 3)
                    (buffer, out scanline, out var maxLength, out var currentLength), "Lock decoded frame");
                locked = true;
                var required = checked((long)Math.Abs(pitch) * (height - 1) + rowBytes);
                Require(required <= currentLength && required <= maxLength, "The decoded frame buffer is smaller than its declared dimensions.");
                if (pitch < 0)
                    scanline = IntPtr.Add(scanline, checked(-pitch * (height - 1)));
            }
            Require(Math.Abs((long)pitch) >= rowBytes, "The decoded frame stride is narrower than a row of pixels.");
            for (var row = 0; row < height; row++)
                Marshal.Copy(IntPtr.Add(scanline, checked(row * pitch)), pixels, row * rowBytes, rowBytes);
            return pixels;
        }
        finally
        {
            if (locked)
            {
                var lockedBuffer = buffer2D == IntPtr.Zero ? buffer : buffer2D;
                _ = MediaFoundation.Method<MediaFoundation.SimpleMethod>(lockedBuffer, 4)(lockedBuffer);
            }
            MediaFoundation.Release(ref buffer2D);
        }
    }

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    private static extern int MFGetStrideForBitmapInfoHeader(uint format, uint width, out int stride);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetStreamSelectionMethod(IntPtr self, uint stream, int selected);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetReaderMediaTypeMethod(IntPtr self, uint stream, IntPtr reserved, IntPtr type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetReaderMediaTypeMethod(IntPtr self, uint stream, out IntPtr type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReadSampleMethod(IntPtr self, uint stream, uint controls, out uint actualStream,
        out uint flags, out long timestamp, out IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetUInt64Method(IntPtr self, in Guid key, out ulong value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetUInt32Method(IntPtr self, in Guid key, out uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetBlobMethod(IntPtr self, in Guid key, IntPtr buffer, uint bufferSize, out uint blobSize);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetPointerMethod(IntPtr self, out IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Lock2DMethod(IntPtr self, out IntPtr scanline, out int pitch);

    private static List<string> ReadMp4Boxes(string path)
    {
        using var stream = File.OpenRead(path);
        var boxes = new List<string>();
        Span<byte> header = stackalloc byte[8];
        while (stream.Position < stream.Length)
        {
            var start = stream.Position;
            Require(stream.Length - start >= 8, "MP4 ends with an incomplete box header.");
            stream.ReadExactly(header);
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header[4..8]);
            var headerSize = 8;
            if (size == 1)
            {
                Require(stream.Length - stream.Position >= 8, "MP4 ends with an incomplete extended box size.");
                stream.ReadExactly(header);
                size = BinaryPrimitives.ReadUInt64BigEndian(header);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = (ulong)(stream.Length - start);
            }
            Require(size >= (ulong)headerSize && size <= (ulong)(stream.Length - start), "An MP4 box has an invalid size.");
            if (type is "moov" or "mdat")
                Require(size > (ulong)headerSize, $"The MP4 {type} box is empty.");
            boxes.Add(type);
            stream.Position = checked(start + (long)size);
        }
        return boxes;
    }

    private static Bitmap CreateFrame(int width, int height, int frame)
    {
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            using var red = new System.Drawing.SolidBrush(DrawingColor.Red);
            using var blue = new System.Drawing.SolidBrush(DrawingColor.Blue);
            graphics.FillRectangle(red, 0, 0, width, height / 2);
            graphics.FillRectangle(blue, 0, height / 2, width, height - height / 2);
            using var font = new System.Drawing.Font("Segoe UI", 16, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var white = new System.Drawing.SolidBrush(DrawingColor.White);
            graphics.DrawString("TOP — Snap synthetic test", font, white, 16, 18);
            graphics.DrawString("BOTTOM — no screen captured", font, white, 16, height / 2 + 18);
            graphics.FillRectangle(white, 16 + (frame * 7) % Math.Max(1, width - 48), height / 2 - 12, 24, 24);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void Equal(Rect expected, Rect actual) =>
        Require(expected == actual, $"Expected {expected}; received {actual}.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
