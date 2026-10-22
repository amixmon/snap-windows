using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Snap.Interop;

/// <summary>
/// Small, explicitly owned Media Foundation interop layer. All pointers belong to
/// the recording thread; no RCW, encoder, or finalizer crosses COM apartments.
/// Vtable slots follow mfobjects.h and mfreadwrite.h in the Windows SDK.
/// </summary>
internal static class MediaFoundation
{
    internal const int Version = 0x00020070;

    internal static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    internal static readonly Guid Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    internal static readonly Guid Video = new("73646976-0000-0010-8000-00aa00389b71");
    internal static readonly Guid H264 = new("34363248-0000-0010-8000-00aa00389b71");
    internal static readonly Guid Rgb32 = new("00000016-0000-0010-8000-00aa00389b71");
    internal static readonly Guid FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    internal static readonly Guid FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    internal static readonly Guid PixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    internal static readonly Guid InterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    internal static readonly Guid AverageBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    internal static readonly Guid VideoProfile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    internal static readonly Guid DefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    internal static readonly Guid HardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    internal static readonly Guid DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern void CoUninitialize();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFStartup(int version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFShutdown();

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateAttributes(out IntPtr attributes, uint initialSize);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMediaType(out IntPtr mediaType);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateMemoryBuffer(uint maxLength, out IntPtr buffer);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCreateSample(out IntPtr sample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    internal static extern int MFCopyImage(IntPtr destination, int destinationStride,
        IntPtr source, int sourceStride, uint widthInBytes, uint lines);

    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int MFCreateSinkWriterFromURL(string outputUrl,
        IntPtr byteStream, IntPtr attributes, out IntPtr sinkWriter);

    internal static void Check(int hr, string operation)
    {
        if (hr < 0)
            throw new COMException($"{operation} failed (0x{hr:X8}). " +
                (Marshal.GetExceptionForHR(hr)?.Message ?? "Windows media error."), hr);
    }

    internal static void Release(ref IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return;
        Marshal.Release(pointer);
        pointer = IntPtr.Zero;
    }

    internal static T Method<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
    }

    internal static ulong Pair(uint high, uint low) => ((ulong)high << 32) | low;

    internal static void SetGuid(IntPtr attributes, Guid key, Guid value) =>
        Check(Method<SetGuidMethod>(attributes, 24)(attributes, in key, in value), "IMFAttributes.SetGUID");

    internal static void SetUInt32(IntPtr attributes, Guid key, uint value) =>
        Check(Method<SetUInt32Method>(attributes, 21)(attributes, in key, value), "IMFAttributes.SetUINT32");

    internal static void SetUInt64(IntPtr attributes, Guid key, ulong value) =>
        Check(Method<SetUInt64Method>(attributes, 22)(attributes, in key, value), "IMFAttributes.SetUINT64");

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetGuidMethod(IntPtr self, in Guid key, in Guid value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetUInt32Method(IntPtr self, in Guid key, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetUInt64Method(IntPtr self, in Guid key, ulong value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SimpleMethod(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int AddStreamMethod(IntPtr self, IntPtr mediaType, out uint streamIndex);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int SetInputMethod(IntPtr self, uint streamIndex, IntPtr mediaType, IntPtr parameters);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int WriteSampleMethod(IntPtr self, uint streamIndex, IntPtr sample);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int LockBufferMethod(IntPtr self, out IntPtr buffer, out uint maxLength, out uint currentLength);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int UInt32Method(IntPtr self, uint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int Int64Method(IntPtr self, long value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    internal delegate int PointerMethod(IntPtr self, IntPtr value);
}

/// <summary>An MP4 sink writer accepting owned, top-down BGRX frames.</summary>
internal sealed class MediaFoundationWriter : IDisposable
{
    private IntPtr _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly uint _streamIndex;
    private bool _begun;
    private bool _finalized;

    internal MediaFoundationWriter(string path, int width, int height, int fps)
    {
        _width = width;
        _height = height;
        IntPtr attributes = IntPtr.Zero;
        IntPtr outputType = IntPtr.Zero;
        IntPtr inputType = IntPtr.Zero;
        try
        {
            MediaFoundation.Check(MediaFoundation.MFCreateAttributes(out attributes, 2), "Create writer attributes");
            MediaFoundation.SetUInt32(attributes, MediaFoundation.HardwareTransforms, 1);
            // Capture is paced by the monotonic clock, so the sink need not pace it again.
            MediaFoundation.SetUInt32(attributes, MediaFoundation.DisableThrottling, 1);
            MediaFoundation.Check(MediaFoundation.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out _writer),
                "Create MP4 sink writer");

            MediaFoundation.Check(MediaFoundation.MFCreateMediaType(out outputType), "Create output media type");
            SetVideoFormat(outputType, MediaFoundation.H264, width, height, fps);
            var bitrate = (uint)Math.Clamp((long)width * height * fps / 4, 2_000_000, 40_000_000);
            MediaFoundation.SetUInt32(outputType, MediaFoundation.AverageBitrate, bitrate);
            MediaFoundation.SetUInt32(outputType, MediaFoundation.VideoProfile, 100); // H.264 High profile.
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.AddStreamMethod>(_writer, 3)
                (_writer, outputType, out _streamIndex), "Add H.264 video stream");

            MediaFoundation.Check(MediaFoundation.MFCreateMediaType(out inputType), "Create input media type");
            SetVideoFormat(inputType, MediaFoundation.Rgb32, width, height, fps);
            // MF stride is positive for top-down images. Explicitly declare it rather
            // than depending on a codec's default interpretation of RGB32 buffers.
            MediaFoundation.SetUInt32(inputType, MediaFoundation.DefaultStride, checked((uint)width * 4));
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.SetInputMethod>(_writer, 4)
                (_writer, _streamIndex, inputType, IntPtr.Zero), "Configure RGB32 video input");
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.SimpleMethod>(_writer, 5)(_writer), "Begin MP4 recording");
            _begun = true;
        }
        catch
        {
            MediaFoundation.Release(ref _writer);
            throw;
        }
        finally
        {
            MediaFoundation.Release(ref inputType);
            MediaFoundation.Release(ref outputType);
            MediaFoundation.Release(ref attributes);
        }
    }

    private static void SetVideoFormat(IntPtr type, Guid subtype, int width, int height, int fps)
    {
        MediaFoundation.SetGuid(type, MediaFoundation.MajorType, MediaFoundation.Video);
        MediaFoundation.SetGuid(type, MediaFoundation.Subtype, subtype);
        MediaFoundation.SetUInt64(type, MediaFoundation.FrameSize, MediaFoundation.Pair((uint)width, (uint)height));
        MediaFoundation.SetUInt64(type, MediaFoundation.FrameRate, MediaFoundation.Pair((uint)fps, 1));
        MediaFoundation.SetUInt64(type, MediaFoundation.PixelAspectRatio, MediaFoundation.Pair(1, 1));
        MediaFoundation.SetUInt32(type, MediaFoundation.InterlaceMode, 2); // Progressive.
    }

    internal void WriteFrame(Bitmap bitmap, long sampleTime, long duration)
    {
        if (!_begun || _finalized)
            throw new InvalidOperationException("The MP4 writer is not accepting frames.");
        if (bitmap.Width != _width || bitmap.Height != _height)
            throw new ArgumentException("The captured frame does not match the recording dimensions.", nameof(bitmap));

        IntPtr buffer = IntPtr.Zero;
        IntPtr sample = IntPtr.Zero;
        BitmapData? pixels = null;
        bool locked = false;
        try
        {
            var length = checked((uint)(_width * _height * 4));
            MediaFoundation.Check(MediaFoundation.MFCreateMemoryBuffer(length, out buffer), "Allocate video frame");
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.LockBufferMethod>(buffer, 3)
                (buffer, out var destination, out _, out _), "Lock video frame");
            locked = true;
            pixels = bitmap.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            MediaFoundation.Check(MediaFoundation.MFCopyImage(destination, _width * 4,
                pixels.Scan0, pixels.Stride, (uint)_width * 4, (uint)_height), "Copy captured pixels");
            bitmap.UnlockBits(pixels);
            pixels = null;
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.SimpleMethod>(buffer, 4)(buffer), "Unlock video frame");
            locked = false;
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.UInt32Method>(buffer, 6)(buffer, length), "Set video frame length");
            MediaFoundation.Check(MediaFoundation.MFCreateSample(out sample), "Create video sample");
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.PointerMethod>(sample, 42)(sample, buffer), "Attach video frame");
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.Int64Method>(sample, 36)(sample, sampleTime), "Set video timestamp");
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.Int64Method>(sample, 38)(sample, duration), "Set video duration");
            MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.WriteSampleMethod>(_writer, 6)
                (_writer, _streamIndex, sample), "Encode video frame");
        }
        finally
        {
            if (pixels is not null)
                bitmap.UnlockBits(pixels);
            if (locked)
                _ = MediaFoundation.Method<MediaFoundation.SimpleMethod>(buffer, 4)(buffer);
            MediaFoundation.Release(ref sample);
            MediaFoundation.Release(ref buffer);
        }
    }

    internal void Complete()
    {
        if (!_begun || _finalized || _writer == IntPtr.Zero)
            return;
        _finalized = true;
        MediaFoundation.Check(MediaFoundation.Method<MediaFoundation.SimpleMethod>(_writer, 11)(_writer), "Finalize MP4 recording");
    }

    public void Dispose() => MediaFoundation.Release(ref _writer);
}
