using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

// Loaded into Simitone by InputHookPatch. Lets a test driver (simtool) supply synthetic mouse and
// keyboard state over a per-user named pipe, so the game can be driven while it is in the
// background and without touching the real mouse/keyboard. Inactive unless "enable" is received,
// and it switches itself off after IdleSeconds without commands.
public static class SimTestHook
{
    const string PipeName = "simitone-test";
    const int IdleSeconds = 60;

    static readonly object L = new object();
    static bool enabled;
    static DateTime lastCmd = DateTime.MinValue;
    static int mx, my, wheel;
    static bool left, middle, right;
    static readonly HashSet<Keys> keys = new HashSet<Keys>();
    static Thread server;

    static SimTestHook() { Start(); }

    static void Start()
    {
        if (server != null) return;
        server = new Thread(Serve) { IsBackground = true, Name = "SimTestHook" };
        server.Start();
    }

    static bool Enabled
    {
        get
        {
            lock (L)
            {
                if (enabled && (DateTime.UtcNow - lastCmd).TotalSeconds > IdleSeconds) Reset();
                return enabled;
            }
        }
    }

    static void Reset() { enabled = false; left = middle = right = false; keys.Clear(); wheel = 0; }

    // ---- replacements for Mouse.GetState(), Keyboard.GetState(), Game.IsActive
    public static MouseState Mouse()
    {
        GameControl.Drain(); // run queued game-control commands on the game thread
        var real = Microsoft.Xna.Framework.Input.Mouse.GetState();
        if (!Enabled) return real;
        lock (L)
            return new MouseState(mx, my, wheel, left ? ButtonState.Pressed : ButtonState.Released,
                middle ? ButtonState.Pressed : ButtonState.Released, right ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released);
    }

    public static KeyboardState Keyboard()
    {
        if (!Enabled) return Microsoft.Xna.Framework.Input.Keyboard.GetState();
        lock (L) return new KeyboardState(keys.ToArray());
    }

    // game-control commands need the game to keep updating while it is in the background, without taking over the mouse
    static long activeUntilTicks;
    public static void KeepActive(int seconds) { Interlocked.Exchange(ref activeUntilTicks, DateTime.UtcNow.AddSeconds(seconds).Ticks); }

    public static bool IsActive(Game g) => Enabled || g.IsActive || DateTime.UtcNow.Ticks < Interlocked.Read(ref activeUntilTicks);

    // ---- pipe server: one text command per line, "ok" or "err ..." back
    static void Serve()
    {
        var me = WindowsIdentity.GetCurrent().User;
        var sec = new PipeSecurity();
        sec.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
        while (true)
        {
            try
            {
                using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, sec))
                {
                    pipe.WaitForConnection();
                    using (var r = new StreamReader(pipe))
                    using (var w = new StreamWriter(pipe) { AutoFlush = true })
                    {
                        string line;
                        while ((line = r.ReadLine()) != null)
                        {
                            string reply;
                            try { reply = Handle(line); } catch (Exception e) { reply = "err " + e.Message; }
                            w.WriteLine(reply);
                        }
                    }
                }
            }
            catch { Thread.Sleep(500); }
        }
    }

    static string Handle(string line)
    {
        var p = line.Split(' ');
        if (GameControl.Verbs.Contains(p[0]))
        {
            lock (L) lastCmd = DateTime.UtcNow;
            KeepActive(8);
            return GameControl.Run(p); // must not hold L here: the game thread takes it whenever it reads input
        }
        lock (L)
        {
            lastCmd = DateTime.UtcNow;
            switch (p[0])
            {
                case "ping": return "ok enabled=" + enabled;
                case "enable": enabled = true; return "ok";
                case "disable": Reset(); return "ok";
                case "mouse": mx = int.Parse(p[1]); my = int.Parse(p[2]); return "ok";
                case "down": SetBtn(p[1], true); return "ok";
                case "up": SetBtn(p[1], false); return "ok";
                case "wheel": wheel += int.Parse(p[1]); return "ok";
                case "keydown": keys.Add(ParseKey(p[1])); return "ok";
                case "keyup": keys.Remove(ParseKey(p[1])); return "ok";
                default: return "err unknown command";
            }
        }
    }

    static void SetBtn(string b, bool v) { if (b == "l") left = v; else if (b == "r") right = v; else if (b == "m") middle = v; else throw new Exception("button l|r|m"); }

    static Keys ParseKey(string n)
    {
        Keys k;
        if (Enum.TryParse(n, true, out k)) return k;
        throw new Exception("unknown key " + n);
    }
}
