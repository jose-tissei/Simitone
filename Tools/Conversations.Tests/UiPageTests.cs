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
        public void AiSettings_ViewTilesAndGameActions()
        {
            var d = AiSettingsPageData.Build(true, true, true, false, "up", "arch", true, false, true);
            var tiles = ((List<object>)d["tiles"]).Cast<Dictionary<string, object>>().ToList();
            Assert.Equal(new[] { "grid", "cube", "home", "eye" }, tiles.Select(x => (string)x["icon"]).ToArray());
            Assert.Equal(new[] { false, false, true, false }, tiles.Select(x => (bool)x["on"]).ToArray());
            Assert.Equal("arch", AiSettingsPageData.ViewOf("view-arch")); Assert.Null(AiSettingsPageData.ViewOf("save"));
            foreach (var a in new[] { "save", "hood", "reload", "quit" }) Assert.Equal(a, AiSettingsPageData.SettingOf(a));
            Assert.Equal("aa", AiSettingsPageData.SettingOf("toggle-aa")); Assert.Equal("light", AiSettingsPageData.SettingOf("toggle-light"));
            var html = Template.Render(File.ReadAllText(FindPage("sims_options.html")), d);
            Assert.Contains("data-action=\"view-full3d\"", html); Assert.Contains("class=\"tile on\"", html);
            Assert.DoesNotContain("data-action=\"view-", Template.Render(File.ReadAllText(FindPage("sims_options.html")), AiSettingsPageData.Build(true, true, true, false, "up")));   // no SpriteCards: no View group
        }

        [Fact]
        public void AiSettingsPage_RendersEverySwitchWithAWordForItsState()
        {
            var html = Template.Render(File.ReadAllText(FindPage("sims_options.html")), AiSettingsPageData.Build(true, false, true, false, "up"));
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(html, @"class=""sw on""").Count);   // only conversations is on (gossip and day theme are off)
            Assert.Contains(@"class=""opt on""><span>Simplified<", html);                                  // the pie menu style is a segmented choice, not a switch
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

        [Fact]
        public void PieStyle_OrbitIsEvenAroundTheHeadAndNeverOverlaps()
        {
            float[] cx, cy;
            foreach (var n in new[] { 1, 2, 3, 6, 8, 11 })
                foreach (var back in new[] { false, true })
                {
                    var w = Enumerable.Range(0, n).Select(i => 110f + 14 * (i % 4)).ToArray();
                    PieStyle.Orbit(w, 40, back, 150, 235, 128, 8, out cx, out cy);
                    for (int i = 0; i < n; i++)
                    {
                        if (back) Assert.True(Math.Abs(cx[i]) >= (w[i] + 150) / 2 + 8 || Math.Abs(cy[i]) >= 40 + 8);
                        for (int j = i + 1; j < n; j++)
                            Assert.True(Math.Abs(cx[i] - cx[j]) >= (w[i] + w[j]) / 2 + 8 || Math.Abs(cy[i] - cy[j]) >= 48);
                    }
                }
            PieStyle.Orbit(new float[] { 120, 120, 120, 120, 120, 120 }, 48, false, 0, 235, 128, 8, out cx, out cy);
            Assert.Equal(0f, cx[0], 3); Assert.Equal(-128f, cy[0], 3);   // first slot straight above the head
            Assert.Equal(0f, cx[3], 3); Assert.Equal(128f, cy[3], 3);    // fourth straight below
            Assert.Equal(-cx[1], cx[5], 3); Assert.Equal(cy[1], cy[5], 3);   // left and right mirror each other
        }

        [Fact]
        public void PieStyle_TooltipsNameTheTargetAndSkipUnknownVerbs()
        {
            Assert.Equal("Start a conversation with Betty.", PieStyle.Tooltip("Talk...", "Betty"));
            Assert.Null(PieStyle.Tooltip("Talk...", ""));                       // a Sim verb on an object: nothing to say
            Assert.Equal("Walk to this spot.", PieStyle.Tooltip("Go Here", ""));
            Assert.Equal("Kiss Betty.", PieStyle.Tooltip("Kiss - Peck", "Betty"));
            Assert.Equal("Ask Betty to come over.", PieStyle.Tooltip("Invite", "Betty"));
            Assert.Equal("Invite Betty downtown.", PieStyle.Tooltip("Invite Downtown", "Betty"));
            Assert.Equal("Take a seat.", PieStyle.Tooltip("Sit", ""));
            Assert.Equal("Play chess to build Logic.", PieStyle.Tooltip("Play Chess", ""));
            Assert.Null(PieStyle.Tooltip("Kiss", ""));                           // a Sim verb with no Sim: nothing to say
            Assert.Equal("More choices: Zzz.", PieStyle.Tooltip("Zzz...", "Bob"));
            Assert.Null(PieStyle.Tooltip("Zzz", "Bob"));
        }

        // Rounded shapes, borders and icons are only smooth when they go through HtmlSurface (anti-aliased corner masks, averaged icons).
        // Drawing a rectangle by hand elsewhere would bring the stair-stepped edges back, so new UI must use the painter.
        [Fact]
        public void NewUi_NeverDrawsShapesByHand()
        {
            string dir = null;
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null && dir == null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "Tools", "Conversations", "Game");
                if (Directory.Exists(p)) dir = p;
            }
            Assert.NotNull(dir);
            var allowed = new[] { "HtmlSurface.cs", "SimMemoryMenu.cs", "SimUiTheme.cs", "CadetAcademyUI.cs" };   // the painter itself, the old hand-drawn Sim Memory fallback, and legacy career mini-game overlays
            foreach (var f in Directory.GetFiles(dir, "*.cs").Where(f => !allowed.Contains(Path.GetFileName(f))))
                Assert.False(File.ReadAllText(f).Contains("DrawLocalTexture("), Path.GetFileName(f) + " draws textures itself: use HtmlSurface (FillRect, FillGradient, StrokeRect, DrawIcon, FillTail)");
        }

        [Fact]
        public void PageData_NewsFilterAndShowMore()
        {
            var news = new List<NewsEntry>();
            for (int i = 0; i < 7; i++) news.Add(new NewsEntry { Text = "story " + i, Certainty = i % 2 == 0 ? "saw it" : "not sure", Differs = i == 3 });
            var speakers = new List<SpeakerIdentity>();
            var all = SimMemoryPageData.Build(Snapshot(), speakers, "n:2", "n:1", true, news, "Bob");
            Assert.Equal(5, ((List<object>)all["news"]).Count);
            Assert.Equal("Show 2 more", all["newsMore"]);
            var more = SimMemoryPageData.Build(Snapshot(), speakers, "n:2", "n:1", true, news, "Bob", "all", true);
            Assert.Equal(7, ((List<object>)more["news"]).Count); Assert.Equal("", more["newsMore"]);
            Assert.Equal(4, ((List<object>)SimMemoryPageData.Build(Snapshot(), speakers, "n:2", "n:1", true, news, "Bob", "saw")["news"]).Count);
            Assert.Single((List<object>)SimMemoryPageData.Build(Snapshot(), speakers, "n:2", "n:1", true, news, "Bob", "mismatch")["news"]);
            var none = SimMemoryPageData.Build(Snapshot(), speakers, "n:2", "n:1", true, new List<NewsEntry> { news[0] }, "Bob", "mismatch");
            Assert.True((bool)none["newsNoMatch"]); Assert.False((bool)none["newsEmpty"]);
        }

        [Fact]
        public void PageData_SearchMatchesNamesAnyCase()
        {
            Assert.True(SimMemoryPageData.NameMatches("Betty Newbie", "newb")); Assert.True(SimMemoryPageData.NameMatches("Betty", "  "));
            Assert.False(SimMemoryPageData.NameMatches("Betty", "bob"));
            var d = SimMemoryPageData.Build(Snapshot(), new List<SpeakerIdentity>(), "n:2", "n:1", false, new List<NewsEntry>(), "Bob", "all", false, "zzz", 5);
            Assert.Contains("5 Sims", (string)d["subtitle"]);
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

        [Fact]
        public void SimIdentityInkIsDeeperInTheDayTheme()
        {
        var json = System.IO.File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FindPage("sims-ui.css")), "..", "..", "..", "docs", "ui-redesign", "design-tokens.json")));
        var night = ThemeTokens.Build(json, "night"); var day = ThemeTokens.Build(json, "day");
        Assert.Equal(night["sim-2"], night["sim-ink-2"]);          // night: the pastel colour as designed
        Assert.NotEqual(day["sim-2"], day["sim-ink-2"]);           // day: deeper, readable on a light card
        Assert.Equal("#7CE8FF", day["sim-2"]);                      // the disc colour itself is unchanged
    }
    }
}
