using System.Diagnostics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

// Records one window to an mp4 through ffmpeg: Windows Graphics Capture frames are held and written at a fixed frame rate,
// so a still screen still produces a steady video. Only the target window's own pixels are captured.
static class Recorder
{
    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInterop { IntPtr CreateForWindow(IntPtr hwnd, in Guid iid); IntPtr CreateForMonitor(IntPtr hmon, in Guid iid); }
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAccess { IntPtr GetInterface(in Guid iid); }

    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr hstring, in Guid iid, out IntPtr factory);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string s, int len, out IntPtr hs);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr inspectableDevice);

    static readonly Guid IidItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    static readonly Guid IidAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    public static void Record(IntPtr hwnd, string outPath, double seconds, int fps)
    {
        WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", 44, out var hs);
        Marshal.ThrowExceptionForHR(RoGetActivationFactory(hs, new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), out var fp));
        var interop = (IInterop)Marshal.GetObjectForIUnknown(fp);
        var item = MarshalInterface<GraphicsCaptureItem>.FromAbi(interop.CreateForWindow(hwnd, IidItem));
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, null, out ID3D11Device dev, out ID3D11DeviceContext ctx).CheckError();
        using var dxgi = dev.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var insp));
        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(insp);
        var size = item.Size;
        int w = size.Width & ~1, h = size.Height & ~1;
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, size);
        using var session = pool.CreateCaptureSession(item);
        try { session.IsCursorCaptureEnabled = false; } catch { }
        session.StartCapture();

        var psi = new ProcessStartInfo("ffmpeg", $"-y -loglevel error -f rawvideo -pix_fmt bgra -s {size.Width}x{size.Height} -r {fps} -i - -vf crop={w}:{h}:0:0 -c:v libx264 -preset veryfast -crf 17 -pix_fmt yuv420p \"{outPath}\"")
        { RedirectStandardInput = true, UseShellExecute = false };
        using var ff = Process.Start(psi)!;
        var stdin = ff.StandardInput.BaseStream;

        ID3D11Texture2D? staging = null;
        byte[]? buf = null;
        var clock = Stopwatch.StartNew();
        long frames = 0, total = (long)(seconds * fps);
        while (frames < total)
        {
            var f = pool.TryGetNextFrame();
            while (f != null)   // keep only the newest
            {
                var next = pool.TryGetNextFrame();
                if (next == null) break;
                f.Dispose(); f = next;
            }
            if (f != null)
            {
                using (f)
                {
                    var surfPtr = MarshalInterface<IDirect3DSurface>.FromManaged(f.Surface);
                    Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surfPtr, in IidAccess, out var accessPtr));
                    var access = (IAccess)Marshal.GetObjectForIUnknown(accessPtr);
                    using var tex = new ID3D11Texture2D(access.GetInterface(typeof(ID3D11Texture2D).GUID));
                    var d = tex.Description;
                    if (staging == null)
                    {
                        staging = dev.CreateTexture2D(d with { Usage = ResourceUsage.Staging, BindFlags = BindFlags.None, CPUAccessFlags = CpuAccessFlags.Read, MiscFlags = ResourceOptionFlags.None });
                        buf = new byte[(int)d.Width * (int)d.Height * 4];
                    }
                    ctx.CopyResource(staging, tex);
                    var map = ctx.Map(staging, 0, MapMode.Read);
                    int rowBytes = (int)d.Width * 4;
                    for (int y = 0; y < (int)d.Height; y++) Marshal.Copy(map.DataPointer + y * (int)map.RowPitch, buf!, y * rowBytes, rowBytes);
                    ctx.Unmap(staging, 0);
                }
            }
            if (buf != null)
            {
                // write as many frames as the clock says are due (holds the last picture when the screen did not change)
                long due = Math.Min(total, (long)(clock.Elapsed.TotalSeconds * fps));
                while (frames < due) { stdin.Write(buf, 0, buf.Length); frames++; }
            }
            Thread.Sleep(4);
        }
        stdin.Close();
        ff.WaitForExit(60000);
    }
}
