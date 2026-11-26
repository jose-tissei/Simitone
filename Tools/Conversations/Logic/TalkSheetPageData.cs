using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Conversations
{
    // The Talk sheet (docs/ui-redesign, board "TalkSheet"): what to say (three rows), free text, tone, a memory hint.
    // Pure logic: the page data, the "why" chips, the tone advice and the shortcut keys. The window is Game/TalkSheetMenu.cs.
    public class TalkRow
    {
        public string Title;
        public string Why = "";   // short reason shown on the row ("Betty likes this", "New topic")
    }

    public static class TalkSheetPageData
    {
        public static readonly string[] ToneKeys = { "gentle", "playful", "bold" };
        public static readonly string[] ToneLabels = { "Gentle", "Playful", "Bold" };

        public static int ToneIndex(string key) { return Math.Max(0, Array.IndexOf(ToneKeys, key ?? "")); }

        // the reason a subject is on offer: what the speaker knows about the listener's taste, or that it is new
        public static string Why(string listenerName, bool knowsTaste, int liking)
        {
            if (!knowsTaste) return "New topic";
            return liking >= 2 ? listenerName + " likes this" : (liking == 1 ? listenerName + " is fine with it" : listenerName + " may not care");
        }

        // best and worst tone from what the Sim has learned (memory.ToneAdjustment per tone); null when nothing stands out
        public static string BestTone(double[] adj) { int b = ArgMax(adj); return adj[b] > 0.05 ? ToneKeys[b] : null; }
        public static string WorstTone(double[] adj) { int w = ArgMin(adj); return adj[w] < -0.05 ? ToneKeys[w] : null; }

        static int ArgMax(double[] a) { int b = 0; for (int i = 1; i < a.Length; i++) if (a[i] > a[b]) b = i; return b; }
        static int ArgMin(double[] a) { int b = 0; for (int i = 1; i < a.Length; i++) if (a[i] < a[b]) b = i; return b; }

        public static string Advice(double[] adj, string tone)
        {
            int i = ToneIndex(tone);
            return adj[i] > 0.05 ? ToneLabels[i] + " works well" : (adj[i] < -0.05 ? ToneLabels[i] + " went badly before" : "");
        }

        public static string Hint(string listenerName, double[] adj)
        {
            var best = BestTone(adj); var worst = WorstTone(adj);
            if (best == null && worst == null) return "You are still getting to know " + listenerName + ".";
            var s = best != null ? listenerName + " responds well to a " + best + " tone." : "";
            if (worst != null) s += (s.Length > 0 ? " " : "") + "Avoid a " + worst + " tone for now.";
            return s;
        }

        // lines: the "three lines to say" variant (one row per tone, no tone selector and no free text)
        public static Dictionary<string, object> Build(string speaker, string listener, string prompt, IList<TalkRow> rows, int selected,
            string tone, bool lines, string text, double[] toneAdj, bool canOpenMemory)
        {
            var d = new Dictionary<string, object>();
            d["title"] = lines ? prompt : "Talk with " + listener;
            d["letter"] = string.IsNullOrEmpty(listener) ? "?" : listener.Substring(0, 1).ToUpperInvariant();
            d["prompt"] = lines ? "What do you say to " + listener + "?" : prompt;
            d["rows"] = rows.Select((r, i) => (object)new Dictionary<string, object>
            {
                { "num", (i + 1).ToString() }, { "title", r.Title }, { "why", r.Why ?? "" }, { "hasWhy", !string.IsNullOrEmpty(r.Why) }, { "on", i == selected },
            }).ToList();
            d["showTone"] = !lines;
            d["showText"] = !lines;
            int ti = ToneIndex(tone);
            d["tones"] = ToneKeys.Select((k, i) => (object)new Dictionary<string, object> { { "key", k }, { "label", ToneLabels[i] }, { "on", i == ti } }).ToList();
            var advice = Advice(toneAdj ?? new double[3], tone);
            d["advice"] = advice; d["hasAdvice"] = advice.Length > 0;
            d["advicePositive"] = advice.EndsWith("works well");
            d["text"] = text ?? ""; d["textEmpty"] = string.IsNullOrEmpty(text);
            var hint = lines ? "" : Hint(listener, toneAdj ?? new double[3]);
            d["hint"] = hint; d["hasHint"] = hint.Length > 0;
            d["canOpenMemory"] = canOpenMemory && !lines;
            d["rowCount"] = rows.Count;
            d["pickHint"] = rows.Count <= 1 ? "" : "1" + (rows.Count > 1 ? "-" + rows.Count : "");
            d["sayLabel"] = "Say it";
            d["pickLabel"] = lines ? "pick a line" : "pick a topic";
            d["height"] = lines ? 520 : 700;
            return d;
        }

        // the page action for a shortcut key: "1".."3" pick a row, Enter says it, Esc is "never mind"; null for anything else
        public static string ActionForKey(string key, int rowCount)
        {
            int n;
            if (key != null && key.Length == 1 && int.TryParse(key, out n) && n >= 1 && n <= rowCount) return "pick-" + (n - 1);
            if (key == "Enter") return "say";
            if (key == "Escape") return "never";
            return null;
        }

        // The answer sent back to the game's dialog: the row index is the response code (0..2, as before) and the tone and the
        // player's own words travel in the response text ("<tone>|<words>"). Decode is the inverse; anything malformed is "gentle", no words.
        public static string EncodeReply(string tone, string text)
        {
            return (tone ?? "gentle") + "|" + (text ?? "").Replace("|", " ").Trim();
        }

        public static void DecodeReply(string reply, out string tone, out string text)
        {
            tone = "gentle"; text = "";
            if (string.IsNullOrEmpty(reply)) return;
            int bar = reply.IndexOf('|');
            if (bar < 0) return;
            var t = reply.Substring(0, bar);
            tone = Array.IndexOf(ToneKeys, t) >= 0 ? t : "gentle";
            text = reply.Substring(bar + 1).Trim();
        }
    }
}
