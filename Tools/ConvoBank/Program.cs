using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Simitone.Conversations;

// Builds the dialogue bank used by the game (Content/conversations/bank.json) with a local Ollama model.
//   convobank --out bank.json [--model gemma3:4b] [--lines 6] [--only hug,kiss] [--laya http://127.0.0.1:8000 --laya-key K] [--review review.md]
// Resumable: groups already in --out are kept and skipped. Every line passes the same filter the game uses
// (length, characters, blocked words, placeholders); with --laya, lines Laya hears as hostile are dropped from friendly interactions.
// Nothing here runs during play: the game only reads the finished file.
static class Program
{
    static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

    static async Task<int> Main(string[] a)
    {
        string Arg(string name, string def = null) { var i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }
        var outPath = Arg("--out", "bank.json");
        var model = Arg("--model", "gemma3:4b");
        var host = Arg("--ollama", "http://localhost:11434");
        int count = int.Parse(Arg("--lines", "6"));
        var only = Arg("--only") == null ? null : Arg("--only").Split(',');
        var reviewPath = Arg("--review");
        var layaUrl = Arg("--laya");
        LayaClient laya = null;
        if (layaUrl != null) { laya = new LayaClient(new HttpLayaTransport()) { TimeoutMs = 5000 }; laya.Configure(layaUrl, Arg("--laya-key")); }

        var bank = File.Exists(outPath) ? JsonConvert.DeserializeObject<BankFile>(File.ReadAllText(outPath)) ?? new BankFile() : new BankFile();
        var review = new StringBuilder("# Bank review sheet\n\nModel: " + model + "\n\n");
        var keys = OutcomeResolver.Interactions.Where(k => only == null || only.Contains(k)).ToList();
        var temps = new[] { Temperament.EasilyOffended, Temperament.ThickSkinned, Temperament.Cold, Temperament.Receptive };
        int calls = 0, kept = 0, dropped = 0;

        foreach (var key in keys)
        {
            // ---- what the speaker says: one list per tone
            if (!bank.Speaker.ContainsKey(key)) bank.Speaker[key] = new Dictionary<string, List<string>>();
            for (int tone = 0; tone < 3; tone++)
            {
                if (bank.Speaker[key].ContainsKey(tone.ToString()) && bank.Speaker[key][tone.ToString()].Count >= 3) continue;
                var lines = await Ask(host, model, Prompts.Speaker(key, tone, count), laya, key, review, $"{key} / {Names.ToneLabels[tone]}");
                calls++; kept += lines.Count;
                bank.Speaker[key][tone.ToString()] = lines;
                Save(outPath, bank);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] speaker {key,-10} {Names.ToneLabels[tone],-8} {lines.Count} lines");
            }

            // ---- what the receiver answers: per quality and temperament. Hostile and special interactions only get neutral replies.
            if (!bank.Reply.ContainsKey(key)) bank.Reply[key] = new Dictionary<string, Dictionary<string, List<string>>>();
            var qualities = OutcomeResolver.Quality(1, key) == 2 && OutcomeResolver.Quality(3, key) == 2 ? new[] { 2 }
                          : OutcomeResolver.MaxOutcome(key) == 2 ? new[] { 1, 3 } : new[] { 1, 2, 3 };
            foreach (var q in qualities)
            {
                if (!bank.Reply[key].ContainsKey(q.ToString())) bank.Reply[key][q.ToString()] = new Dictionary<string, List<string>>();
                foreach (var t in temps)
                {
                    var tk = Names.Key(t);
                    if (bank.Reply[key][q.ToString()].ContainsKey(tk) && bank.Reply[key][q.ToString()][tk].Count >= 3) continue;
                    var lines = await Ask(host, model, Prompts.Reply(key, t, q, count), null, key, review, $"{key} / reply {q} / {tk}");
                    calls++; kept += lines.Count;
                    bank.Reply[key][q.ToString()][tk] = lines;
                    Save(outPath, bank);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] reply   {key,-10} q{q} {tk,-16} {lines.Count} lines");
                }
                // a generic set for Sims whose temperament is not known
                if (!bank.Reply[key][q.ToString()].ContainsKey("any") || bank.Reply[key][q.ToString()]["any"].Count < 3)
                {
                    var lines = await Ask(host, model, Prompts.Reply(key, Temperament.Unknown, q, count), null, key, review, $"{key} / reply {q} / any");
                    calls++; kept += lines.Count;
                    bank.Reply[key][q.ToString()]["any"] = lines;
                    Save(outPath, bank);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] reply   {key,-10} q{q} {"any",-16} {lines.Count} lines");
                }
            }
        }

        // ---- Talk: subject lines per tone, replies per how much the listener likes the subject
        if (only == null || only.Contains("talk"))
        {
            if (!bank.Speaker.ContainsKey("talk")) bank.Speaker["talk"] = new Dictionary<string, List<string>>();
            for (int tone = 0; tone < 3; tone++)
            {
                if (bank.Speaker["talk"].ContainsKey(tone.ToString()) && bank.Speaker["talk"][tone.ToString()].Count >= 3) continue;
                var lines = await Ask(host, model, TalkPrompts.Speaker(tone, count), null, "talk", review, "talk / " + Names.ToneLabels[tone]);
                bank.Speaker["talk"][tone.ToString()] = lines; calls++; kept += lines.Count; Save(outPath, bank);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] speaker talk       {Names.ToneLabels[tone],-8} {lines.Count} lines");
            }
            if (!bank.Reply.ContainsKey("talk")) bank.Reply["talk"] = new Dictionary<string, Dictionary<string, List<string>>>();
            for (int q = 1; q <= 3; q++)
            {
                if (!bank.Reply["talk"].ContainsKey(q.ToString())) bank.Reply["talk"][q.ToString()] = new Dictionary<string, List<string>>();
                foreach (var t in temps.Concat(new[] { Temperament.Unknown }))
                {
                    var tk = Names.Key(t);
                    if (bank.Reply["talk"][q.ToString()].ContainsKey(tk) && bank.Reply["talk"][q.ToString()][tk].Count >= 3) continue;
                    var lines = await Ask(host, model, TalkPrompts.Reply(t, q, count), null, "talk", review, "talk / reply " + q + " / " + tk);
                    bank.Reply["talk"][q.ToString()][tk] = lines; calls++; kept += lines.Count; Save(outPath, bank);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] reply   talk       q{q} {tk,-16} {lines.Count} lines");
                }
            }
        }

        if (reviewPath != null) File.WriteAllText(reviewPath, review.ToString());
        Console.WriteLine($"done: {calls} model calls, {kept} lines kept. Bank: {outPath}");
        return 0;
    }

    static void Save(string path, BankFile bank)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(bank, Formatting.Indented));
        File.Copy(tmp, path, true); File.Delete(tmp);
    }

    // one model call -> the lines that pass the filter (and, with Laya, are not hostile for friendly interactions)
    static async Task<List<string>> Ask(string host, string model, string prompt, LayaClient laya, string key, StringBuilder review, string label)
    {
        var all = new List<string>();   // lines kept over several attempts: a model sometimes returns only one line
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                var body = new JObject
                {
                    ["model"] = model, ["prompt"] = prompt, ["stream"] = false, ["format"] = "json",
                    ["options"] = new JObject { ["temperature"] = 0.9, ["num_predict"] = 700 },
                };
                var resp = await http.PostAsync(host + "/api/generate", new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
                resp.EnsureSuccessStatusCode();
                var text = (string)JObject.Parse(await resp.Content.ReadAsStringAsync())["response"];
                var arr = JObject.Parse(text)["lines"] as JArray;
                if (arr == null) continue;
                var good = all;
                review.AppendLine("## " + label);
                foreach (var t in arr.Select(x => ((string)x ?? "").Trim().Trim('"').Trim()))
                {
                    string reason;
                    if (!DialogueBank.IsAcceptable(t, out reason)) { review.AppendLine("- ~~" + t + "~~ (" + reason + ")"); continue; }
                    if (laya != null && IsHostile(laya, t, key)) { review.AppendLine("- ~~" + t + "~~ (Laya hears it as hostile)"); continue; }
                    if (good.Contains(t)) continue;
                    good.Add(t); review.AppendLine("- " + t);
                }
                review.AppendLine();
                if (good.Count >= 3) return good;
            }
            catch (Exception e) { Console.WriteLine("  retry (" + e.Message + ")"); }
        }
        return all;
    }

    static readonly HashSet<string> Hostile = new HashSet<string> { "insult", "scare", "slap", "tease" };

    static bool IsHostile(LayaClient laya, string line, string key)
    {
        if (Hostile.Contains(key)) return false;
        return laya.ClassifyTone(line.Replace("{name}", "friend").Replace("{me}", "friend")) == "hostile";
    }
}
