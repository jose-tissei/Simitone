using System;
using System.Collections.Generic;
using Xunit;
using Simitone.Conversations;

namespace Simitone.Conversations.Tests
{
    public class SimUiLayoutTests
    {
        public static IEnumerable<object[]> Resolutions()
        {
            yield return new object[] { 800f, 600f };
            yield return new object[] { 1024f, 768f };
            yield return new object[] { 1280f, 720f };
            yield return new object[] { 1920f, 1080f };
            yield return new object[] { 2560f, 1440f };
        }

        static void AssertNoOverlapsAndContained(SimRect parent, params (string Name, SimRect Rect)[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                var a = items[i];
                Assert.False(a.Rect.IsEmpty, a.Name + " should have positive dimensions");
                Assert.True(parent.Contains(a.Rect), a.Name + " " + a.Rect + " escaped parent " + parent);

                for (int j = i + 1; j < items.Length; j++)
                {
                    var b = items[j];
                    Assert.False(a.Rect.Intersects(b.Rect),
                        a.Name + " " + a.Rect + " overlaps " + b.Name + " " + b.Rect);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Resolutions))]
        public void DispatchToast_HasZeroOverlapsAcrossResolutions(float w, float h)
        {
            var screen = new SimRect(0, 0, w, h);
            var l = DispatchToastLayout.Compute(w, h);

            Assert.True(screen.Contains(l.Panel), "Toast escaped screen " + screen);

            AssertNoOverlapsAndContained(l.HeaderBar,
                ("LedRect", l.LedRect),
                ("ChannelTextRect", l.ChannelTextRect),
                ("SignalBarsRect", l.SignalBarsRect));

            AssertNoOverlapsAndContained(l.Panel,
                ("HeaderBar", l.HeaderBar),
                ("TitleRect", l.TitleRect),
                ("BodyRect", l.BodyRect),
                ("AnswerBtn", l.AnswerBtn),
                ("IgnoreBtn", l.IgnoreBtn),
                ("TimerTextRect", l.TimerTextRect),
                ("TimerBarRect", l.TimerBarRect));
        }

        [Theory]
        [MemberData(nameof(Resolutions))]
        public void SecurityWatch_ActiveCctv_HasZeroOverlapsAcrossResolutions(float w, float h)
        {
            var screen = new SimRect(0, 0, w, h);
            var l = SecurityWatchLayout.Compute(w, h, false);

            Assert.True(screen.Contains(l.Panel), "SecurityWatch modal escaped screen " + screen);

            AssertNoOverlapsAndContained(l.HeaderBar,
                ("TitleRect", l.TitleRect),
                ("RoundBadge", l.RoundBadge),
                ("ScoreBadge", l.ScoreBadge),
                ("CloseBtn", l.CloseBtn));

            AssertNoOverlapsAndContained(l.Panel,
                ("HeaderBar", l.HeaderBar),
                ("SubtitleBar", l.SubtitleBar),
                ("Cam0", l.Cameras[0]),
                ("Cam1", l.Cameras[1]),
                ("Cam2", l.Cameras[2]),
                ("Cam3", l.Cameras[3]),
                ("DeckPanel", l.DeckPanel));

            AssertNoOverlapsAndContained(l.DeckPanel,
                ("ReportTextRect", l.ReportTextRect),
                ("ClueTextRect", l.ClueTextRect),
                ("TimerLabelRect", l.TimerLabelRect),
                ("TimerBarRect", l.TimerBarRect),
                ("AlarmBtn", l.AlarmBtn),
                ("ClearBtn", l.ClearBtn),
                ("AwakeLabelRect", l.AwakeLabelRect),
                ("AwakeBarRect", l.AwakeBarRect),
                ("CoffeeBtn", l.CoffeeBtn));
        }

        [Fact]
        public void SecurityWatch_StacksDeckControlsOnNarrowViewports()
        {
            var narrow = SecurityWatchLayout.Compute(740f, 580f, false);
            var wide = SecurityWatchLayout.Compute(1280f, 720f, false);

            Assert.True(narrow.CompactDeck);
            Assert.True(narrow.CoffeeBtn.Top >= narrow.AlarmBtn.Bottom);
            Assert.False(wide.CompactDeck);
            Assert.Equal(wide.AlarmBtn.Top, wide.CoffeeBtn.Top, 1);
        }

