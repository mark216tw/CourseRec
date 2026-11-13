using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// 原生 WASAPI（系統 Loopback、單一視窗 Process Loopback 與麥克風擷取）COM 互通封裝。
/// </summary>
public static unsafe partial class WasapiNativeMethods
{
    public const int EDataFlowRender = 0;
    public const int EDataFlowCapture = 1;
    public const int ERoleConsole = 0;
    public const int ERoleMultimedia = 1;
    public const uint ClsCtxAll = 23;

    public const int AudclntSharemodeShared = 0;
    public const uint AudclntStreamflagsLoopback = 0x00020000;
    public const uint AudclntStreamflagsAutoconvertPcm = 0x80000000;
    public const uint AudclntStreamflagsSrcDefaultQuality = 0x08000000;
    public const uint AudclntBufferflagsSilent = 0x00000002;
    public const int AudclntEDeviceInvalidated = unchecked((int)0x88890004);
    private const uint WaitObject0 = 0;

    public const ushort WaveFormatPcm = 1;
    public const ushort WaveFormatIeeeFloat = 3;
    public const ushort WaveFormatExtensibleTag = 0xFFFE;

    private const ushort VtBlob = 65;
    private const int ActivationTypeProcessLoopback = 1;
    private const int ProcessLoopbackModeIncludeTree = 0;
    private const string VirtualAudioDeviceProcessLoopback = @"VAD\Process_Loopback";

    private static readonly Guid ClsidMmDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidMmDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IidAgileObject = new("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90");
    private static readonly Guid IidActivateCompletionHandler = new("41D949AB-9862-444A-80F6-C261334DA5EB");
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");
    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00AA00389B71");

