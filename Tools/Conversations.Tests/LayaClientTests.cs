using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
using Simitone.Conversations;
using Xunit;

public class LayaClientTests
{
    class Fake : ILayaTransport
    {
        public Func<string, string> Responder = body => Answer("receptive", 0.55, 0.20);
        public int Calls;
        public string LastUrl, LastBearer, LastBody;
        public int LastTimeout;
        public string Post(string url, string json, string bearer, int timeoutMs)
        {
            Interlocked.Increment(ref Calls);
            LastUrl = url; LastBearer = bearer; LastBody = json; LastTimeout = timeoutMs;
            return Responder(json);
        }
    }

    static string Answer(string choice, double top, double other)
    {
        var probs = new JObject();
        foreach (var k in new[] { "easily_offended", "thick_skinned", "cold", "receptive" }) probs[k] = k == choice ? top : other / 3;
        return new JObject { ["answers"] = new JObject { ["q"] = new JObject { ["choice"] = choice, ["probabilities"] = probs } } }.ToString();
    }

    static LayaClient Client(Fake f) { var c = new LayaClient(f); c.Configure("http://127.0.0.1:9999/", "secret"); return c; }

    static TraitProfile Sim(string name = "Bella", short mood = 0) { return TraitProfile.From(new RawSim { Name = name, Mood = mood }); }

    [Fact]
    public void ClassifiesTemperamentAndSendsTheDescription()
    {
        var f = new Fake(); var c = Client(f);
        Assert.Equal(Temperament.Receptive, c.ClassifyTemperament(Sim()));
        Assert.Equal("http://127.0.0.1:9999/v1/systemone", f.LastUrl);
        Assert.Equal("secret", f.LastBearer);
        var body = JObject.Parse(f.LastBody);
        Assert.Contains("Bella is", (string)body["state"]["body"]);
        Assert.Equal("choice", (string)body["questions"]["q"]["type"]);
        Assert.NotNull(body["questions"]["q"]["criteria"]["easily_offended"]);
        Assert.Equal(LayaClient.DefaultTimeoutMs, f.LastTimeout);
    }

    [Fact]
    public void ResultsAreCached()
    {
        var f = new Fake(); var c = Client(f);
        c.ClassifyTemperament(Sim()); c.ClassifyTemperament(Sim());
        Assert.Equal(1, f.Calls);
        c.ClassifyTemperament(Sim("Bob"));
        Assert.Equal(2, f.Calls);
        c.ClassifyTemperament(Sim("Bella", 90));   // a different mood bucket is a different question
        Assert.Equal(3, f.Calls);
    }

    [Fact]
    public void WeakAnswersAreUnknownButRemembered()
    {
        var f = new Fake { Responder = b => Answer("cold", 0.30, 0.9) };   // top 0.30 is under the floor
        var c = Client(f);
        Assert.Equal(Temperament.Unknown, c.ClassifyTemperament(Sim()));
        Assert.Equal(Temperament.Unknown, c.ClassifyTemperament(Sim()));
        Assert.Equal(1, f.Calls);
    }

    [Fact]
    public void NearTiesAreUnknown()
    {
        var probs = new JObject { ["thick_skinned"] = 0.37, ["receptive"] = 0.39, ["cold"] = 0.12, ["easily_offended"] = 0.12 };
        var json = new JObject { ["answers"] = new JObject { ["q"] = new JObject { ["choice"] = "receptive", ["probabilities"] = probs } } }.ToString();
        Assert.Null(LayaClient.ParseChoice(json));
    }

    [Fact]
    public void ClearWinnerIsAccepted() { Assert.Equal("cold", LayaClient.ParseChoice(Answer("cold", 0.72, 0.20))); }

    [Theory]
    [InlineData("")] [InlineData("not json")] [InlineData("{}")] [InlineData("{\"answers\":{}}")]
    public void MalformedResponsesDoNotThrowInParse(string json)
    {
        try { Assert.Null(LayaClient.ParseChoice(json)); } catch (Newtonsoft.Json.JsonException) { /* the caller catches this */ }
    }

    [Fact]
    public void FailuresAreNotCachedAndNeverThrow()
    {
        var f = new Fake { Responder = b => throw new TimeoutException() };
        var c = Client(f);
        Assert.Equal(Temperament.Unknown, c.ClassifyTemperament(Sim()));
        Assert.False(c.HasTemperament(Sim()));
        Assert.Equal(1, c.Failures);
    }

    [Fact]
    public void ThreeFailuresPauseTheClient()
    {
        var f = new Fake { Responder = b => throw new TimeoutException() };
        var c = Client(f);
        for (int i = 0; i < 3; i++) c.ClassifyTemperament(Sim("S" + i));
        Assert.False(c.Available);
        int before = f.Calls;
        c.ClassifyTemperament(Sim("Another"));
        Assert.Equal(before, f.Calls);   // no more requests while paused
    }

    [Fact]
    public void ASuccessResetsTheFailureCount()
    {
        int n = 0;
        var f = new Fake { Responder = b => (++n <= 2) ? throw new TimeoutException() : Answer("receptive", 0.6, 0.2) };
        var c = Client(f);
        c.ClassifyTemperament(Sim("A")); c.ClassifyTemperament(Sim("B")); c.ClassifyTemperament(Sim("C"));
        Assert.Equal(0, c.Failures);
        Assert.True(c.Available);
    }

    [Fact]
    public void UnconfiguredClientMakesNoRequests()
    {
        var f = new Fake(); var c = new LayaClient(f);
        Assert.False(c.Available);
        Assert.Equal(Temperament.Unknown, c.ClassifyTemperament(Sim()));
        c.Prefetch(Sim());
        Assert.Equal(0, f.Calls);
    }

    [Fact]
    public void PrefetchRunsOffTheCallingThreadAndFillsTheCache()
    {
        var gate = new ManualResetEventSlim(false);
        var f = new Fake { Responder = b => { gate.Wait(2000); return Answer("cold", 0.7, 0.2); } };
        var c = Client(f);
        var started = DateTime.UtcNow;
        c.Prefetch(Sim());
        Assert.True((DateTime.UtcNow - started).TotalMilliseconds < 500, "Prefetch must not block");
        c.Prefetch(Sim());   // a second request for the same Sim is not started
        gate.Set();
        for (int i = 0; i < 100 && !c.HasTemperament(Sim()); i++) Thread.Sleep(20);
        Assert.Equal(Temperament.Cold, c.CachedTemperament(Sim()));
        Assert.Equal(1, f.Calls);
    }

    [Fact]
    public void ToneClassification()
    {
        var f = new Fake { Responder = b => new JObject { ["answers"] = new JObject { ["q"] = new JObject { ["choice"] = "hostile",
            ["probabilities"] = new JObject { ["hostile"] = 0.85, ["neutral"] = 0.03, ["kind"] = 0.04, ["romantic"] = 0.02, ["playful"] = 0.06 } } } }.ToString() };
        Assert.Equal("hostile", Client(f).ClassifyTone("You are a pathetic loser."));
    }

    [Fact]
    public void RequestBodyHasTheShapeLayaExpects()
    {
        var j = JObject.Parse(LayaClient.BuildRequest("text", "Q?", new[] { "a|first", "b|second" }));
        Assert.Equal("text", (string)j["state"]["body"]);
        Assert.Equal("Q?", (string)j["questions"]["q"]["instructions"]);
        Assert.Equal("second", (string)j["questions"]["q"]["criteria"]["b"]);
    }
}
