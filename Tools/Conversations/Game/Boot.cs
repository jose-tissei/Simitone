using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FSO.SimAntics;

namespace Simitone.Conversations
{
    // Entry points called from the patched game (see Tools/SpriteCardsPatch):
    //   Boot.StartupCore(gameDir)           from the game's Main via Early.Startup: loads settings, bank, AI subjects, memory, starts Laya
    //   Boot.OnContextReady(ctx)            at the end of VMContext..ctor: installs conversation primitives into the lot's scripts
    //   Boot.OnTick(vm)                     at the end of VM.InternalTick: drives autonomous parallel room conversations (Social <= 60%)
    //   Boot.FilterPieMenu(list,t,vm,c)     before VMEntity.GetPieMenu returns: restricts adult Sim pie menus to Talk & Romance
    // Nothing here may stop the game from running: every entry point swallows its own errors.
    public static class Boot
    {
        public static DialogueBank Bank = new DialogueBank();
        public static AiSubjectBank Subjects = AiSubjectBank.Builtin();
        public static SocialMemory Memory = new SocialMemory();
        public static readonly LayaClient Laya = new LayaClient(new HttpLayaTransport());
        // requests run on a worker thread, never on the game thread, so they may take longer than the 150 ms planning figure
        static Boot() { Laya.TimeoutMs = 3000; }
        public static LayaHost Host;
        public static string GameDir = ".";
        public static string LastDecision = "";
        public static int ForceOutcome;          // test aid: when 1..3 the outcome is forced (simtool convo force N)
        public static int AutoTone = -1;         // test aid: when 0..2 the dialog is skipped and this tone is used
        public static bool TreatAsAutonomous;    // test aid: every Sim counts as not player-controlled
        // true = the game's own pie menu, false = the simplified one (one Talk bubble, romantic actions under Romance, everything else as before).
        // The Options button changes it and remembers it (conversations.menu); `simtool convo menu full|simple` changes it for this run only.
        public static bool FullMenu;
        public static bool SimpleMenu { get { return !FullMenu; } }
        public static void SetFullMenu(bool full, bool remember)
        {
            FullMenu = full;
            if (remember) { try { File.WriteAllText(MenuPath, full ? "full" : "simple"); } catch { } }
        }
        static bool enabled = true;
        static bool started;