    private static readonly nint* CompletionHandlerVtbl = CreateCompletionHandlerVtbl();

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WaveFormatEx
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSec;
        public uint AvgBytesPerSec;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort CbSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct WaveFormatExtensible
    {
        public WaveFormatEx Format;
        public ushort ValidBitsPerSample;
        public uint ChannelMask;
        public Guid SubFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariantBlob
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public uint CbSize;
        public uint Padding;
        public nint PBlobData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCompletionHandlerState
    {
        public nint* Vtbl;
        public int RefCount;
        public nint SignalEventHandle;
        public int ActivateHr;
        public nint ActivatedAudioClient;
        public AudioClientActivationParams ActivationParams;
        public PropVariantBlob PropVariant;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint pv);

    [LibraryImport("mmdevapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        in Guid riid,
        nint activationParams,
        nint completionHandler,
        out nint activationOperation);

    public static bool IsProcessLoopbackSupportedByOs()
    {
        // Windows 10 Build 20348 / Windows 11 Build 22000+
        return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
    }

    public static string? TryGetDefaultEndpointId(int dataFlow)
    {
        int hr = CoCreateInstance(in ClsidMmDeviceEnumerator, 0, ClsCtxAll, in IidMmDeviceEnumerator, out nint enumerator);
        if (hr < 0 || enumerator == 0)
        {
            return null;
        }

        try
        {
            nint device = 0;
            var getDefault = (delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)(*(nint**)enumerator)[4];
            hr = getDefault(enumerator, dataFlow, ERoleMultimedia, &device);
            if (hr < 0 || device == 0)
            {
                return null;
            }

            try
            {
                nint idPtr = 0;
                var getId = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)device)[5];
                hr = getId(device, &idPtr);
                if (hr < 0 || idPtr == 0)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUni(idPtr);
                }
                finally
                {
                    CoTaskMemFree(idPtr);
                }
            }
            finally
            {
                D3D11NativeMethods.ReleaseCom(ref device);
            }
        }
        finally
        {
            D3D11NativeMethods.ReleaseCom(ref enumerator);
        }
    }

    public static bool TryOpenEndpointCapture(
        int dataFlow,
        bool isLoopback,
        out nint audioClient,
        out nint captureClient,
        out int channels,
        out int sampleRate,
        out bool isFloatFormat,
        out int bitsPerSample,
        out string? endpointId)
    {
        audioClient = 0;
        captureClient = 0;
        channels = 2;
        sampleRate = 48_000;
        isFloatFormat = true;
        bitsPerSample = 32;
        endpointId = null;

        int hr = CoCreateInstance(in ClsidMmDeviceEnumerator, 0, ClsCtxAll, in IidMmDeviceEnumerator, out nint enumerator);
        if (hr < 0 || enumerator == 0)
        {
            return false;
        }

        try
        {
            nint device = 0;
            var getDefault = (delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)(*(nint**)enumerator)[4];
            hr = getDefault(enumerator, dataFlow, ERoleMultimedia, &device);
            if (hr < 0 || device == 0)
            {
                return false;
            }

            try
            {
                nint idPtr = 0;
                var getId = (delegate* unmanaged[Stdcall]<nint, nint*, int>)(*(nint**)device)[5];
                if (getId(device, &idPtr) >= 0 && idPtr != 0)
                {
                    endpointId = Marshal.PtrToStringUni(idPtr);
                    CoTaskMemFree(idPtr);
                }

                Guid audioClientIid = IidAudioClient;
                nint client = 0;
                var activate = (delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)(*(nint**)device)[3];
                hr = activate(device, &audioClientIid, ClsCtxAll, 0, &client);
                if (hr < 0 || client == 0)
                {
                    return false;
                }

                WaveFormatEx* mixFormat = null;
                var getMixFormat = (delegate* unmanaged[Stdcall]<nint, WaveFormatEx**, int>)(*(nint**)client)[8];
                hr = getMixFormat(client, &mixFormat);
                if (hr < 0 || mixFormat == null)
                {
                    D3D11NativeMethods.ReleaseCom(ref client);
                    return false;
                }

                try
                {
                    uint baseLoopbackFlag = isLoopback ? AudclntStreamflagsLoopback : 0u;
                    const long bufferDuration100Ns = 500_000; // 50ms
                    var initialize = (delegate* unmanaged[Stdcall]<nint, int, uint, long, long, WaveFormatEx*, Guid*, int>)(*(nint**)client)[3];

                    var preferredFormat = new WaveFormatEx
                    {
                        FormatTag = WaveFormatIeeeFloat,
                        Channels = 2,
                        SamplesPerSec = 48_000,
                        BitsPerSample = 32,
                        BlockAlign = 8,
                        AvgBytesPerSec = 48_000 * 8,
                        CbSize = 0,
                    };

                    uint autoConvertFlags = baseLoopbackFlag | AudclntStreamflagsAutoconvertPcm | AudclntStreamflagsSrcDefaultQuality;
                    hr = initialize(client, AudclntSharemodeShared, autoConvertFlags, bufferDuration100Ns, 0, &preferredFormat, null);
                    if (hr >= 0)
                    {
                        channels = 2;
                        sampleRate = 48_000;
                        isFloatFormat = true;
                        bitsPerSample = 32;
                    }
                    else
                    {
                        ParseWaveFormat(mixFormat, out channels, out sampleRate, out isFloatFormat, out bitsPerSample);
                        hr = initialize(client, AudclntSharemodeShared, baseLoopbackFlag, bufferDuration100Ns, 0, mixFormat, null);
                    }

                    if (hr < 0)
                    {
                        D3D11NativeMethods.ReleaseCom(ref client);
                        return false;
                    }

                    Guid captureIid = IidAudioCaptureClient;
                    nint capClient = 0;
                    var getService = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)client)[14];
                    hr = getService(client, &captureIid, &capClient);
                    if (hr < 0 || capClient == 0)
                    {
                        D3D11NativeMethods.ReleaseCom(ref client);
                        return false;
                    }

                    var start = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)client)[10];
                    hr = start(client);
                    if (hr < 0)
                    {
                        D3D11NativeMethods.ReleaseCom(ref capClient);
                        D3D11NativeMethods.ReleaseCom(ref client);
                        return false;
                    }

                    audioClient = client;
                    captureClient = capClient;
                    return true;
                }
                finally
                {
                    CoTaskMemFree((nint)mixFormat);
                }
            }
            finally
            {
                D3D11NativeMethods.ReleaseCom(ref device);
            }
        }
        finally
        {
            D3D11NativeMethods.ReleaseCom(ref enumerator);
        }
    }

    public static bool TryOpenProcessLoopbackCapture(
        uint processId,
        out nint audioClient,
        out nint captureClient)
    {
        audioClient = 0;
        captureClient = 0;

        if (processId == 0 || !IsProcessLoopbackSupportedByOs())
        {
            return false;
        }

        nint eventHandle = CreateEventW(0, bManualReset: true, bInitialState: false, null);
        if (eventHandle == 0)
        {
            return false;
        }

        var statePtr = (NativeCompletionHandlerState*)NativeMemory.AllocZeroed((nuint)sizeof(NativeCompletionHandlerState));
        statePtr->Vtbl = CompletionHandlerVtbl;
        statePtr->RefCount = 1;
        statePtr->SignalEventHandle = eventHandle;
        statePtr->ActivateHr = -1;
        statePtr->ActivationParams = new AudioClientActivationParams
        {
            ActivationType = ActivationTypeProcessLoopback,
            TargetProcessId = processId,
            ProcessLoopbackMode = ProcessLoopbackModeIncludeTree,
        };
        statePtr->PropVariant = new PropVariantBlob
        {
            Vt = VtBlob,
            CbSize = (uint)sizeof(AudioClientActivationParams),
            PBlobData = (nint)(&statePtr->ActivationParams),
        };

        try
        {
            int hr = ActivateAudioInterfaceAsync(
                VirtualAudioDeviceProcessLoopback,
                in IidAudioClient,
                (nint)(&statePtr->PropVariant),
                (nint)statePtr,
                out nint operation);

            if (hr < 0)
            {
                return false;
            }

            try
            {
                if (WaitForSingleObject(statePtr->SignalEventHandle, 3000) != WaitObject0)
                {
                    return false;
                }
            }
            finally
            {
                D3D11NativeMethods.ReleaseCom(ref operation);
            }

            if (statePtr->ActivateHr < 0 || statePtr->ActivatedAudioClient == 0)
            {
                return false;
            }

            nint client = statePtr->ActivatedAudioClient;
            statePtr->ActivatedAudioClient = 0;

            var format = new WaveFormatEx
            {
                FormatTag = WaveFormatIeeeFloat,
                Channels = 2,
                SamplesPerSec = 48_000,
                BitsPerSample = 32,
                BlockAlign = 8,
                AvgBytesPerSec = 48_000 * 8,
                CbSize = 0,
            };

            const long bufferDuration100Ns = 500_000; // 50ms
            var initialize = (delegate* unmanaged[Stdcall]<nint, int, uint, long, long, WaveFormatEx*, Guid*, int>)(*(nint**)client)[3];
            hr = initialize(client, AudclntSharemodeShared, AudclntStreamflagsLoopback, bufferDuration100Ns, 0, &format, null);
            if (hr < 0)
            {
                D3D11NativeMethods.ReleaseCom(ref client);
                return false;
            }

            Guid captureIid = IidAudioCaptureClient;
            nint capClient = 0;
            var getService = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)client)[14];
            hr = getService(client, &captureIid, &capClient);
            if (hr < 0 || capClient == 0)
            {
                D3D11NativeMethods.ReleaseCom(ref client);
                return false;
            }

            var start = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)client)[10];
            hr = start(client);
            if (hr < 0)
            {
                D3D11NativeMethods.ReleaseCom(ref capClient);
                D3D11NativeMethods.ReleaseCom(ref client);
                return false;
            }

            audioClient = client;
            captureClient = capClient;
            return true;
        }
        finally
        {
            ReleaseInternal(statePtr);
        }
    }

    public static int DrainCapturePacketsToFloat(
        nint captureClient,
        int channels,
        bool isFloatFormat,
        int bitsPerSample,
        List<float> outputSamples)
    {
        outputSamples.Clear();
        if (captureClient == 0 || channels <= 0)
        {
            return 0;
        }

        var getNextPacketSize = (delegate* unmanaged[Stdcall]<nint, uint*, int>)(*(nint**)captureClient)[5];
        var getBuffer = (delegate* unmanaged[Stdcall]<nint, byte**, uint*, uint*, ulong*, ulong*, int>)(*(nint**)captureClient)[3];
        var releaseBuffer = (delegate* unmanaged[Stdcall]<nint, uint, int>)(*(nint**)captureClient)[4];

        uint packetFrames = 0;
        int hr = getNextPacketSize(captureClient, &packetFrames);
        if (hr < 0)
        {
            return hr;
        }

        while (packetFrames > 0)
        {
            byte* dataPtr = null;
            uint framesToRead = 0;
            uint flags = 0;
            ulong devPos = 0;
            ulong qpcPos = 0;

            hr = getBuffer(captureClient, &dataPtr, &framesToRead, &flags, &devPos, &qpcPos);
            if (hr < 0)
            {
                return hr;
            }

            int totalSamples = (int)framesToRead * channels;
            if ((flags & AudclntBufferflagsSilent) != 0 || dataPtr == null)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    outputSamples.Add(0f);
                }
            }
            else if (isFloatFormat && bitsPerSample == 32)
            {
                float* fPtr = (float*)dataPtr;
                for (int i = 0; i < totalSamples; i++)
                {
                    outputSamples.Add(fPtr[i]);
                }
            }
            else if (bitsPerSample == 16)
            {
                short* sPtr = (short*)dataPtr;
                for (int i = 0; i < totalSamples; i++)
                {
                    outputSamples.Add(sPtr[i] / 32768f);
                }
            }
            else
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    outputSamples.Add(0f);
                }
            }

            hr = releaseBuffer(captureClient, framesToRead);
            if (hr < 0)
            {
                return hr;
            }

            hr = getNextPacketSize(captureClient, &packetFrames);
            if (hr < 0)
            {
                return hr;
            }
        }

        return 0;
    }

    public static void StopAndReleaseAudioClient(ref nint captureClient, ref nint audioClient)
    {
        if (audioClient != 0)
        {
            var stop = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)audioClient)[11];
            _ = stop(audioClient);
        }

        D3D11NativeMethods.ReleaseCom(ref captureClient);
        D3D11NativeMethods.ReleaseCom(ref audioClient);
    }

    private static void ParseWaveFormat(
        WaveFormatEx* wf,
        out int channels,
        out int sampleRate,
        out bool isFloatFormat,
        out int bitsPerSample)
    {
        channels = Math.Max(1, (int)wf->Channels);
        sampleRate = Math.Max(8000, (int)wf->SamplesPerSec);
        bitsPerSample = wf->BitsPerSample;

        if (wf->FormatTag == WaveFormatIeeeFloat)
        {
            isFloatFormat = true;
        }
        else if (wf->FormatTag == WaveFormatExtensibleTag && wf->CbSize >= 22)
        {
            var ext = (WaveFormatExtensible*)wf;
            isFloatFormat = ext->SubFormat == SubtypeIeeeFloat;
            if (!isFloatFormat && ext->SubFormat != SubtypePcm)
            {
                isFloatFormat = wf->BitsPerSample == 32;
            }
        }
        else
        {
            isFloatFormat = false;
        }
    }

    private static nint* CreateCompletionHandlerVtbl()
    {
        nint* vtbl = (nint*)NativeMemory.Alloc((nuint)(4 * sizeof(nint)));
        vtbl[0] = (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterfaceHandler;
        vtbl[1] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRefHandler;
        vtbl[2] = (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&ReleaseHandler;
        vtbl[3] = (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&ActivateCompletedHandler;
        return vtbl;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterfaceHandler(nint thisPtr, Guid* iid, nint* ppv)
    {
        if (ppv == null)
        {
            return unchecked((int)0x80004003); // E_POINTER
        }

        if (*iid == IidUnknown || *iid == IidActivateCompletionHandler || *iid == IidAgileObject)
        {
            *ppv = thisPtr;
            AddRefInternal((NativeCompletionHandlerState*)thisPtr);
            return 0;
        }

        *ppv = 0;
        return unchecked((int)0x80004002); // E_NOINTERFACE
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRefHandler(nint thisPtr)
    {
        return AddRefInternal((NativeCompletionHandlerState*)thisPtr);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint ReleaseHandler(nint thisPtr)
    {
        return ReleaseInternal((NativeCompletionHandlerState*)thisPtr);
    }

    private static uint AddRefInternal(NativeCompletionHandlerState* state)
    {
        return (uint)Interlocked.Increment(ref state->RefCount);
    }

    private static uint ReleaseInternal(NativeCompletionHandlerState* state)
    {
        int remaining = Interlocked.Decrement(ref state->RefCount);
        if (remaining == 0)
        {
            D3D11NativeMethods.ReleaseCom(ref state->ActivatedAudioClient);
            if (state->SignalEventHandle != 0)
            {
                CloseHandle(state->SignalEventHandle);
                state->SignalEventHandle = 0;
            }

            NativeMemory.Free(state);
        }

        return (uint)Math.Max(0, remaining);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ActivateCompletedHandler(nint thisPtr, nint operation)
    {
        var state = (NativeCompletionHandlerState*)thisPtr;
        if (operation != 0)
        {
            int activateHr = -1;
            nint activatedObj = 0;
            var getResult = (delegate* unmanaged[Stdcall]<nint, int*, nint*, int>)(*(nint**)operation)[3];
            int hr = getResult(operation, &activateHr, &activatedObj);
            state->ActivateHr = hr >= 0 ? activateHr : hr;
            state->ActivatedAudioClient = activatedObj;
        }

        if (state->SignalEventHandle != 0)
        {
            SetEvent(state->SignalEventHandle);
        }

        return 0;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateEventW(
        nint lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        string? lpName);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetEvent(nint hEvent);
}
