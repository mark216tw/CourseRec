using System.Runtime.InteropServices;
using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// Windows Media Foundation（<c>mfplat.dll</c> / <c>mfreadwrite.dll</c>）硬體 H.264 / AAC 編碼器 COM 互通方法。
/// </summary>
public static unsafe partial class MediaFoundationNativeMethods
{
    public const uint MfVersion = 0x00020070;
    public const uint MfStartupNosocket = 0x1;
    public const uint MfVideoInterlaceProgressive = 2;
    public const uint AvEncH264VProfileMain = 77;
    public const uint AvEncH264VProfileHigh = 100;

    private static readonly Guid MfReadwriteEnableHardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0");
    private static readonly Guid MfSinkWriterDisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
    private static readonly Guid MfTranscodeContainerType = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
    private static readonly Guid MfTranscodeContainerTypeFmpeg4 = new("9ba876f1-419f-4b77-a1e0-35959d9d4004");

    private static readonly Guid MfMtMajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
    private static readonly Guid MfMtSubtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
    private static readonly Guid MfMtAllSamplesIndependent = new("c9173739-5e56-461c-b713-46fb995cb95f");
    private static readonly Guid MfMtInterlaceMode = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
    private static readonly Guid MfMtFrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d");
    private static readonly Guid MfMtFrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
    private static readonly Guid MfMtPixelAspectRatio = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
    private static readonly Guid MfMtAvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
    private static readonly Guid MfMtDefaultStride = new("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
    private static readonly Guid MfMtMpeg2Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036");
    private static readonly Guid MfMtMaxKeyframeSpacing = new("c16eb52b-73a1-476f-8d62-839d6a020652");

    private static readonly Guid MfMtAudioNumChannels = new("37e48bf5-645e-4c5b-89de-ada9e29b696a");
    private static readonly Guid MfMtAudioSamplesPerSecond = new("5faeeae7-0290-4c31-9e8a-c534f68d9dba");
    private static readonly Guid MfMtAudioAvgBytesPerSecond = new("1aab75c8-cfef-451c-ab95-ac034b8e1731");
    private static readonly Guid MfMtAudioBlockAlignment = new("322de230-9eeb-43bd-ab7a-ff412251541d");
    private static readonly Guid MfMtAudioBitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

    private static readonly Guid MfMediaTypeVideo = new("73646976-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfMediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatH264 = new("34363248-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfVideoFormatRgb32 = new("00000016-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfAudioFormatAac = new("00001610-0000-0010-8000-00AA00389B71");
    private static readonly Guid MfAudioFormatPcm = new("00000001-0000-0010-8000-00AA00389B71");

    [LibraryImport("mfplat.dll")]
    public static partial int MFStartup(uint version, uint dwFlags);

    [LibraryImport("mfplat.dll")]
    public static partial int MFShutdown();

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateAttributes(out nint ppMFAttributes, uint cInitialSize);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMediaType(out nint ppMFType);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateMemoryBuffer(uint cbMaxLength, out nint ppBuffer);

    [LibraryImport("mfplat.dll")]
    private static partial int MFCreateSample(out nint ppIMFSample);

    [LibraryImport("mfreadwrite.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MFCreateSinkWriterFromURL(
        string pwszOutputURL,
        nint pByteStream,
        nint pAttributes,
        out nint ppSinkWriter);

    public static int CreateConfiguredMp4SinkWriter(
        string outputFilePath,
        RecordingProfile profile,
        out nint sinkWriter,
        out uint videoStreamIndex,
        out uint audioStreamIndex)
    {
        return CreateConfiguredMp4SinkWriter(
            outputFilePath,
            profile,
            requestedInputWidth: profile.Width,
            requestedInputHeight: profile.Height,
            preferFragmentedMp4: false,
            out sinkWriter,
            out videoStreamIndex,
            out audioStreamIndex,
            out _,
            out _);
    }

    public static int CreateConfiguredMp4SinkWriter(
        string outputFilePath,
        RecordingProfile profile,
        int requestedInputWidth,
        int requestedInputHeight,
        bool preferFragmentedMp4,
        out nint sinkWriter,
        out uint videoStreamIndex,
        out uint audioStreamIndex,
        out int configuredInputWidth,
        out int configuredInputHeight)
    {
        sinkWriter = 0;
        videoStreamIndex = 0;
        audioStreamIndex = 0;
        configuredInputWidth = profile.Width;
        configuredInputHeight = profile.Height;

        int hr = CreateSinkWriterInstance(outputFilePath, preferFragmentedMp4, out nint writer);
        if (hr < 0 && preferFragmentedMp4)
        {
            hr = CreateSinkWriterInstance(outputFilePath, useFragmentedMp4: false, out writer);
        }

        if (hr < 0 || writer == 0)
        {
            return hr < 0 ? hr : -1;
        }

        ulong packedFrameSize = PackUint32Pair((uint)profile.Width, (uint)profile.Height);
        ulong packedFrameRate = PackUint32Pair((uint)profile.FrameRate, 1u);
        ulong packedAspect = PackUint32Pair(1u, 1u);

        hr = ConfigureVideoStream(
            writer,
            profile,
            requestedInputWidth,
            requestedInputHeight,
            packedFrameSize,
            packedFrameRate,
            packedAspect,
            out videoStreamIndex,
            out configuredInputWidth,
            out configuredInputHeight);
        if (hr < 0)
        {
            D3D11NativeMethods.ReleaseCom(ref writer);
            return hr;
        }

        hr = ConfigureAudioStream(writer, profile, out audioStreamIndex);
        if (hr < 0)
        {
            D3D11NativeMethods.ReleaseCom(ref writer);
            return hr;
        }

        // IMFSinkWriter::BeginWriting (slot 5)
        var beginWriting = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)writer)[5];
        hr = beginWriting(writer);
        if (hr < 0)
        {
            D3D11NativeMethods.ReleaseCom(ref writer);
            return hr;
        }

        sinkWriter = writer;
        return 0;
    }

    private static int CreateSinkWriterInstance(string outputFilePath, bool useFragmentedMp4, out nint writer)
    {
        writer = 0;
        int hr = MFCreateAttributes(out nint attributes, useFragmentedMp4 ? 3u : 2u);
        if (hr < 0)
        {
            return hr;
        }

        try
        {
            SetUint32(attributes, MfReadwriteEnableHardwareTransforms, 1);
            SetUint32(attributes, MfSinkWriterDisableThrottling, 1);
            if (useFragmentedMp4)
            {
                SetGuid(attributes, MfTranscodeContainerType, MfTranscodeContainerTypeFmpeg4);
            }

            return MFCreateSinkWriterFromURL(outputFilePath, 0, attributes, out writer);
        }
        finally
        {
            D3D11NativeMethods.ReleaseCom(ref attributes);
        }
    }

    public static int WriteBufferSample(
        nint sinkWriter,
        uint streamIndex,
        ReadOnlySpan<byte> payload,
        long sampleTime100Ns,
        long sampleDuration100Ns)
    {
        return WriteBufferSample(
            sinkWriter,
            streamIndex,
            payload,
            sampleTime100Ns,
            sampleDuration100Ns,
            Span<(nint Buffer, uint Capacity)>.Empty);
    }

    public static int WriteBufferSample(
        nint sinkWriter,
        uint streamIndex,
        ReadOnlySpan<byte> payload,
        long sampleTime100Ns,
        long sampleDuration100Ns,
        Span<(nint Buffer, uint Capacity)> bufferPool)
    {
        if (sinkWriter == 0 || payload.IsEmpty)
        {
            return -1;
        }

        uint requiredLength = (uint)payload.Length;
        int hr = AcquireMediaBuffer(bufferPool, requiredLength, out nint mediaBuffer, out bool ownedByPool);
        if (hr < 0 || mediaBuffer == 0)
        {
            return hr;
        }

        try
        {
            byte* dstPtr = null;
            uint maxLen = 0;
            uint curLen = 0;

            // IMFMediaBuffer::Lock (slot 3), Unlock (slot 4), SetCurrentLength (slot 6)
            var lockBuf = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, int>)(*(nint**)mediaBuffer)[3];
            var unlockBuf = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)mediaBuffer)[4];
            var setCurLen = (delegate* unmanaged[Stdcall]<nint, uint, int>)(*(nint**)mediaBuffer)[6];

            hr = lockBuf(mediaBuffer, &dstPtr, &maxLen, &curLen);
            if (hr < 0 || dstPtr == null)
            {
                return hr;
            }

            try
            {
                fixed (byte* srcPtr = payload)
                {
                    Buffer.MemoryCopy(srcPtr, dstPtr, maxLen, payload.Length);
                }
            }
            finally
            {
                _ = unlockBuf(mediaBuffer);
            }

            _ = setCurLen(mediaBuffer, requiredLength);

            hr = MFCreateSample(out nint sample);
            if (hr < 0 || sample == 0)
            {
                return hr;
            }

            try
            {
                // IMFSample::SetSampleTime (slot 36), SetSampleDuration (slot 38), AddBuffer (slot 42)
                var setSampleTime = (delegate* unmanaged[Stdcall]<nint, long, int>)(*(nint**)sample)[36];
                var setSampleDur = (delegate* unmanaged[Stdcall]<nint, long, int>)(*(nint**)sample)[38];
                var addBuffer = (delegate* unmanaged[Stdcall]<nint, nint, int>)(*(nint**)sample)[42];

                _ = addBuffer(sample, mediaBuffer);
                _ = setSampleTime(sample, sampleTime100Ns);
                _ = setSampleDur(sample, sampleDuration100Ns);

                // IMFSinkWriter::WriteSample (slot 6)
                var writeSample = (delegate* unmanaged[Stdcall]<nint, uint, nint, int>)(*(nint**)sinkWriter)[6];
                return writeSample(sinkWriter, streamIndex, sample);
            }
            finally
            {
                D3D11NativeMethods.ReleaseCom(ref sample);
            }
        }
        finally
        {
            if (!ownedByPool)
            {
                D3D11NativeMethods.ReleaseCom(ref mediaBuffer);
            }
        }
    }

    public static void ReleaseBufferPool(Span<(nint Buffer, uint Capacity)> bufferPool)
    {
        for (int i = 0; i < bufferPool.Length; i++)
        {
            nint buf = bufferPool[i].Buffer;
            if (buf != 0)
            {
                D3D11NativeMethods.ReleaseCom(ref buf);
                bufferPool[i] = default;
            }
        }
    }

    private static int AcquireMediaBuffer(
        Span<(nint Buffer, uint Capacity)> bufferPool,
        uint requiredLength,
        out nint mediaBuffer,
        out bool ownedByPool)
    {
        mediaBuffer = 0;
        ownedByPool = false;

        if (!bufferPool.IsEmpty)
        {
            for (int i = 0; i < bufferPool.Length; i++)
            {
                if (bufferPool[i].Buffer != 0
                    && bufferPool[i].Capacity == requiredLength
                    && IsExclusivelyOwnedComObject(bufferPool[i].Buffer))
                {
                    mediaBuffer = bufferPool[i].Buffer;
                    ownedByPool = true;
                    return 0;
                }
            }

            for (int i = 0; i < bufferPool.Length; i++)
            {
                bool canReplace = bufferPool[i].Buffer == 0
                    || (bufferPool[i].Capacity != requiredLength && IsExclusivelyOwnedComObject(bufferPool[i].Buffer));
                if (!canReplace)
                {
                    continue;
                }

                nint old = bufferPool[i].Buffer;
                if (old != 0)
                {
                    D3D11NativeMethods.ReleaseCom(ref old);
                    bufferPool[i] = default;
                }

                int poolHr = MFCreateMemoryBuffer(requiredLength, out nint created);
                if (poolHr < 0 || created == 0)
                {
                    return poolHr;
                }

                bufferPool[i] = (created, requiredLength);
                mediaBuffer = created;
                ownedByPool = true;
                return 0;
            }
        }

        return MFCreateMemoryBuffer(requiredLength, out mediaBuffer);
    }

    private static bool IsExclusivelyOwnedComObject(nint comPtr)
    {
        if (comPtr == 0)
        {
            return false;
        }

        var addRef = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)comPtr)[1];
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)comPtr)[2];
        _ = addRef(comPtr);
        return release(comPtr) == 1;
    }

    public static int FinalizeAndReleaseSinkWriter(ref nint sinkWriter)
    {
        if (sinkWriter == 0)
        {
            return 0;
        }

        // IMFSinkWriter::Finalize (slot 11)
        var finalize = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)sinkWriter)[11];
        int hr = finalize(sinkWriter);
        D3D11NativeMethods.ReleaseCom(ref sinkWriter);
        return hr;
    }

    private static int ConfigureVideoStream(
        nint writer,
        RecordingProfile profile,
        int requestedInputWidth,
        int requestedInputHeight,
        ulong packedFrameSize,
        ulong packedFrameRate,
        ulong packedAspect,
        out uint videoStreamIndex,
        out int configuredInputWidth,
        out int configuredInputHeight)
    {
        videoStreamIndex = 0;
        configuredInputWidth = profile.Width;
        configuredInputHeight = profile.Height;

        int hr = MFCreateMediaType(out nint outType);
        if (hr < 0)
        {
            return hr;
        }

        nint inType = 0;
        try
        {
            uint keyframeSpacing = (uint)Math.Max(15, profile.FrameRate * 2);
            SetGuid(outType, MfMtMajorType, MfMediaTypeVideo);
            SetGuid(outType, MfMtSubtype, MfVideoFormatH264);
            SetUint32(outType, MfMtMpeg2Profile, AvEncH264VProfileHigh);
            SetUint32(outType, MfMtMaxKeyframeSpacing, keyframeSpacing);
            SetUint32(outType, MfMtAvgBitrate, profile.VideoBitrateBps);
            SetUint32(outType, MfMtInterlaceMode, MfVideoInterlaceProgressive);
            SetUint64(outType, MfMtFrameSize, packedFrameSize);
            SetUint64(outType, MfMtFrameRate, packedFrameRate);
            SetUint64(outType, MfMtPixelAspectRatio, packedAspect);

            uint streamIdx = 0;
            var addStream = (delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)(*(nint**)writer)[3];
            hr = addStream(writer, outType, &streamIdx);
            if (hr < 0)
            {
                SetUint32(outType, MfMtMpeg2Profile, AvEncH264VProfileMain);
                hr = addStream(writer, outType, &streamIdx);
                if (hr < 0)
                {
                    return hr;
                }
            }

            hr = MFCreateMediaType(out inType);
            if (hr < 0)
            {
                return hr;
            }

            SetGuid(inType, MfMtMajorType, MfMediaTypeVideo);
            SetGuid(inType, MfMtSubtype, MfVideoFormatRgb32);
            SetUint32(inType, MfMtInterlaceMode, MfVideoInterlaceProgressive);
            SetUint64(inType, MfMtFrameRate, packedFrameRate);
            SetUint64(inType, MfMtPixelAspectRatio, packedAspect);

            var setInputMediaType = (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)(*(nint**)writer)[4];

            bool canDelegateScalingToMft = requestedInputWidth >= 2
                && requestedInputHeight >= 2
                && (requestedInputWidth & 1) == 0
                && (requestedInputHeight & 1) == 0
                && (requestedInputWidth != profile.Width || requestedInputHeight != profile.Height)
                && Math.Abs(((double)requestedInputWidth / requestedInputHeight) - ((double)profile.Width / profile.Height)) <= 0.015;

            if (canDelegateScalingToMft)
            {
                SetUint64(inType, MfMtFrameSize, PackUint32Pair((uint)requestedInputWidth, (uint)requestedInputHeight));
                SetUint32(inType, MfMtDefaultStride, (uint)(requestedInputWidth * 4));
                if (setInputMediaType(writer, streamIdx, inType, 0) >= 0)
                {
                    configuredInputWidth = requestedInputWidth;
                    configuredInputHeight = requestedInputHeight;
                    videoStreamIndex = streamIdx;
                    return 0;
                }
            }

            SetUint64(inType, MfMtFrameSize, packedFrameSize);
            SetUint32(inType, MfMtDefaultStride, (uint)(profile.Width * 4));
            hr = setInputMediaType(writer, streamIdx, inType, 0);
            if (hr < 0)
            {
                return hr;
            }

            videoStreamIndex = streamIdx;
            return 0;
        }
        finally
        {
            D3D11NativeMethods.ReleaseCom(ref inType);
            D3D11NativeMethods.ReleaseCom(ref outType);
        }
    }

    private static int ConfigureAudioStream(
        nint writer,
        RecordingProfile profile,
        out uint audioStreamIndex)
    {
        audioStreamIndex = 0;

        int hr = MFCreateMediaType(out nint outType);
        if (hr < 0)
        {
            return hr;
        }

        nint inType = 0;
        try
        {
            SetGuid(outType, MfMtMajorType, MfMediaTypeAudio);
            SetGuid(outType, MfMtSubtype, MfAudioFormatAac);
            SetUint32(outType, MfMtAudioNumChannels, profile.AudioChannels);
            SetUint32(outType, MfMtAudioSamplesPerSecond, profile.AudioSampleRate);
            SetUint32(outType, MfMtAudioBitsPerSample, 16);
            SetUint32(outType, MfMtAudioAvgBytesPerSecond, profile.AudioBitrateBps / 8u);

            uint streamIdx = 0;
            var addStream = (delegate* unmanaged[Stdcall]<nint, nint, uint*, int>)(*(nint**)writer)[3];
            hr = addStream(writer, outType, &streamIdx);
            if (hr < 0)
            {
                return hr;
            }

            hr = MFCreateMediaType(out inType);
            if (hr < 0)
            {
                return hr;
            }

            uint blockAlign = profile.AudioChannels * 2u;
            SetGuid(inType, MfMtMajorType, MfMediaTypeAudio);
            SetGuid(inType, MfMtSubtype, MfAudioFormatPcm);
            SetUint32(inType, MfMtAudioNumChannels, profile.AudioChannels);
            SetUint32(inType, MfMtAudioSamplesPerSecond, profile.AudioSampleRate);
            SetUint32(inType, MfMtAudioBitsPerSample, 16);
            SetUint32(inType, MfMtAudioBlockAlignment, blockAlign);
            SetUint32(inType, MfMtAudioAvgBytesPerSecond, profile.AudioSampleRate * blockAlign);
            SetUint32(inType, MfMtAllSamplesIndependent, 1);

            var setInputMediaType = (delegate* unmanaged[Stdcall]<nint, uint, nint, nint, int>)(*(nint**)writer)[4];
            hr = setInputMediaType(writer, streamIdx, inType, 0);
            if (hr < 0)
            {
                return hr;
            }

            audioStreamIndex = streamIdx;
            return 0;
        }
        finally
        {
            D3D11NativeMethods.ReleaseCom(ref inType);
            D3D11NativeMethods.ReleaseCom(ref outType);
        }
    }

    private static ulong PackUint32Pair(uint high, uint low)
    {
        return ((ulong)high << 32) | low;
    }

    private static void SetUint32(nint attr, Guid key, uint val)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, uint, int>)(*(nint**)attr)[21];
        _ = fn(attr, &key, val);
    }

    private static void SetUint64(nint attr, Guid key, ulong val)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, ulong, int>)(*(nint**)attr)[22];
        _ = fn(attr, &key, val);
    }

    private static void SetGuid(nint attr, Guid key, Guid val)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, Guid*, int>)(*(nint**)attr)[24];
        _ = fn(attr, &key, &val);
    }
}
