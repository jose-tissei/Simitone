using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using FSO.SimAntics;

namespace Simitone.Conversations
{
    // Entry points called from the patched game (see Tools/SpriteCardsPatch):
    //   Boot.Startup(gameDir)   from the game's Main via SpriteCards.Startup: loads settings and the bank, starts Laya in the background
    //   Boot.OnLotReady(vm)     at the end of VMTS1Activator.LoadFromIff: installs the conversation step into the lot's scripts
    // Nothing here may stop the game from running: every entry point swallows its own errors.
    public static class Boot
    {
        public static DialogueBank Bank = new DialogueBank();
        public static readonly LayaClient Laya = new LayaClient(new HttpLayaTransport());
        // requests run on a worker thread, never on the game thread, so they may take longer than the 150 ms planning figure
        static Boot() { Laya.TimeoutMs = 3000; }
        public static LayaHost Host;
        public static string GameDir = ".";
        public static string LastDecision = "";
        public static int ForceOutcome;          // test aid: when 1..3 the outcome is forced (simtool convo force N)
        public static int AutoTone = -1;         // test aid: when 0..2 the dialog is skipped and this tone is used
        public static bool TreatAsAutonomous;    // test aid: every Sim counts as not player-controlled (the game's command checks stop tests from queueing for the other Sim)
        static bool enabled = true;
        static bool started;

        // same folder the game uses for its settings (DocumentsSimitone); no game types here because this runs before MonoGame is linked
        static string ProfilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Simitone", "conversations.profile"); } }

        public static bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                try { File.WriteAllText(ProfilePath, value ? "on" : "off"); } catch { }
                if (value) EnsureLaya();
            }
        }

        public static void StartupCore(string gameDir)
        {
            if (started) return;
            started = true;
            try
            {
                GameDir = string.IsNullOrEmpty(gameDir) ? "." : gameDir;
                try { enabled = !(File.Exists(ProfilePath) && File.ReadAllText(ProfilePath).Trim().ToLowerInvariant() == "off"); } catch { }
                Bank = DialogueBank.Load(Path.Combine(GameDir, "Content", "conversations", "bank.json"));
                Log("startup: enabled=" + enabled + ", bank " + (Bank.Loaded ? "loaded" : "built-in lines only"));
                AppDomain.CurrentDomain.FirstChanceException += (s, e) => { try { var st = e.Exception.StackTrace ?? ""; if (st.Contains("Conversations") || (e.Exception.Message ?? "").Contains("Conversations")) Log("first-chance: " + e.Exception.GetType().Name + ": " + e.Exception.Message + " @ " + st.Split((char)10)[0].Trim()); } catch { } };
                AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { if (Host != null) Host.Stop(); } catch { } try { WriteMisses(); } catch { } };
                if (enabled) EnsureLaya();
            }
            catch (Exception e) { Log("startup failed: " + e.Message); }
        }

        // lines the bank lacked during this run, for `convobank` to fill later (offline; nothing is generated while playing)
        static void WriteMisses()
        {
            var m = Bank.Misses;
            if (m.Count == 0) return;
            var dir = Path.Combine(GameDir, "Content", "conversations");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "missing.txt"), m);
        }

        static int layaStarting;

        // Laya comes with the game: start our own server in the background. SIMITONE_LAYA_URL (and _KEY) point at an
        // already running one instead (development and tests); the game never reuses a server it did not start otherwise.
        public static void EnsureLaya()
        {
            if (Laya.Configured || Interlocked.Exchange(ref layaStarting, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var url = Environment.GetEnvironmentVariable("SIMITONE_LAYA_URL");
                    if (!string.IsNullOrEmpty(url))
                    {
                        Laya.Configure(url, Environment.GetEnvironmentVariable("SIMITONE_LAYA_KEY"));
                        Log("laya: using external server " + url);
                        return;
                    }
                    var host = new LayaHost();
                    Host = host;
                    if (host.Start(GameDir)) { Laya.Configure(host.BaseUrl, host.ApiKey); Log("laya: " + host.Status); }
                    else Log("laya: not available (" + host.Status + "); conversations use the game's own outcomes");
                }
                catch (Exception e) { Log("laya start failed: " + e.Message); }
                finally { Interlocked.Exchange(ref layaStarting, 0); }
            });
        }

        public static void OnContextReady(VMContext ctx)
        {
            try { ConvoPrimitive.Install(ctx); }
            catch (Exception e) { Log("install failed: " + e.Message); }
        }

        public static void Log(string msg)
        {
            try { File.AppendAllText(Path.Combine(GameDir, "Content", "conversations.log"), DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine); }
            catch { }
        }

        // ---- test and diagnostics commands (simtool convo ...) ----
        public static string Command(string[] a)
        {
            var cmd = a.Length > 0 ? a[0] : "status";
            switch (cmd)
            {
                case "status":
                    return "enabled=" + enabled + " laya=" + (Laya.Available ? "up" : (Laya.Configured ? "paused" : "off")) + " bank=" + (Bank.Loaded ? "loaded" : "builtin") +
                           " force=" + ForceOutcome + " autotone=" + AutoTone + " autonomous=" + TreatAsAutonomous + " installed=" + ConvoPrimitive.InstalledCount + " balloons=" + ConvoOverlay.LastPlacement + " last=[" + LastDecision + "]";
                case "on": Enabled = true; return "enabled=true";
                case "off": Enabled = false; return "enabled=false";
                case "force": ForceOutcome = a.Length > 1 && a[1] != "off" ? int.Parse(a[1]) : 0; return "force=" + ForceOutcome;
                case "tone": AutoTone = a.Length > 1 && a[1] != "off" ? int.Parse(a[1]) : -1; return "autotone=" + AutoTone;
                case "auto": TreatAsAutonomous = a.Length > 1 && a[1] == "on"; return "autonomous=" + TreatAsAutonomous;
                case "last": return LastDecision;
                case "log": try { return string.Join(" | ", File.ReadAllLines(Path.Combine(GameDir, "Content", "conversations.log")).Reverse().Take(8).Reverse()); } catch { return "(no log)"; }
                default: throw new Exception("convo status|on|off|force N|tone N|auto on|off|last|log");
            }
        }
    }
}

namespace Simitone.Conversations
{
    // Called from the game's Main, before MonoGame is linked. This class must not mention any game type: merely
    // reflecting over a type whose signatures use game types would load MonoGame too early and fail.
    public static class Early
    {
        public static void Startup(string gameDir)
        {
            // wait until the game has linked MonoGame, then start; the real work is in a separate method that is only compiled by then
            var t = new System.Threading.Thread(() => Run(gameDir)) { IsBackground = true, Name = "ConversationsStartup" };
            t.Start();
        }

        static void Run(string gameDir)
        {
            for (int i = 0; i < 240; i++)
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) if (a.GetName().Name == "MonoGame.Framework") { Go(gameDir); return; }
                System.Threading.Thread.Sleep(250);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void Go(string gameDir) { Boot.StartupCore(gameDir); }
    }
}
