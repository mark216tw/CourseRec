using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// Direct3D 11 裝置建立、Staging 紋理映射與 WinRT <see cref="IDirect3DSurface"/> 互通方法。
/// </summary>
public static unsafe partial class D3D11NativeMethods
{
    public const int D3DDriverTypeHardware = 1;
    public const int D3DDriverTypeWarp = 5;
    public const uint D3D11CreateDeviceBgraSupport = 0x20;
    public const uint D3D11SdkVersion = 7;
    public const uint DxgiFormatB8G8R8A8Unorm = 87;
    public const uint D3D11UsageStaging = 3;
    public const uint D3D11CpuAccessRead = 0x20000;
    public const uint D3D11MapRead = 1;
    public const int DxgiErrorDeviceRemoved = unchecked((int)0x887A0005);
    public const int DxgiErrorDeviceReset = unchecked((int)0x887A0007);

    private static readonly Guid IidDxgiDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly Guid IidDirect3DDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IidD3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [StructLayout(LayoutKind.Sequential)]
    internal struct DxgiSampleDesc
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11Texture2DDesc
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public DxgiSampleDesc SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct D3D11MappedSubresource
    {
        public nint PData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [LibraryImport("d3d11.dll")]
    private static partial int D3D11CreateDevice(
        nint pAdapter,
        int driverType,
        nint software,
        uint flags,
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out nint ppDevice,
        out int pFeatureLevel,
        out nint ppImmediateContext);

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice,
        out nint graphicsDevice);

