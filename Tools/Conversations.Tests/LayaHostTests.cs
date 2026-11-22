using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Simitone.Conversations;
using Xunit;

public class LayaHostTests
{
    [Fact]
    public void ServerIsBoundToLocalhostOnlyWithAKeyAndOneModel()
    {
        var psi = LayaHost.BuildStart("python", "-m laya.serve", 4321, "k3y");
        Assert.Equal("127.0.0.1", psi.EnvironmentVariables["LAYA_HOST"]);   // Laya's own default is 0.0.0.0
        Assert.Equal("4321", psi.EnvironmentVariables["LAYA_PORT"]);
        Assert.Equal("k3y", psi.EnvironmentVariables["LAYA_API_KEY"]);
        Assert.Equal("english", psi.EnvironmentVariables["LAYA_MODELS"]);
        Assert.True(psi.CreateNoWindow);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void FreePortIsUsable()
    {
        int port = LayaHost.FreePort();
        Assert.InRange(port, 1024, 65535);
        var l = new TcpListener(IPAddress.Loopback, port);
        l.Start(); l.Stop();
    }

    [Fact]
    public void KeysAreLongRandomAndUrlSafe()
    {
        var a = LayaHost.NewKey(); var b = LayaHost.NewKey();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 30);
        Assert.All(a, ch => Assert.True(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'));
    }

    [Fact]
    public void BundledPythonIsTriedBeforeTheSystemOne()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "layahost-" + Guid.NewGuid().ToString("N"));
        var py = System.IO.Path.Combine(dir, "laya", "python");
        System.IO.Directory.CreateDirectory(py);
        System.IO.File.WriteAllText(System.IO.Path.Combine(py, "python.exe"), "");
        try
        {
            var first = LayaHost.Candidates(dir).First();
            Assert.EndsWith("python.exe", first[0]);
            Assert.Contains(dir, first[0]);
            Assert.Equal("py", LayaHost.Candidates(null).First()[0]);
        }
        finally { System.IO.Directory.Delete(dir, true); }
    }

    [Fact]
    public void ANonexistentGameFolderStillFallsBackToSystemCandidates()
    {
        Assert.Equal(new[] { "py", "python" }, LayaHost.Candidates(@"C:\nope").Select(c => c[0]).ToArray());
    }

    [Fact]
    public void StartFailureLeavesTheHostNotReadyAndDoesNotThrow()
    {
        // a Python that does not exist: Start must give up cleanly (and never start a real server in the test)
        var host = new LayaHost();
        var never = new NeverAnswers();
        bool ok = host.Start(@"C:\nope", never, 1, new[] { new[] { @"C:\no\such\python.exe", "-m laya.serve" } });
        Assert.False(ok);
        Assert.False(host.Ready);
        host.Stop();
    }

    class NeverAnswers : ILayaTransport
    {
        public string Post(string url, string json, string bearer, int timeoutMs) { throw new WebException("refused"); }
    }
}
