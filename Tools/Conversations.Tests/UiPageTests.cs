using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Simitone.Conversations;
using Simitone.HtmlUi;
using Xunit;

namespace Conversations.Tests
{
    // the template engine and design tokens behind the HTML windows (Tools/HtmlUi), and the data the Sim Memory page is filled with
    public class UiPageTests
    {
        static Dictionary<string, object> D(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        [Fact]
        public void Template_ReplacesAndEscapes()
        {
            Assert.Equal("Hi &lt;b&gt; & Bob", Template.Render("Hi {{a}} & {{ who.name }}", D("a", "<b>", "who", D("name", "Bob"))));
            Assert.Equal("[]", Template.Render("[{{missing}}]", D()));
        }

        [Fact]
        public void Template_Each_RepeatsWithIndexAndItem()
        {
            var data = D("xs", new List<object> { "a", "b", "c" });
            Assert.Equal("0a,1b,2c,", Template.Render("<!--#each xs-->{{#}}{{.}},<!--/each-->", data));
            Assert.Equal("none", Template.Render("<!--#each ys-->x<!--/each-->none", data));
        }

        [Fact]
        public void Template_Each_ItemsSeeTheirParentScope()
        {
            var data = D("title", "T", "rows", new List<object> { D("n", 1), D("n", 2) });
            Assert.Equal("T1T2", Template.Render("<!--#each rows-->{{title}}{{n}}<!--/each-->", data));
        }

        [Fact]
        public void Template_IfElse_AndNesting()
        {
            string t = "<!--#if a-->A<!--#if b-->B<!--#else-->b<!--/if--><!--#else-->x<!--/if-->";
            Assert.Equal("AB", Template.Render(t, D("a", true, "b", true)));
            Assert.Equal("Ab", Template.Render(t, D("a", true, "b", false)));
            Assert.Equal("x", Template.Render(t, D("a", false, "b", true)));
            Assert.Equal("yes", Template.Render("<!--#if xs-->yes<!--#else-->no<!--/if-->", D("xs", new List<object> { 1 })));
            Assert.Equal("no", Template.Render("<!--#if xs-->yes<!--#else-->no<!--/if-->", D("xs", new List<object>())));
        }

        [Fact]
        public void Template_UnclosedBlockThrows()
        {
            Assert.Throws<InvalidOperationException>(() => Template.Render("<!--#if a-->x", D("a", true)));
        }

        static string FindTokens()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "docs", "ui-redesign", "design-tokens.json");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        [Fact]
        public void ThemeTokens_BothThemesDefineTheSameVariables()
        {
            var json = File.ReadAllText(FindTokens());
            var night = ThemeTokens.Build(json, "night"); var day = ThemeTokens.Build(json, "day");
            Assert.Equal(night.Keys.OrderBy(k => k), day.Keys.OrderBy(k => k));
            foreach (var name in new[] { "card", "text", "text-2", "accent", "accent-deep", "head-a", "good-ink", "good-a16", "r-card", "sp-4", "sim-3", "icon-btn-a", "pill-text" })
                Assert.True(night.ContainsKey(name), name);
            Assert.NotEqual(night["card"], day["card"]);
        }

        static SimMemorySnapshot Snapshot(int topics = 0)
        {
            var card = new SimKnowledgeCard { ListenerKey = "n:1", ListenerName = "Betty", Exchanges = 33, PositiveCount = 26, NegativeCount = 3, LastStr = 6, LastLtr = 2, KnowledgePercent = 52, FamiliarityLabel = "Acquaintance", IsTense = true };
            card.DislikedInterests.AddRange(new[] { "A", "B", "C", "D" });
            for (int i = 0; i < topics; i++) card.Topics.Add(new LearnedTopicEntry { Title = "T" + i, LastOutcome = 1 });
            var snap = new SimMemorySnapshot { SpeakerKey = "n:2", SpeakerName = "Bob" };
            snap.KnownSims.Add(card);
            return snap;
        }

        [Fact]
        public void PageData_CardShowsNumbersAndCapsLongLists()
        {
            var speakers = new List<SpeakerIdentity> { new SpeakerIdentity { Key = "n:2", Name = "Bob", KnownCount = 1 } };
            var d = SimMemoryPageData.Build(Snapshot(5), speakers, "n:2", "n:1", false, new List<NewsEntry>(), "Bob");
            var card = (Dictionary<string, object>)d["card"];
            Assert.Equal("+6", card["today"]);
            Assert.Equal(26, card["great"]); Assert.Equal(4, card["ok"]); Assert.Equal(3, card["awkward"]);
            var dislikes = ((List<object>)card["dislikes"]).Cast<string>().ToList();
            Assert.Equal(new[] { "A", "B", "+2 more" }, dislikes);
            Assert.Equal(2, ((List<object>)card["topics"]).Count);
            // the bar runs from the centre line: half of the percentage to the right, the rest empty
            Assert.Equal(26, card["relFill"]); Assert.Equal(24, card["relRest"]);
            Assert.True((bool)((Dictionary<string, object>)((List<object>)d["known"])[0])["selected"]);
        }

