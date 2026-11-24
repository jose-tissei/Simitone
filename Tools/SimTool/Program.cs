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
    static bool Real;
    const string ProcName = "Simitone.Windows";
    const string AppDir = @"C:\Users\Zé\.t3\projects\the-sims-run\app";

    static int Main(string[] a)
    {
        Native.SetProcessDPIAware();
        if (a.Contains("--real")) { Real = true; a = a.Where(x => x != "--real").ToArray(); }
        if (a.Length == 0) return Usage();
        try
        {
            switch (a[0])
            {
                case "launch": return Launch(a.Skip(1).ToArray());
                case "kill": Kill(); return 0;
                case "status": return Status();
                case "shot": return Shot(a.Length > 1 ? a[1] : "shot.png");
                case "record": { Recorder.Record(Hwnd(), a[1], double.Parse(a[2]), a.Length > 3 ? int.Parse(a[3]) : 30); Console.WriteLine("recorded " + Path.GetFullPath(a[1])); return 0; }
                case "click": return Real ? ClickReal(a, 1) : HookClick(a, "l");
                case "rclick": return Real ? ClickReal(a, 2) : HookClick(a, "r");
                case "move": return Real ? MoveReal(a) : HookMove(a);
                case "drag": return HookDrag(a);
                case "ctrlclick": { var (cx, cy) = Pt(a, 1); return HookCmds(new[] { "enable", "keydown LeftControl", $"mouse {cx} {cy}", "sleep 150", "down l", "sleep 150", "up l", "sleep 100", "keyup LeftControl", "sleep 200" }, true); }
                case "wheel": return HookCmds(new[] { "enable", "wheel " + a[1] }, true);
                case "enable": return HookCmds(new[] { "enable" }, false);
                case "disable": return HookCmds(new[] { "disable" }, false);
                case "ping": return HookCmds(new[] { "ping" }, false);
                // game control through the hook (no clicks): see README "Game control"
                case "sims": case "menu": case "interact": case "rel": case "dialog": case "answer": case "speed": case "global": case "time": case "objs": case "attr": case "persona": case "setrel": case "motives": case "focus": case "convo": case "aa": case "render": case "gotolot": case "families": case "playhouse": case "lotinfo": case "alerts": case "dismiss": case "camera": case "lotname":
                    return HookQuery(string.Join(" ", a));
                case "key": return Real ? KeyReal(a[1], a.Length > 2 ? int.Parse(a[2]) : 80) : HookKey(a[1], a.Length > 2 ? int.Parse(a[2]) : 80);
                case "wait": Thread.Sleep((int)(double.Parse(a[1]) * 1000)); return 0;
                default: return Usage();
            }
        }
        catch (Exception e) { Console.Error.WriteLine("error: " + e.Message); return 1; }
    }

    static int Usage()
    {
        Console.WriteLine("simtool launch [--3d] | kill | status | shot [out.png] | click X Y | rclick X Y | move X Y | drag X1 Y1 X2 Y2 | ctrlclick X Y | wheel N | key NAME [holdMs] | wait SECONDS | ping | disable");
        Console.WriteLine("input goes through the in-game hook (works in the background); add --real to use focus-based real input instead");
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
        { WorkingDirectory = AppDir, UseShellExecute = true }; // shell-execute so the game does not inherit our stdout (would hang callers that pipe us)
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


    // ---- in-game hook (SimTestHook): synthetic input over a per-user named pipe, no focus needed
    static System.IO.Pipes.NamedPipeClientStream OpenPipe()
    {
        var c = new System.IO.Pipes.NamedPipeClientStream(".", "simitone-test", System.IO.Pipes.PipeDirection.InOut);
        try { c.Connect(2000); } catch { throw new Exception("input hook not reachable: is Simitone running with the hook installed? (or use --real)"); }
        return c;
    }

    static int HookCmds(string[] cmds, bool disableAfter, int gapMs = 0)
    {
        using var c = OpenPipe();
        var w = new StreamWriter(c, new System.Text.UTF8Encoding(false), 1024, true) { AutoFlush = true };
        var r = new StreamReader(c, System.Text.Encoding.UTF8, false, 1024, true);
        foreach (var cmd in cmds)
        {
            if (cmd.StartsWith("sleep ")) { Thread.Sleep(int.Parse(cmd[6..])); continue; }
            w.WriteLine(cmd);
            var reply = r.ReadLine();
            if (reply == null || !reply.StartsWith("ok")) throw new Exception($"hook rejected '{cmd}': {reply}");
            if (cmd == "ping") Console.WriteLine(reply);
            if (gapMs > 0) Thread.Sleep(gapMs);
        }
        if (disableAfter) { w.WriteLine("disable"); r.ReadLine(); }
        return 0;
    }

    // one command, reply printed (without the leading "ok"); an "err" reply fails with exit code 1
    static int HookQuery(string cmd)
    {
        using var c = OpenPipe();
        var w = new StreamWriter(c, new System.Text.UTF8Encoding(false), 1024, true) { AutoFlush = true };
        var r = new StreamReader(c, System.Text.Encoding.UTF8, false, 1024, true);
        w.WriteLine(cmd);
        var reply = r.ReadLine() ?? "err no reply";
        if (reply.StartsWith("ok")) { Console.WriteLine(reply.Length > 3 ? reply[3..] : ""); return 0; }
        Console.Error.WriteLine(reply);
        return 1;
    }

    static int HookClick(string[] a, string btn)
    {
        var (x, y) = Pt(a, 1);
        return HookCmds(new[] { "enable", $"mouse {x} {y}", "sleep 150", $"down {btn}", "sleep 150", $"up {btn}", "sleep 200" }, true);
    }

    static int HookMove(string[] a)
    {
        var (x, y) = Pt(a, 1);
        return HookCmds(new[] { "enable", $"mouse {x} {y}", "sleep 300" }, false); // stays active (hover) until "disable" or idle timeout
    }

    static int HookDrag(string[] a)
    {
        var (x1, y1) = Pt(a, 1); var (x2, y2) = Pt(a, 3);
        var cmds = new List<string> { "enable", $"mouse {x1} {y1}", "sleep 150", "down l", "sleep 100" };
        for (int i = 1; i <= 10; i++) { cmds.Add($"mouse {x1 + (x2 - x1) * i / 10} {y1 + (y2 - y1) * i / 10}"); cmds.Add("sleep 40"); }
        cmds.AddRange(new[] { "sleep 100", "up l", "sleep 200" });
        return HookCmds(cmds.ToArray(), true);
    }

    static int HookKey(string name, int holdMs)
    {
        var k = HookKeyName(name);
        return HookCmds(new[] { "enable", "sleep 100", $"keydown {k}", $"sleep {holdMs}", $"keyup {k}", "sleep 150" }, true);
    }

    // simtool key names -> XNA Keys enum names
    static string HookKeyName(string n)
    {
        n = n.ToLowerInvariant();
        return n switch
        {
            "enter" => "Enter", "esc" => "Escape", "space" => "Space", "tab" => "Tab", "backspace" => "Back", "delete" => "Delete",
            "left" => "Left", "right" => "Right", "up" => "Up", "down" => "Down", "pageup" => "PageUp", "pagedown" => "PageDown",
            _ when n.Length == 1 && char.IsLetter(n[0]) => n.ToUpperInvariant(),
            _ when n.Length == 1 && char.IsDigit(n[0]) => "D" + n,
            _ when n.StartsWith("f") && int.TryParse(n[1..], out _) => "F" + n[1..],
            _ => throw new Exception("unknown key " + n),
        };
    }

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

    static int ClickReal(string[] a, int btn)
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

    static int MoveReal(string[] a)
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

    static int KeyReal(string name, int holdMs = 80)
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
