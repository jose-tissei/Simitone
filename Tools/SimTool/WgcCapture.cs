using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

// Windows Graphics Capture of a single window handle. Works for GPU-rendered windows, even when
// they are covered by other windows. Only the target window's own pixels are ever captured.
static class WgcCapture
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow(IntPtr hwnd, in Guid iid);
        IntPtr CreateForMonitor(IntPtr hmon, in Guid iid);
    }

    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr hstring, in Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int len, out IntPtr hs);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr hs);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr inspectableDevice);

    static readonly Guid IidInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    static readonly Guid IidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static readonly Guid IidDxgiAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    public static Bitmap Capture(IntPtr hwnd, int timeoutMs = 4000)
    {
        // capture item for the window
        WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", 44, out var hs);
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(hs, IidInterop, out var factoryPtr));
        WindowsDeleteString(hs);
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
        var itemPtr = interop.CreateForWindow(hwnd, IidItem);
        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);

        // D3D11 device + WinRT wrapper
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        using var dxgi = dev.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var inspectable));
        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);

        var size = item.Size;
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, size);
        using var session = pool.CreateCaptureSession(item);
        try { session.IsCursorCaptureEnabled = false; } catch { }
        session.StartCapture();

        Direct3D11CaptureFrame frame = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (frame == null && sw.ElapsedMilliseconds < timeoutMs) { frame = pool.TryGetNextFrame(); if (frame == null) Thread.Sleep(30); }
        if (frame == null) throw new Exception("no frame arrived from the window (is it minimised?)");

        using (frame)
        {
            var surfPtr = MarshalInterface<IDirect3DSurface>.FromManaged(frame.Surface);
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfPtr, in IidDxgiAccess, out var accessPtr));
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPtr);
            var texPtr = access.GetInterface(typeof(ID3D11Texture2D).GUID);
            using var tex = new ID3D11Texture2D(texPtr);
            var d = tex.Description;
            var stagingDesc = d with { Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CPUAccessFlags = CpuAccessFlags.Read, MiscFlags = ResourceOptionFlags.None };
            using var staging = dev.CreateTexture2D(stagingDesc);
            ctx.CopyResource(staging, tex);
            var map = ctx.Map(staging, 0, MapMode.Read);
            var bmp = new Bitmap((int)d.Width, (int)d.Height, PixelFormat.Format32bppArgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            unsafe
            {
                for (int y = 0; y < bmp.Height; y++)
                    Buffer.MemoryCopy((void*)(map.DataPointer + y * (int)map.RowPitch),
                                      (void*)(bd.Scan0 + y * bd.Stride), bd.Stride, bmp.Width * 4);
            }
            bmp.UnlockBits(bd);
            ctx.Unmap(staging, 0);
            return bmp;
        }
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirect3DDxgiInterfaceAccess { IntPtr GetInterface(in Guid iid); }
}