        [Fact]
        public void PageData_NewsChipsNeverRelyOnColourAlone()
        {
            var news = new List<NewsEntry>
            {
                new NewsEntry { Text = "ann gave maybe Hank a gift today", Certainty = "not sure", Source = "heard it from Bob", Differs = true, Fuzzy = true },
                new NewsEntry { Text = "Betty and Bob became friends today", Certainty = "saw it" },
            };
            var d = SimMemoryPageData.Build(Snapshot(), new List<SpeakerIdentity>(), "n:2", "n:1", true, news, "Bob");
            var rows = ((List<object>)d["news"]).Cast<Dictionary<string, object>>().ToList();
            Assert.Equal("Ann gave maybe Hank a gift today", rows[0]["text"]);
            Assert.True((bool)rows[0]["flagged"]);
            Assert.Equal("gift", rows[0]["icon"]);
            var chips = ((List<object>)rows[0]["chips"]).Cast<Dictionary<string, object>>().ToList();
            Assert.All(chips, c => { Assert.False(string.IsNullOrEmpty((string)c["icon"])); Assert.False(string.IsNullOrEmpty((string)c["label"])); });
            Assert.Contains(chips, c => (string)c["cls"] == "bad" && ((string)c["label"]).StartsWith("Differs"));
            Assert.Equal("eye", ((Dictionary<string, object>)((List<object>)rows[1]["chips"])[0])["icon"]);
        }

        [Fact]
        public void AiSettings_PageDataAndActions()
        {
            var d = AiSettingsPageData.Build(true, false, true, false, "paused");
            Assert.True((bool)d["convo"]); Assert.False((bool)d["gossip"]); Assert.True((bool)d["simple"]); Assert.False((bool)d["day"]);
            Assert.False((bool)d["layaUp"]);
            Assert.Contains("Paused", (string)d["layaText"]);
            Assert.True((bool)AiSettingsPageData.Build(true, true, true, true, "up")["layaUp"]);
            Assert.Equal("gossip", AiSettingsPageData.SettingOf("toggle-gossip"));
            Assert.Equal("theme", AiSettingsPageData.SettingOf("toggle-theme"));
            Assert.Null(AiSettingsPageData.SettingOf("close"));
        }

        [Fact]
        public void AiSettingsPage_RendersEverySwitchWithAWordForItsState()
        {
            var html = Template.Render(File.ReadAllText(FindPage("sims_options.html")), AiSettingsPageData.Build(true, false, true, false, "up"));
            Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(html, @"class=""sw on""").Count);   // conversations + simplified menu are on
            Assert.Contains(">Off<", html); Assert.Contains(">On<", html);
        }

        [Fact]
        public void PieStyle_IconsLabelsAndSlotWidth()
        {
            Assert.Equal("book", PieStyle.IconFor("Study Cooking"));
            Assert.Equal("chat", PieStyle.IconFor("Talk..."));
            Assert.Equal("kiss", PieStyle.IconFor("Romance/Kiss"));
            Assert.Equal("right", PieStyle.IconFor("Go Here"));
            Assert.Equal("hand", PieStyle.IconFor("Zzz unknown"));
            Assert.Equal("hand", PieStyle.IconFor(""));
            Assert.True(PieStyle.IsSubmenu("Romance...")); Assert.False(PieStyle.IsSubmenu("Hug"));
            Assert.Equal("Romance", PieStyle.CleanLabel("Romance..."));
            Assert.Equal("Polite", PieStyle.ShortLabel("Kiss - Polite"));
            Assert.Equal("Back Rub", PieStyle.ShortLabel("Flirt - Back Rub"));
            Assert.Equal("Talk", PieStyle.ShortLabel("Talk..."));
            Assert.Equal("Go Here", PieStyle.ShortLabel("Go Here"));
            // never narrower than the native button it hides, and a submenu marker adds room
            Assert.Equal(108f, PieStyle.SlotWidth(10, 20, 100, false));
            Assert.True(PieStyle.SlotWidth(80, 20, 50, true) > PieStyle.SlotWidth(80, 20, 50, false));
        }

        [Fact]
        public void PieStyle_EveryIconNameExistsInTheDesignSheet()
        {
            var order = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(FindTokens()))["iconSheet"]["order"].Select(t => (string)t).ToList();
            foreach (var label in new[] { "study", "kiss", "romance", "hug", "cheer", "talk", "joke", "brag", "apolog", "dance", "play", "sleep", "eat", "shower", "toilet", "clean", "read", "call", "gift", "watch", "sit", "chair", "work", "fight", "trash", "sell", "repair", "go here", "light", "computer", "turn", "cancel", "back", "nothing" })
                Assert.Contains(PieStyle.IconFor(label), order);
        }

        static string FindPage(string name)
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "Tools", "HtmlUi", "ui", name);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        [Fact]
        public void PageData_SimColourIsStablePerName()
        {
            Assert.Equal(SimMemoryPageData.SimColor("Betty"), SimMemoryPageData.SimColor("betty"));
            Assert.InRange(SimMemoryPageData.SimColor("Mystery Man"), 1, 6);
        }
    }
}