        // same folder the game uses for its settings (Documents\Simitone); no game types here because this runs before MonoGame is linked
        static string UserDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Simitone"); } }
        static string ProfilePath { get { return Path.Combine(UserDir, "conversations.profile"); } }
        static string MenuPath { get { return Path.Combine(UserDir, "conversations.menu"); } }
        static string MemoryPath { get { return Path.Combine(UserDir, "conversations.memory.json"); } }

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
                try { FullMenu = File.Exists(MenuPath) && File.ReadAllText(MenuPath).Trim().ToLowerInvariant() == "full"; } catch { }
                Bank = DialogueBank.Load(Path.Combine(GameDir, "Content", "conversations", "bank.json"));
                Subjects = AiSubjectBank.FromBank(Bank);
                Memory = SocialMemory.Load(MemoryPath);
                Log("startup: enabled=" + enabled + ", bank " + (Bank.Loaded ? "loaded" : "built-in lines only") +
                    ", subjects=" + Subjects.Count + (Subjects.LoadedFromBank ? " (bank)" : " (builtin)") +
                    ", memoryPairs=" + Memory.PairCount);
                AppDomain.CurrentDomain.FirstChanceException += (s, e) => { try { var st = e.Exception.StackTrace ?? ""; if (st.Contains("Conversations") || (e.Exception.Message ?? "").Contains("Conversations")) Log("first-chance: " + e.Exception.GetType().Name + ": " + e.Exception.Message + " @ " + st.Split((char)10)[0].Trim()); } catch { } };
                AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { if (Host != null) Host.Stop(); } catch { } try { Memory.Save(MemoryPath); } catch { } try { WriteMisses(); } catch { } };
                if (enabled) EnsureLaya();
            }
            catch (Exception e) { Log("startup failed: " + e.Message); }
        }

        public static void SaveMemoryAsync()
        {
            ThreadPool.QueueUserWorkItem(_ => { try { Memory.Save(MemoryPath); } catch { } });
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

        public static void OnTick(VM vm)
        {
            try { ParallelSocialDriver.OnTick(vm); }
            catch (Exception e) { Log("parallel tick error: " + e.GetType().Name + ": " + e.Message); }
        }

        public static List<VMPieMenuInteraction> FilterPieMenu(List<VMPieMenuInteraction> raw, VMEntity target, VM vm, VMEntity caller)
        {
            return ConvoPieMenu.Filter(raw, target, vm, caller);
        }

        public static bool MemoryMenuOpen { get { return SimMemoryMenu.IsOpen; } }

        public static void ToggleMemoryMenu()
        {
            try { SimMemoryMenu.Toggle(); }
            catch (Exception e) { Log("memory menu error: " + e.Message); }
        }

        public static void OpenMemoryMenu()
        {
            try { SimMemoryMenu.Open(null); }
            catch (Exception e) { Log("memory menu error: " + e.Message); }
        }

        public static void CloseMemoryMenu()
        {
            try { SimMemoryMenu.Close(); }
            catch { }
        }

        static string OpenLiveMemoryTab(bool expandSwitcher)
        {
            try
            {
                var screen = FSO.Client.UI.Framework.UIScreen.Current;
                if (screen == null) return "no screen";
                var frontend = screen.GetType().GetProperty("Frontend")?.GetValue(screen, null);
                var main = frontend?.GetType().GetField("MainPanel")?.GetValue(frontend)
                           ?? screen.GetType().GetField("MainPanel")?.GetValue(screen);
                if (main == null) return "no main panel";
                // Ensure panel is open in LIVE mode (0)
                var setMode = main.GetType().GetMethod("SetMode");
                var modeField = main.GetType().GetField("Mode");
                var liveVal = modeField != null ? Enum.ToObject(modeField.FieldType, 0) : null;
                if (setMode != null && liveVal != null) setMode.Invoke(main, new[] { liveVal });
                var openMethod = main.GetType().GetMethod("Open");
                if (openMethod != null && openMethod.GetParameters().Length == 0) openMethod.Invoke(main, null);
                var switcher = main.GetType().GetField("Switcher")?.GetValue(main);
                if (switcher == null) return "no switcher";
                if (expandSwitcher)
                {
                    SimMemoryMenu.Close();
                    switcher.GetType().GetMethod("Open")?.Invoke(switcher, null);
                    return "switcher=open";
                }
                switcher.GetType().GetMethod("Select")?.Invoke(switcher, new object[] { 5 });
                return "tab=5 " + SimMemoryMenu.Status();
            }
            catch (Exception e) { return "error: " + (e.InnerException ?? e).Message; }
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
                    return "enabled=" + enabled + " laya=" + (Laya.Available ? "up" : (Laya.Configured ? "paused" : "off")) +
                           " bank=" + (Bank.Loaded ? "loaded" : "builtin") + " subjects=" + Subjects.Count +
                           " menu=" + (FullMenu ? "full" : "simplified") + " parallel=" + ParallelSocialDriver.TotalExchanges +
                           " memPairs=" + Memory.PairCount + " memUi=" + (SimMemoryMenu.IsOpen ? "open" : "closed") +
                           " force=" + ForceOutcome + " autotone=" + AutoTone +
                           " autonomous=" + TreatAsAutonomous + " installed=" + ConvoPrimitive.InstalledCount +
                           " balloons=" + ConvoOverlay.LastPlacement + " last=[" + LastDecision + "]";
                case "on": Enabled = true; return "enabled=true";
                case "off": Enabled = false; return "enabled=false";
                case "force": ForceOutcome = a.Length > 1 && a[1] != "off" ? int.Parse(a[1]) : 0; return "force=" + ForceOutcome;
                case "tone": AutoTone = a.Length > 1 && a[1] != "off" ? int.Parse(a[1]) : -1; return "autotone=" + AutoTone;
                case "auto": TreatAsAutonomous = a.Length > 1 && a[1] == "on"; return "autonomous=" + TreatAsAutonomous;
                case "menu": SetFullMenu(a.Length > 1 && a[1] == "full", false); return "menu=" + (FullMenu ? "full" : "simplified");
                case "parallel": return "count=" + ParallelSocialDriver.TotalExchanges + " last=[" + ParallelSocialDriver.LastParallelDecision + "]";
                case "memory": return "pairs=" + Memory.PairCount + " [" + Memory.Summary() + "]";
                case "memory-ui":
                {
                    var sub = a.Length > 1 ? a[1].ToLowerInvariant() : "open";
                    if (sub == "close" || sub == "off") return SimMemoryMenu.Close();
                    if (sub == "status") return SimMemoryMenu.Status();
                    if (sub == "tab") return OpenLiveMemoryTab(false);
                    if (sub == "switcher") return OpenLiveMemoryTab(true);
                    if (sub == "select") return SimMemoryMenu.SelectKnown(a.Length > 2 ? string.Join(" ", a.Skip(2)) : "");
                    if (sub == "toggle") { SimMemoryMenu.Toggle(); return SimMemoryMenu.Status(); }
                    return SimMemoryMenu.Open(a.Length > 2 ? string.Join(" ", a.Skip(2)) : null);
                }
                case "trigger": return ParallelSocialDriver.ForceTrigger(a.Length > 1 ? a[1] : null, a.Length > 2 ? a[2] : null);
                case "last": return LastDecision;
                case "log": try { return string.Join(" | ", File.ReadAllLines(Path.Combine(GameDir, "Content", "conversations.log")).Reverse().Take(8).Reverse()); } catch { return "(no log)"; }
                default: throw new Exception("convo status|on|off|force N|tone N|auto on|off|menu full|simple|parallel|memory|memory-ui [open|close|tab|switcher|select|status]|trigger A B|last|log");
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