        [Theory]
        [MemberData(nameof(Resolutions))]
        public void SecurityWatch_ShiftReport_HasZeroOverlapsAcrossResolutions(float w, float h)
        {
            var screen = new SimRect(0, 0, w, h);
            var l = SecurityWatchLayout.Compute(w, h, true);

            Assert.True(screen.Contains(l.Panel), "Shift Report modal escaped screen " + screen);

            AssertNoOverlapsAndContained(l.Panel,
                ("HeaderBar", l.HeaderBar),
                ("SubtitleBar", l.SubtitleBar),
                ("SummaryCard", l.SummaryCard));

            AssertNoOverlapsAndContained(l.SummaryCard,
                ("GradeBadge", l.GradeBadge),
                ("GradeCaptionRect", l.GradeCaptionRect),
                ("SummaryTitleRect", l.SummaryTitleRect),
                ("SummaryBodyRect", l.SummaryBodyRect),
                ("Stat0", l.StatCards[0]),
                ("Stat1", l.StatCards[1]),
                ("Stat2", l.StatCards[2]),
                ("RewardsCard", l.RewardsCard),
                ("ClaimBtn", l.ClaimBtn));

            AssertNoOverlapsAndContained(l.RewardsCard,
                ("RewardsHeaderRect", l.RewardsHeaderRect),
                ("RewardCashRect", l.RewardCashRect),
                ("RewardPerfRect", l.RewardPerfRect),
                ("RewardMotivesRect", l.RewardMotivesRect));
        }

        [Theory]
        [MemberData(nameof(Resolutions))]
        public void SimMemory_HasZeroOverlapsAcrossResolutions(float w, float h)
        {
            var screen = new SimRect(0, 0, w, h);
            var l = SimMemoryLayout.Compute(w, h);

            Assert.True(screen.Contains(l.Panel), "SimMemory modal escaped screen " + screen);

            AssertNoOverlapsAndContained(l.HeaderBar,
                ("TitleRect", l.TitleRect),
                ("CloseBtn", l.CloseBtn));

            AssertNoOverlapsAndContained(l.Panel,
                ("HeaderBar", l.HeaderBar),
                ("SubtitleRect", l.SubtitleRect),
                ("TabStripRect", l.TabStripRect),
                ("LeftColumn", l.LeftColumn),
                ("RightColumn", l.RightColumn),
                ("FooterRect", l.FooterRect));

            AssertNoOverlapsAndContained(l.LeftColumn,
                ("LeftHeaderRect", l.LeftHeaderRect),
                ("LeftListRect", l.LeftListRect));

            AssertNoOverlapsAndContained(l.RightColumn,
                ("DossierNameRect", l.DossierNameRect),
                ("DossierRelRect", l.DossierRelRect),
                ("FamiliarityLabelRect", l.FamiliarityLabelRect),
                ("FamiliarityBarRect", l.FamiliarityBarRect),
                ("DossierBodyRect", l.DossierBodyRect));

            Assert.True(l.MaxVisibleRows >= 1);
            var lastRow = l.GetRowRect(l.MaxVisibleRows - 1);
            Assert.True(l.LeftListRect.Contains(lastRow, 1f), "Last visible row escaped LeftListRect");
        }

        [Fact]
        public void SimMemory_StacksDossierHeaderOnNarrowViewports()
        {
            var narrow = SimMemoryLayout.Compute(640f, 520f);
            var wide = SimMemoryLayout.Compute(1280f, 720f);

            Assert.True(narrow.StackedDossierHeader);
            Assert.True(narrow.FamiliarityBarRect.Top >= narrow.DossierRelRect.Bottom);
            Assert.False(wide.StackedDossierHeader);
            Assert.True(wide.FamiliarityBarRect.Left >= wide.DossierNameRect.Right);
        }

        [Fact]
        public void Ellipsize_StrictlyFitsWithinMaxWidth()
        {
            Func<string, float> measure = s => s.Length * 8f;

            Assert.Equal("Short", SimLayout.Ellipsize("Short", 100f, measure));

            var clipped = SimLayout.Ellipsize("Extremely Long Sim Name And Career Description", 100f, measure);
            Assert.EndsWith("...", clipped);
            Assert.True(measure(clipped) <= 100f);
            Assert.Equal("", SimLayout.Ellipsize("Hello", 2f, measure));
        }

        [Fact]
        public void WrapClamped_RespectsMaxLinesAndWidthWithEllipsis()
        {
            Func<string, float> measure = s => s.Length * 8f;
            string longText = "Bob Newbie had an extraordinary shift catching burglars vandals and prowlers across every precinct camera.";

            var lines = SimLayout.WrapClamped(longText, 160f, 2, measure);
            Assert.Equal(2, lines.Count);
            Assert.EndsWith("...", lines[1]);
            foreach (var line in lines)
                Assert.True(measure(line) <= 160f, "Line exceeded maxWidth: " + line);
        }
    }
}
