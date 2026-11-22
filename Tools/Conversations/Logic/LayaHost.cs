using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace Simitone.Conversations
{
    // Starts, warms up and stops the Laya server that ships with the game (CONV-12).
    //  - bound to 127.0.0.1 only, on a free port, with a random API key (Laya listens on 0.0.0.0 by default)
    //  - runs hidden, and is killed with the game even if the game crashes (Windows job object)
    //  - never reuses a server it did not start
    //  - if anything fails the feature simply stays off; the game itself is unaffected
    public class LayaHost : IDisposable
    {
        public string BaseUrl { get; private set; }
        public string ApiKey { get; private set; }
        public bool Ready { get; private set; }
        public string Status { get; private set; } = "not started";

        Process process;
        IntPtr job = IntPtr.Zero;

        // where to look for Python: a copy bundled next to the game first, then the machine's own
        public static IEnumerable<string[]> Candidates(string gameDir)
        {
            if (!string.IsNullOrEmpty(gameDir))
            {
                var bundled = Path.Combine(gameDir, "laya", "python", "python.exe");
                if (File.Exists(bundled)) yield return new[] { bundled, "-m laya.serve" };
            }
            yield return new[] { "py", "-3.11 -m laya.serve" };
            yield return new[] { "python", "-m laya.serve" };
        }

        public static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            try { return ((IPEndPoint)l.LocalEndpoint).Port; } finally { l.Stop(); }
        }

        public static string NewKey()
        {
            var b = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        public static ProcessStartInfo BuildStart(string exe, string args, int port, string key)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.EnvironmentVariables["LAYA_HOST"] = "127.0.0.1";
            psi.EnvironmentVariables["LAYA_PORT"] = port.ToString();
            psi.EnvironmentVariables["LAYA_API_KEY"] = key;
            psi.EnvironmentVariables["LAYA_MODELS"] = "english";
            psi.EnvironmentVariables["LAYA_PRELOAD"] = "1";
            psi.EnvironmentVariables["LAYA_MAX_CONCURRENT"] = "4";
            var root = BundleRoot(exe);
            if (root != null)
            {
                // the copy that ships with the game: its own weights, no network, and nothing from the user's own Python
                psi.EnvironmentVariables["HF_HOME"] = Path.Combine(root, "hf");
                psi.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";
                psi.EnvironmentVariables["PYTHONNOUSERSITE"] = "1";
            }
            return psi;
        }

        // <game>\laya for <game>\laya\python\python.exe, otherwise null (a system Python)
        public static string BundleRoot(string exe)
        {
            try
            {
                var py = Path.GetDirectoryName(exe);
                if (string.IsNullOrEmpty(py) || !string.Equals(Path.GetFileName(py), "python", StringComparison.OrdinalIgnoreCase)) return null;
                var root = Path.GetDirectoryName(py);
                return !string.IsNullOrEmpty(root) && string.Equals(Path.GetFileName(root), "laya", StringComparison.OrdinalIgnoreCase) ? root : null;
            }
            catch { return null; }
        }

        // Runs on a background thread; returns when the server answered a request or gave up.
        public bool Start(string gameDir, ILayaTransport transport = null, int healthTimeoutSeconds = 90, IEnumerable<string[]> candidates = null)
        {
            transport = transport ?? new HttpLayaTransport();
            foreach (var c in candidates ?? Candidates(gameDir))
            {
                try
                {
                    int port = FreePort();
                    var key = NewKey();
                    var p = Process.Start(BuildStart(c[0], c[1], port, key));
                    if (p == null) continue;
                    // drain output so the pipe never fills and stalls the server
                    p.OutputDataReceived += (s, e) => { };
                    p.ErrorDataReceived += (s, e) => { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    process = p;
                    AttachToJob(p);
                    BaseUrl = "http://127.0.0.1:" + port;
                    ApiKey = key;
                    if (WaitHealthy(p, transport, healthTimeoutSeconds)) { Ready = true; Status = "ready on port " + port + " via " + c[0]; return true; }
                    Status = "server did not become healthy (" + c[0] + ")";
                    Stop();
                }
                catch (Exception e) { Status = "could not start " + c[0] + ": " + e.Message; Stop(); }
            }
            return false;
        }

        bool WaitHealthy(Process p, ILayaTransport transport, int seconds)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                if (p.HasExited) return false;
                try
                {
                    // a real (tiny) request: it also loads the model, so the first game request is not slow
                    var body = LayaClient.BuildRequest("Hello there.", "What is the tone of this sentence?", new[] { "neutral|plain", "kind|warm" });
                    transport.Post(BaseUrl + "/v1/systemone", body, ApiKey, 5000);
                    return true;
                }
                catch { Thread.Sleep(500); }
            }
            return false;
        }

        public void Stop()
        {
            Ready = false;
            try { if (process != null && !process.HasExited) process.Kill(); } catch { }
            process = null;
            if (job != IntPtr.Zero) { CloseHandle(job); job = IntPtr.Zero; }
        }

        public void Dispose() { Stop(); }

        // ---- job object: when the game dies for any reason, Windows kills Laya with it ----
        void AttachToJob(Process p)
        {
            try
            {
                job = CreateJobObject(IntPtr.Zero, null);
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    SetInformationJobObject(job, 9, ptr, (uint)len);   // JobObjectExtendedLimitInformation
                }
                finally { Marshal.FreeHGlobal(ptr); }
                AssignProcessToJobObject(job, p.Handle);
            }
            catch { /* best effort: Stop() still kills it on a normal exit */ }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
    }
}
