using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.SimAntics;
using FSO.SimAntics.NetPlay.Model.Commands;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Conversations
{
    // The Talk sheet (docs/ui-redesign "TalkSheet"): replaces the engine's Yes/No/Cancel dialog for the Talk subject and for the
    // "three lines to say" choice. The simulation stays blocked on the thread's VMDialogResult exactly as with the native dialog;
    // the sheet answers with the same VMNetDialogResponseCmd the native dialog's buttons send (code = row, text = "<tone>|<words>").
    public class TalkSheetRequest
    {
        public VM Vm;
        public VMEntity Speaker;
        public string SpeakerName, ListenerName, Prompt;
        public List<TalkRow> Rows = new List<TalkRow>();
        public bool Lines;                 // the "three lines to say" variant: rows are tones, no free text
        public string Tone = "gentle";
        public double[] ToneAdj = new double[3];
    }

    public class TalkSheetMenu : UIElement
    {
        public const byte NeverMind = 255;   // response code of "Never mind" (the rows use 0..2)

        static TalkSheetMenu instance;
        readonly HtmlSurface surface;
        TalkSheetRequest req;
        int selected;
        string tone = "gentle";
        readonly StringBuilder text = new StringBuilder();
        string lastKey = "";
        bool prevEsc = true, prevEnter = true;

        public static bool IsOpen { get { return instance != null && instance.Parent != null && instance.req != null; } }

        TalkSheetMenu()
        {
            ListenForMouse(new Microsoft.Xna.Framework.Rectangle(0, 0, 4000, 3000), (type, st) => { });   // modal: nothing underneath gets these clicks
            surface = new HtmlSurface(this, "talk_sheet.html");
            surface.Action += a => Act(a);
        }

        // false when the sheet cannot be shown (the caller then falls back to the native dialog)
        public static bool Open(TalkSheetRequest r)
        {
            try
            {
                var screen = UIScreen.Current;
                if (screen == null || r == null || r.Rows.Count == 0) return false;
                if (!File.Exists(Path.Combine(Boot.GameDir, "Content", "ui", "talk_sheet.html"))) return false;
                if (SimMemoryMenu.IsOpen) SimMemoryMenu.Close();
                if (SimsOptionsMenu.IsOpen) SimsOptionsMenu.Close();
                if (instance == null) instance = new TalkSheetMenu();
                instance.req = r; instance.selected = 0; instance.tone = r.Tone ?? "gentle"; instance.text.Clear(); instance.lastKey = "";
                if (instance.Parent != screen) { if (instance.Parent != null) instance.Parent.Remove(instance); screen.Add(instance); }
                instance.surface.IgnoreCurrentClick(); instance.prevEsc = true; instance.prevEnter = true;
                return true;
            }
            catch (Exception e) { Boot.Log("talk sheet open failed: " + e.Message); return false; }
        }

        public static void Close()
        {
            if (instance != null && instance.Parent != null) instance.Parent.Remove(instance);
        }

        public static string Status()
        {
            if (!IsOpen) return "open=False";
            return "open=True selected=" + instance.selected + " tone=" + instance.tone + " text=[" + instance.text + "] rows=[" + string.Join(" | ", instance.req.Rows.Select(r => r.Title + " (" + r.Why + ")")) + "] lines=" + instance.req.Lines;
        }

        // test aid and the one place every page action goes through: simtool convo talksheet pick 1 | tone bold | type hello | say | never
        public static string Command(string[] a)
        {
            var sub = a.Length > 0 ? a[0].ToLowerInvariant() : "status";
            if (!IsOpen) return "open=False";
            switch (sub)
            {
                case "pick": Act("pick-" + (int.Parse(a[1]) - 1)); break;
                case "tone": Act("tone-" + a[1].ToLowerInvariant()); break;
                case "type": instance.text.Clear(); instance.text.Append(string.Join(" ", a.Skip(1))); break;
                case "say": Act("say"); break;
                case "never": Act("never"); break;
            }
            return Status();
        }

        public static void Act(string action)
        {
            var me = instance; if (me == null || me.req == null) return;
            try
            {
                int n;
                if (action == "close" || action == "never") { me.Answer(NeverMind, ""); return; }
                if (action == "say") { me.Say(); return; }
                if (action == "memory") { SimMemoryMenu.Open(null); return; }   // the sheet stays up behind it: Memory is a read-only side trip
                if (action.StartsWith("pick-") && int.TryParse(action.Substring(5), out n) && n >= 0 && n < me.req.Rows.Count) { me.selected = n; if (me.req.Lines) me.Say(); }
                else if (action.StartsWith("tone-")) me.tone = action.Substring(5);
            }
            catch (Exception e) { Boot.Log("talk sheet action " + action + ": " + e.Message); }
        }

        // the chosen row; the player's own words can steer it to another row (Laya reads which offered topic the words are about)
        void Say()
        {
            int code = selected;
            var words = text.ToString().Trim();
            if (words.Length > 0 && !req.Lines && req.Rows.Count > 1) code = Classify(words, selected);
            Answer((byte)code, TalkSheetPageData.EncodeReply(tone, words));
        }

        int Classify(string words, int fallback)
        {
            try
            {
                if (!Boot.Laya.Available) return fallback;
                var criteria = req.Rows.Select((r, i) => i + "|" + r.Title).ToList();
                var scores = Boot.Laya.ScoreChoices(req.SpeakerName + " says to " + req.ListenerName + ": \"" + words + "\"", "Which of these topics is this about?", criteria);
                if (scores == null || scores.Count == 0) return fallback;
                string best = null; double top = 0.5;   // a clear match only; otherwise the row the player picked stays
                foreach (var kv in scores) if (kv.Value > top) { top = kv.Value; best = kv.Key; }
                int idx; return best != null && int.TryParse(best.Split('|')[0], out idx) && idx >= 0 && idx < req.Rows.Count ? idx : fallback;
            }
            catch (Exception e) { Boot.Log("talk sheet classify: " + e.Message); return fallback; }
        }

        void Answer(byte code, string reply)
        {
            var r = req; Close(); req = null;
            if (r == null || r.Vm == null) return;
            r.Vm.SendCommand(new VMNetDialogResponseCmd { ActorUID = r.Speaker == null ? 0 : r.Speaker.PersistID, ResponseCode = code, ResponseText = reply ?? "" });
            if (r.Vm.SpeedMultiplier == 0) r.Vm.SpeedMultiplier = 1;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!IsOpen || req == null) return;
            // answered some other way (the test hook, or the VM moved on): close
            if (req.Vm != null && req.Vm.GlobalBlockingDialog == null && req.Vm.TS1) { Close(); req = null; return; }

            bool esc = state.KeyboardState.IsKeyDown(Keys.Escape);
            if (esc && !prevEsc) { prevEsc = true; Act("never"); return; }
            prevEsc = esc;
            bool enter = state.KeyboardState.IsKeyDown(Keys.Enter);
            if (enter && !prevEnter) { prevEnter = true; Act("say"); return; }
            prevEnter = enter;

            // 1-3 pick a row while nothing has been typed (then digits are text)
            if (text.Length == 0)
                foreach (var k in state.NewKeys)
                {
                    int n = k >= Keys.D1 && k <= Keys.D9 ? k - Keys.D1 : (k >= Keys.NumPad1 && k <= Keys.NumPad9 ? k - Keys.NumPad1 : -1);
                    if (n >= 0 && n < req.Rows.Count) { Act("pick-" + n); if (!IsOpen) return; return; }
                }
            if (!req.Lines) Type(state);
            surface.Update(state);
        }

        void Type(UpdateState state)
        {
            try
            {
                string was = text.ToString();
                state.InputManager.ApplyKeyboardInput(text, state, -1, -1, true);
                bool noText = state.FrameTextInput == null || state.FrameTextInput.Count == 0;
                if (noText && text.ToString() == was)
                {
                    bool shift = state.KeyboardState.IsKeyDown(Keys.LeftShift) || state.KeyboardState.IsKeyDown(Keys.RightShift);
                    foreach (var k in state.NewKeys)
                        if ((k >= Keys.A && k <= Keys.Z) || (k >= Keys.D0 && k <= Keys.D9) || k == Keys.Space)
                        {
                            char c = FSO.Common.Rendering.Framework.IO.InputManager.TranslateChar(k, shift, state.KeyboardState.CapsLock, false);
                            if (c != 0) text.Append(c);
                        }
                }
                text.Replace("\r", "").Replace("\n", "").Replace("\t", "");
                if (text.Length > 120) text.Length = 120;
            }
            catch (Exception e) { Boot.Log("talk sheet typing: " + e.Message); }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!IsOpen || req == null) return;
            string words = text.ToString();
            string key = selected + "|" + tone + "|" + words + "|" + Boot.Theme;
            surface.Theme = Boot.Theme;
            if (key != lastKey)
            {
                lastKey = key;
                surface.SetData(TalkSheetPageData.Build(req.SpeakerName, req.ListenerName, req.Prompt, req.Rows, selected, tone, req.Lines, words, req.ToneAdj, !req.Lines), key);
                surface.Invalidate();
            }
            surface.Draw(batch);
        }
    }
}