    public static bool TryCreateDirect3DDevice(
        out IDirect3DDevice? winrtDevice,
        out nint d3dDevice,
        out nint d3dContext)
    {
        winrtDevice = null;
        d3dDevice = 0;
        d3dContext = 0;

        int hr = D3D11CreateDevice(
            0,
            D3DDriverTypeHardware,
            0,
            D3D11CreateDeviceBgraSupport,
            0,
            0,
            D3D11SdkVersion,
            out d3dDevice,
            out _,
            out d3dContext);

        if (hr < 0)
        {
            hr = D3D11CreateDevice(
                0,
                D3DDriverTypeWarp,
                0,
                D3D11CreateDeviceBgraSupport,
                0,
                0,
                D3D11SdkVersion,
                out d3dDevice,
                out _,
                out d3dContext);
        }

        if (hr < 0 || d3dDevice == 0)
        {
            return false;
        }

        Guid dxgiIid = IidDxgiDevice;
        nint dxgiDevice = 0;
        var qi = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)d3dDevice)[0];
        hr = qi(d3dDevice, &dxgiIid, &dxgiDevice);
        if (hr < 0 || dxgiDevice == 0)
        {
            ReleaseCom(ref d3dContext);
            ReleaseCom(ref d3dDevice);
            return false;
        }

        try
        {
            hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint inspectableDevice);
            if (hr < 0 || inspectableDevice == 0)
            {
                ReleaseCom(ref d3dContext);
                ReleaseCom(ref d3dDevice);
                return false;
            }

            try
            {
                winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
                return true;
            }
            finally
            {
                ReleaseCom(ref inspectableDevice);
            }
        }
        finally
        {
            ReleaseCom(ref dxgiDevice);
        }
    }

    public static bool IsDeviceLostHresult(int hr)
    {
        return hr == DxgiErrorDeviceRemoved || hr == DxgiErrorDeviceReset;
    }

    public static bool TryCopySurfaceBgraPixels(
        nint d3dDevice,
        nint d3dContext,
        IDirect3DSurface surface,
        ref nint cachedStagingTexture,
        ref int cachedWidth,
        ref int cachedHeight,
        ref byte[] pixelBuffer,
        out int width,
        out int height)
    {
        return TryCopySurfaceBgraPixels(
            d3dDevice,
            d3dContext,
            surface,
            ref cachedStagingTexture,
            ref cachedWidth,
            ref cachedHeight,
            ref pixelBuffer,
            out width,
            out height,
            out _);
    }

    public static bool TryCopySurfaceBgraPixels(
        nint d3dDevice,
        nint d3dContext,
        IDirect3DSurface surface,
        ref nint cachedStagingTexture,
        ref int cachedWidth,
        ref int cachedHeight,
        ref byte[] pixelBuffer,
        out int width,
        out int height,
        out int failureHresult)
    {
        width = 0;
        height = 0;
        failureHresult = 0;

        if (d3dDevice == 0 || d3dContext == 0)
        {
            return false;
        }

        nint srcTexture = GetTextureFromSurface(surface);
        if (srcTexture == 0)
        {
            return false;
        }

        try
        {
            D3D11Texture2DDesc srcDesc;
            var getDesc = (delegate* unmanaged[Stdcall]<nint, D3D11Texture2DDesc*, void>)(*(nint**)srcTexture)[10];
            getDesc(srcTexture, &srcDesc);

            if (srcDesc.Width == 0 || srcDesc.Height == 0)
            {
                return false;
            }

            width = (int)srcDesc.Width;
            height = (int)srcDesc.Height;

            if (cachedStagingTexture == 0 || cachedWidth != width || cachedHeight != height)
            {
                ReleaseCom(ref cachedStagingTexture);

                var stagingDesc = new D3D11Texture2DDesc
                {
                    Width = srcDesc.Width,
                    Height = srcDesc.Height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = DxgiFormatB8G8R8A8Unorm,
                    SampleDesc = new DxgiSampleDesc { Count = 1, Quality = 0 },
                    Usage = D3D11UsageStaging,
                    BindFlags = 0,
                    CpuAccessFlags = D3D11CpuAccessRead,
                    MiscFlags = 0,
                };

                nint newStaging = 0;
                var createTexture2D = (delegate* unmanaged[Stdcall]<nint, D3D11Texture2DDesc*, nint, nint*, int>)(*(nint**)d3dDevice)[5];
                int createHr = createTexture2D(d3dDevice, &stagingDesc, 0, &newStaging);
                if (createHr < 0 || newStaging == 0)
                {
                    failureHresult = createHr;
                    return false;
                }

                cachedStagingTexture = newStaging;
                cachedWidth = width;
                cachedHeight = height;
            }

            // ID3D11DeviceContext::CopyResource (vtable slot 47)
            var copyResource = (delegate* unmanaged[Stdcall]<nint, nint, nint, void>)(*(nint**)d3dContext)[47];
            copyResource(d3dContext, cachedStagingTexture, srcTexture);

            // ID3D11DeviceContext::Map (vtable slot 14) & Unmap (vtable slot 15)
            D3D11MappedSubresource mapped;
            var map = (delegate* unmanaged[Stdcall]<nint, nint, uint, uint, uint, D3D11MappedSubresource*, int>)(*(nint**)d3dContext)[14];
            var unmap = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)(*(nint**)d3dContext)[15];

            int mapHr = map(d3dContext, cachedStagingTexture, 0, D3D11MapRead, 0, &mapped);
            if (mapHr < 0 || mapped.PData == 0)
            {
                failureHresult = mapHr;
                return false;
            }

            try
            {
                int rowBytes = width * 4;
                int totalBytes = rowBytes * height;
                if (pixelBuffer.Length != totalBytes)
                {
                    pixelBuffer = new byte[totalBytes];
                }

                byte* srcPtr = (byte*)mapped.PData;
                fixed (byte* dstPtr = pixelBuffer)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            srcPtr + (y * (int)mapped.RowPitch),
                            dstPtr + (y * rowBytes),
                            rowBytes,
                            rowBytes);
                    }
                }

                return true;
            }
            finally
            {
                unmap(d3dContext, cachedStagingTexture, 0);
            }
        }
        finally
        {
            ReleaseCom(ref srcTexture);
        }
    }

    public static void ReleaseCom(ref nint ptr)
    {
        if (ptr != 0)
        {
            nint copy = ptr;
            ptr = 0;
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)copy)[2];
            release(copy);
        }
    }

    private static nint GetTextureFromSurface(IDirect3DSurface surface)
    {
        if (surface is not IWinRTObject winrtObj)
        {
            return 0;
        }

        int hr = winrtObj.NativeObject.TryAs(IidDirect3DDxgiInterfaceAccess, out nint accessPtr);
        if (hr < 0 || accessPtr == 0)
        {
            return 0;
        }

        try
        {
            Guid texIid = IidD3D11Texture2D;
            nint texPtr = 0;
            var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)accessPtr)[3];
            hr = getInterface(accessPtr, &texIid, &texPtr);
            return hr >= 0 ? texPtr : 0;
        }
        finally
        {
            ReleaseCom(ref accessPtr);
        }
    }
}
