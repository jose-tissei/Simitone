using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// simtool: drive ONLY the Simitone window (launch, capture, click, keys). It never reads or
// clicks anything outside that window: capture is PrintWindow on its handle, input is posted
// as window messages to its handle.
static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
}

static class Program
{
    const string ProcName = "Simitone.Windows";
    const string AppDir = @"C:\Users\Zé\.t3\projects\the-sims-run\app";

    static int Main(string[] a)
    {
        Native.SetProcessDPIAware();
        if (a.Length == 0) return Usage();
        try
        {
            switch (a[0])
            {
                case "launch": return Launch(a.Skip(1).ToArray());
                case "kill": Kill(); return 0;
                case "status": return Status();
                case "shot": return Shot(a.Length > 1 ? a[1] : "shot.png");
                case "click": return Click(a, 0x0201, 0x0202, 1);
                case "rclick": return Click(a, 0x0204, 0x0205, 2);
                case "move": return Move(a);
                case "key": return Key(a[1], a.Length > 2 ? int.Parse(a[2]) : 80);
                case "wait": Thread.Sleep((int)(double.Parse(a[1]) * 1000)); return 0;
                default: return Usage();
            }
        }
        catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); return 1; }
    }

    static int Usage()
    {
        Console.WriteLine("simtool launch [--3d] | kill | status | shot [out.png] | click X Y | rclick X Y | move X Y | key NAME [holdMs] | wait SECONDS");
        Console.WriteLine("X,Y are client pixels of the Simitone window. key: enter,esc,space,tab,left,right,up,down,f1-f12,a-z,0-9");
        return 2;
    }

    static Process Proc() => Process.GetProcessesByName(ProcName).FirstOrDefault();

    static IntPtr Hwnd()
    {
        var p = Proc() ?? throw new Exception("Simitone is not running (use: simtool launch)");
        p.Refresh();
        if (p.MainWindowHandle == IntPtr.Zero || !Native.IsWindow(p.MainWindowHandle)) throw new Exception("Simitone has no window yet");
        return p.MainWindowHandle;
    }

    static void Kill() { foreach (var p in Process.GetProcessesByName(ProcName)) { p.Kill(); p.WaitForExit(5000); } }

    static int Launch(string[] args)
    {
        Kill();
        Thread.Sleep(1000);
        var psi = new ProcessStartInfo(Path.Combine(AppDir, ProcName + ".exe"), string.Join(" ", args.Where(x => x == "--3d").Select(_ => "-3d")))
        { WorkingDirectory = AppDir, UseShellExecute = false };
        Process.Start(psi);
        for (int i = 0; i < 60; i++)
        {
            Thread.Sleep(1000);
            var p = Proc();
            if (p != null) { p.Refresh(); if (p.MainWindowHandle != IntPtr.Zero) { Console.WriteLine("launched, window ready"); return 0; } }
        }
        Console.Error.WriteLine("timed out waiting for window");
        return 1;
    }

    static int Status()
    {
        var p = Proc();
        if (p == null) { Console.WriteLine("not running"); return 1; }
        p.Refresh();
        Native.GetClientRect(p.MainWindowHandle, out var r);
        Console.WriteLine($"running pid={p.Id} responding={p.Responding} client={r.R}x{r.B}");
        return 0;
    }

    static int Shot(string path)
    {
        var h = Hwnd();
        Bitmap bmp;
        try { bmp = WgcCapture.Capture(h); }
        catch (Exception e) { Console.Error.WriteLine("graphics-capture failed (" + e.Message + "); falling back to PrintWindow"); bmp = PrintShot(h); }
        using (bmp)
        {
            bmp.Save(path, ImageFormat.Png);
            bool blank = IsBlank(bmp);
            Console.WriteLine($"{(blank ? "BLANK " : "")}{bmp.Width}x{bmp.Height} -> {Path.GetFullPath(path)}");
            return blank ? 3 : 0;
        }
    }

    static Bitmap PrintShot(IntPtr h)
    {
        Native.GetClientRect(h, out var r);
        var bmp = new Bitmap(r.R, r.B, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var dc = g.GetHdc();
        Native.PrintWindow(h, dc, 3);
        g.ReleaseHdc(dc);
        return bmp;
    }

    // true if a sparse sample of pixels is (almost) a single colour => capture failed
    static bool IsBlank(Bitmap b)
    {
        var first = b.GetPixel(b.Width / 2, b.Height / 2);
        int same = 0, n = 0;
        for (int y = 5; y < b.Height; y += b.Height / 12)
            for (int x = 5; x < b.Width; x += b.Width / 12)
            { n++; var c = b.GetPixel(x, y); if (Math.Abs(c.R - first.R) + Math.Abs(c.G - first.G) + Math.Abs(c.B - first.B) < 6) same++; }
        return same == n;
    }

    static (int, int) Pt(string[] a, int i) => (int.Parse(a[i]), int.Parse(a[i + 1]));

    // ---- real input, guarded. The game reads the real mouse/keyboard and only when focused, so we
    // briefly focus it, verify it is really in front, send input, then give focus back.
    static IntPtr prevFg; static Native.POINT prevCur;

    static void Focus(IntPtr h)
    {
        prevFg = Native.GetForegroundWindow(); Native.GetCursorPos(out prevCur);
        if (prevFg == h) return;
        if (Native.IsIconic(h)) Native.ShowWindow(h, 9);
        uint fgThread = Native.GetWindowThreadProcessId(prevFg, out _);
        uint me = Native.GetCurrentThreadId();
        Native.AttachThreadInput(me, fgThread, true);
        Native.SetForegroundWindow(h);
        Native.AttachThreadInput(me, fgThread, false);
        for (int i = 0; i < 20 && Native.GetForegroundWindow() != h; i++) Thread.Sleep(50);
        Thread.Sleep(150);
        if (Native.GetForegroundWindow() != h) throw new Exception("could not bring Simitone to the front; no input sent");
    }

    static void Restore(IntPtr h)
    {
        Native.SetCursorPos(prevCur.X, prevCur.Y);
        if (prevFg != IntPtr.Zero && prevFg != h) Native.SetForegroundWindow(prevFg);
    }

    static bool IsGame(IntPtr h, Native.POINT sp) => Native.GetAncestor(Native.WindowFromPoint(sp), 2) == h;

    static int Click(string[] a, uint down, uint up, int btn)
    {
        var h = Hwnd(); var (x, y) = Pt(a, 1);
        Focus(h);
        try
        {
            var sp = new Native.POINT { X = x, Y = y };
            Native.ClientToScreen(h, ref sp);
            Native.SetCursorPos(sp.X, sp.Y); Thread.Sleep(150);
            if (!IsGame(h, sp)) throw new Exception("point is not on the Simitone window (something is covering it); no click sent");
            Native.mouse_event(btn == 1 ? 0x0002u : 0x0008u, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(80);
            Native.mouse_event(btn == 1 ? 0x0004u : 0x0010u, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(150);
        }
        finally { Restore(h); }
        return 0;
    }

    static int Move(string[] a)
    {
        var h = Hwnd(); var (x, y) = Pt(a, 1);
        Focus(h);
        try
        {
            var sp = new Native.POINT { X = x, Y = y }; Native.ClientToScreen(h, ref sp);
            Native.SetCursorPos(sp.X, sp.Y); Thread.Sleep(300);
        }
        finally { Restore(h); }
        return 0;
    }

    static readonly Dictionary<string, int> Vk = new()
    {
        ["enter"] = 0x0D, ["esc"] = 0x1B, ["space"] = 0x20, ["tab"] = 0x09, ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["pageup"] = 0x21, ["pagedown"] = 0x22, ["backspace"] = 0x08, ["delete"] = 0x2E,
    };

    static int Key(string name, int holdMs = 80)
    {
        var h = Hwnd();
        name = name.ToLowerInvariant();
        int vk;
        if (Vk.TryGetValue(name, out var v)) vk = v;
        else if (name.Length == 1 && char.IsLetter(name[0])) vk = char.ToUpperInvariant(name[0]);
        else if (name.Length == 1 && char.IsDigit(name[0])) vk = name[0];
        else if (name.StartsWith("f") && int.TryParse(name[1..], out var f) && f is >= 1 and <= 12) vk = 0x6F + f;
        else throw new Exception("unknown key " + name);
        byte sc = (byte)Native.MapVirtualKey((uint)vk, 0);
        Focus(h);
        try
        {
            if (Native.GetForegroundWindow() != h) throw new Exception("Simitone lost focus; no key sent");
            bool ext = vk is 0x25 or 0x26 or 0x27 or 0x28 or 0x21 or 0x22 or 0x2E;
            Native.keybd_event((byte)vk, sc, ext ? 1u : 0u, UIntPtr.Zero); Thread.Sleep(holdMs);
            Native.keybd_event((byte)vk, sc, (ext ? 1u : 0u) | 2u, UIntPtr.Zero); Thread.Sleep(100);
        }
        finally { Restore(h); }
        return 0;
    }
}
