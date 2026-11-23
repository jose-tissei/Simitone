using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Simitone.Conversations
{
    public interface ILayaTransport
    {
        // returns the response body, or throws on any failure (timeout, refused connection, bad status)
        string Post(string url, string json, string bearer, int timeoutMs);
    }

    public class HttpLayaTransport : ILayaTransport
    {
        public string Post(string url, string json, string bearer, int timeoutMs)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Proxy = null;
            if (!string.IsNullOrEmpty(bearer)) req.Headers["Authorization"] = "Bearer " + bearer;
            var data = Encoding.UTF8.GetBytes(json);
            req.ContentLength = data.Length;
            using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }
    }

    // Laya is used only to CLASSIFY text (spike result: it cannot judge reactions, but it labels tone and temperament well):
    //   ClassifyTemperament: "how does this person take jokes and teasing?" from the Sim's description
    //   ClassifyTone: what a line sounds like (hostile, neutral, kind, romantic, playful), used when checking generated lines
    // Nothing here may block the game: results are cached, requests run off the game thread, and failures only
    // switch the feature back to the original behavior for a short while.
    public class LayaClient
    {
        public const int DefaultTimeoutMs = 150;
        public const double MinProbability = 0.35;   // below this, or a near tie, the answer is treated as "unknown"
        public const double MinMargin = 0.05;

        readonly ILayaTransport transport;
        readonly Dictionary<string, Temperament> temperaments = new Dictionary<string, Temperament>();
        readonly HashSet<string> pending = new HashSet<string>();
        readonly object gate = new object();
        string baseUrl, key;
        int failures;
        DateTime pausedUntil = DateTime.MinValue;
        public int TimeoutMs = DefaultTimeoutMs;

        public LayaClient(ILayaTransport transport) { this.transport = transport ?? new HttpLayaTransport(); }

        public void Configure(string baseUrl, string apiKey)
        {
            lock (gate) { this.baseUrl = baseUrl == null ? null : baseUrl.TrimEnd('/'); key = apiKey; failures = 0; pausedUntil = DateTime.MinValue; }
        }

        public bool Configured { get { lock (gate) return baseUrl != null; } }

        public bool Available { get { lock (gate) return baseUrl != null && DateTime.UtcNow >= pausedUntil; } }

        public int Failures { get { lock (gate) return failures; } }
        public string LastError { get; private set; }

        public const string TemperamentQuestion = "Which best describes how this person takes jokes and teasing?";

        public static readonly string[] TemperamentCriteria =
        {
            "easily_offended|takes things personally, gets hurt or angry",
            "thick_skinned|laughs it off, enjoys banter",
            "cold|unresponsive, irritated, not interested",
            "receptive|friendly and open, responds warmly",
        };

        public static string BuildRequest(string text, string question, IEnumerable<string> criteria)
        {
            var crit = new JObject();
            foreach (var c in criteria) { var i = c.IndexOf('|'); crit[c.Substring(0, i)] = c.Substring(i + 1); }
            var body = new JObject
            {
                ["model"] = "english",   // never let Laya route to the multilingual checkpoint (newer versions do by default; the bundle has only this one)
                ["state"] = new JObject { ["body"] = text },
                ["questions"] = new JObject { ["q"] = new JObject { ["type"] = "choice", ["instructions"] = question, ["criteria"] = crit } },
            };
            return body.ToString(Newtonsoft.Json.Formatting.None);
        }

        // picks the winner only when it is clear; returns null when the answer is too weak to trust
        public static string ParseChoice(string response)
        {
            var q = JObject.Parse(response)["answers"]?["q"];
            if (q == null) return null;
            var probs = q["probabilities"] as JObject;
            var choice = (string)q["choice"];
            if (probs == null || choice == null) return null;
            double top = -1, second = -1; string topKey = null;
            foreach (var p in probs.Properties())
            {
                double v = (double)p.Value;
                if (v > top) { second = top; top = v; topKey = p.Name; } else if (v > second) second = v;
            }
            if (topKey != choice || top < MinProbability || top - second < MinMargin) return null;
            return choice;
        }

        // returns all choice probabilities from a Laya /v1/systemone response, or null if malformed
        public static Dictionary<string, double> ParseProbabilities(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return null;
            try
            {
                var q = JObject.Parse(response)["answers"]?["q"];
                var probs = q?["probabilities"] as JObject;
                if (probs == null) return null;
                var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in probs.Properties()) map[p.Name] = (double)p.Value;
                return map.Count > 0 ? map : null;
            }
            catch { return null; }
        }

        // asks Laya to score all criteria and returns the full probability distribution, or null if unavailable/failed
        public Dictionary<string, double> ScoreChoices(string text, string question, IEnumerable<string> criteria)
        {
            string url, bearer;
            lock (gate) { if (baseUrl == null || DateTime.UtcNow < pausedUntil) return null; url = baseUrl + "/v1/systemone"; bearer = key; }
            try
            {
                var resp = transport.Post(url, BuildRequest(text, question, criteria), bearer, TimeoutMs);
                lock (gate) failures = 0;
                return ParseProbabilities(resp);
            }
            catch (Exception e)
            {
                lock (gate)
                {
                    LastError = e.GetType().Name + ": " + e.Message;
                    failures++;
                    if (failures >= 3) pausedUntil = DateTime.UtcNow.AddSeconds(30);
                }
                return null;
            }
        }

        string Ask(string text, string question, IEnumerable<string> criteria, out bool answered)
        {
            answered = false;
            string url, bearer;
            lock (gate) { if (baseUrl == null || DateTime.UtcNow < pausedUntil) return null; url = baseUrl + "/v1/systemone"; bearer = key; }
            try
            {
                var resp = transport.Post(url, BuildRequest(text, question, criteria), bearer, TimeoutMs);
                lock (gate) failures = 0;
                answered = true;
                return ParseChoice(resp);
            }
            catch (Exception e)
            {
                lock (gate)
                {
                    LastError = e.GetType().Name + ": " + e.Message;
                    failures++;
                    if (failures >= 3) pausedUntil = DateTime.UtcNow.AddSeconds(30);   // stop hammering a dead server
                }
                return null;
            }
        }

        public Temperament CachedTemperament(TraitProfile p)
        {
            Temperament t;
            lock (gate) return temperaments.TryGetValue(p.Key, out t) ? t : Temperament.Unknown;
        }

        public bool HasTemperament(TraitProfile p) { lock (gate) return temperaments.ContainsKey(p.Key); }

        // synchronous classification, for the background thread
        public Temperament ClassifyTemperament(TraitProfile p)
        {
            if (HasTemperament(p)) return CachedTemperament(p);
            bool answered;
            var t = Names.ParseTemperament(Ask(p.Summary, TemperamentQuestion, TemperamentCriteria, out answered));
            if (answered) lock (gate) temperaments[p.Key] = t;   // a weak answer is remembered as unknown; a failed request is not
            return t;
        }

        // starts classification on a worker thread if it is not cached or already running
        public void Prefetch(TraitProfile p)
        {
            if (!Available) return;
            lock (gate) { if (temperaments.ContainsKey(p.Key) || !pending.Add(p.Key)) return; }
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try { ClassifyTemperament(p); }
                finally { lock (gate) pending.Remove(p.Key); }
            });
        }

        public string ClassifyTone(string line)
        {
            bool answered;
            return Ask(line, "What is the tone of this sentence?", new[]
            {
                "hostile|insulting, aggressive or mean", "neutral|plain small talk", "kind|friendly, warm, complimenting",
                "romantic|flirting or romantic", "playful|joking or teasing",
            }, out answered);
        }
    }
}
