using System;
using System.Collections.Generic;

namespace Simitone.Conversations
{
    // Pure, game-engine-free rectangle struct for responsive UI layout calculation and unit testing.
    public struct SimRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;

        public float Left { get { return X; } }
        public float Top { get { return Y; } }
        public float Right { get { return X + Width; } }
        public float Bottom { get { return Y + Height; } }
        public bool IsEmpty { get { return Width <= 0f || Height <= 0f; } }

        public SimRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = Math.Max(0f, width);
            Height = Math.Max(0f, height);
        }

        public SimRect Deflate(float padX, float padY)
        {
            return new SimRect(X + padX, Y + padY, Math.Max(0f, Width - padX * 2f), Math.Max(0f, Height - padY * 2f));
        }

        public bool Contains(SimRect other, float epsilon = 0.5f)
        {
            return other.Left >= Left - epsilon
                && other.Top >= Top - epsilon
                && other.Right <= Right + epsilon
                && other.Bottom <= Bottom + epsilon;
        }

        public bool Intersects(SimRect other, float epsilon = 0.01f)
        {
            if (IsEmpty || other.IsEmpty) return false;
            return Left < other.Right - epsilon
                && Right > other.Left + epsilon
                && Top < other.Bottom - epsilon
                && Bottom > other.Top + epsilon;
        }

        public override string ToString()
        {
            return string.Format("({0:0.#}, {1:0.#}, {2:0.#}x{3:0.#})", X, Y, Width, Height);
        }
    }

    // Shared responsive geometry & text-fitting helpers.
    public static class SimLayout
    {
        public static float Clamp(float value, float min, float max)
        {
            if (max < min) return min;
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // Computes a centered modal frame that never exceeds screen bounds with a minimum outer margin.
        public static SimRect ComputeModalFrame(float screenW, float screenH, float maxW, float maxH, float minW, float minH, float marginX = 24f, float marginY = 24f)
        {
            float availW = Math.Max(200f, screenW - marginX * 2f);
            float availH = Math.Max(200f, screenH - marginY * 2f);
            float w = availW < minW ? availW : Clamp(availW, minW, maxW);
            float h = availH < minH ? availH : Clamp(availH, minH, maxH);
            float x = Math.Max(0f, (screenW - w) * 0.5f);
            float y = Math.Max(0f, (screenH - h) * 0.5f);
            return new SimRect(x, y, w, h);
        }

        // Splits a horizontal container into proportional non-overlapping columns with fixed gaps.
        public static SimRect[] SplitColumns(SimRect container, float[] weights, float gap)
        {
            if (weights == null || weights.Length == 0) return new SimRect[0];
            int n = weights.Length;
            var result = new SimRect[n];
            float totalGap = gap * Math.Max(0, n - 1);
            float usableW = Math.Max(0f, container.Width - totalGap);
            float sumW = 0f;
            for (int i = 0; i < n; i++) sumW += Math.Max(0.001f, weights[i]);

            float curX = container.X;
            for (int i = 0; i < n; i++)
            {
                float colW = (i == n - 1)
                    ? Math.Max(0f, container.Right - curX)
                    : usableW * (Math.Max(0.001f, weights[i]) / sumW);
                result[i] = new SimRect(curX, container.Y, colW, container.Height);
                curX += colW + gap;
            }
            return result;
        }

        // Truncates a single line with "..." so its measured width never exceeds maxWidth.
        public static string Ellipsize(string text, float maxWidth, Func<string, float> measureWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (maxWidth <= 4f || measureWidth == null) return "";
            if (measureWidth(text) <= maxWidth) return text;

            const string dots = "...";
            float dotsW = measureWidth(dots);
            if (dotsW >= maxWidth) return "";

            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                string cand = text.Substring(0, mid).TrimEnd() + dots;
                if (measureWidth(cand) <= maxWidth) lo = mid;
                else hi = mid - 1;
            }
            if (lo <= 0) return dots;
            return text.Substring(0, lo).TrimEnd() + dots;
        }

        // Wraps text into at most maxLines lines, ellipsizing the final line if any text remains.
        public static List<string> WrapClamped(string text, float maxWidth, int maxLines, Func<string, float> measureWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || maxLines <= 0 || maxWidth <= 8f || measureWidth == null)
                return lines;

            var words = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return lines;

            string cur = "";
            int idx = 0;
            while (idx < words.Length)
            {
                string word = words[idx];
                string cand = cur.Length == 0 ? word : cur + " " + word;
                if (measureWidth(cand) <= maxWidth)
                {
                    cur = cand;
                    idx++;
                    continue;
                }

                if (lines.Count + 1 >= maxLines)
                {
                    // Last allowed line: fit as much of the remaining text as possible with ellipsis
                    string remainder = cur.Length == 0
                        ? string.Join(" ", words, idx, words.Length - idx)
                        : cur + " " + string.Join(" ", words, idx, words.Length - idx);
                    lines.Add(Ellipsize(remainder, maxWidth, measureWidth));
                    return lines;
                }

                if (cur.Length > 0)
                {
                    lines.Add(cur);
                    cur = "";
                }
                else
                {
                    // Single very long word wider than maxWidth
                    lines.Add(Ellipsize(word, maxWidth, measureWidth));
                    idx++;
                }
            }

            if (cur.Length > 0 && lines.Count < maxLines)
                lines.Add(cur);
            return lines;
        }
    }

    // Responsive layout for the top-right Radio Dispatch toast.
    public sealed class DispatchToastLayout
    {
        public SimRect Panel;
        public SimRect HeaderBar;
        public SimRect LedRect;
        public SimRect ChannelTextRect;
        public SimRect SignalBarsRect;
        public SimRect TitleRect;
        public SimRect BodyRect;
        public SimRect AnswerBtn;
        public SimRect IgnoreBtn;
        public SimRect TimerTextRect;
        public SimRect TimerBarRect;

        public static DispatchToastLayout Compute(float screenW, float screenH)
        {
            var l = new DispatchToastLayout();
            float margin = screenW < 900f ? 12f : 20f;
            float w = SimLayout.Clamp(screenW - margin * 2f, 320f, 412f);
            float h = 140f;
            float x = Math.Max(margin, screenW - w - margin);
            float y = Math.Min(margin, Math.Max(8f, screenH - h - 8f));

            l.Panel = new SimRect(x, y, w, h);

            // Signature Simitone gradient header strip
            float headerH = 28f;
            l.HeaderBar = new SimRect(x, y, w, headerH);
            l.LedRect = new SimRect(x + 12f, y + 9f, 10f, 10f);
            float sigW = 34f;
            l.SignalBarsRect = new SimRect(l.Panel.Right - sigW - 10f, y + 5f, sigW, 18f);
            l.ChannelTextRect = new SimRect(l.LedRect.Right + 8f, y + 4f, Math.Max(60f, l.SignalBarsRect.Left - (l.LedRect.Right + 8f) - 8f), 20f);

            // Body copy
            float padX = 14f;
            float innerW = w - padX * 2f;
            l.TitleRect = new SimRect(x + padX, l.HeaderBar.Bottom + 7f, innerW, 22f);
            l.BodyRect = new SimRect(x + padX, l.TitleRect.Bottom + 3f, innerW, 20f);

            // Action buttons + timer readout row
            float btnY = l.BodyRect.Bottom + 10f;
            float btnH = 32f;
            float timerW = 40f;
            float gap = 8f;
            float btnsW = Math.Max(140f, innerW - timerW - gap);
            var cols = SimLayout.SplitColumns(new SimRect(x + padX, btnY, btnsW, btnH), new[] { 1.45f, 1.0f }, gap);
            l.AnswerBtn = cols[0];
            l.IgnoreBtn = cols[1];
            l.TimerTextRect = new SimRect(l.IgnoreBtn.Right + gap, btnY + 6f, timerW, 20f);

            // Bottom motive-style countdown bar
            l.TimerBarRect = new SimRect(x + padX, l.Panel.Bottom - 13f, innerW, 8f);
            return l;
        }
    }

    // Responsive layout for the Security Guard Level 1 ("CCTV Night Watch") mini-game and Shift Report.
    public sealed class SecurityWatchLayout
    {
        public bool IsSummary;
        public bool CompactDeck;

        public SimRect Panel;
        public SimRect HeaderBar;
        public SimRect TitleRect;
        public SimRect RoundBadge;
        public SimRect ScoreBadge;
        public SimRect CloseBtn;
        public SimRect SubtitleBar;

        // Active CCTV grid & tactical deck
        public SimRect[] Cameras = new SimRect[4];
        public SimRect DeckPanel;
        public SimRect ReportTextRect;
        public SimRect ClueTextRect;
        public SimRect TimerLabelRect;
        public SimRect TimerBarRect;
        public SimRect AlarmBtn;
        public SimRect ClearBtn;
        public SimRect AwakeLabelRect;
        public SimRect AwakeBarRect;
        public SimRect CoffeeBtn;

        // Summary / Shift Report
        public SimRect SummaryCard;
        public SimRect GradeBadge;
        public SimRect GradeCaptionRect;
        public SimRect SummaryTitleRect;
        public SimRect SummaryBodyRect;
        public SimRect[] StatCards = new SimRect[3];
        public SimRect RewardsCard;
        public SimRect RewardsHeaderRect;
        public SimRect RewardCashRect;
        public SimRect RewardPerfRect;
        public SimRect RewardMotivesRect;
        public SimRect ClaimBtn;

        public static SecurityWatchLayout Compute(float screenW, float screenH, bool isSummary)
        {
            var l = new SecurityWatchLayout { IsSummary = isSummary };
            float marginX = screenW < 960f ? 16f : 32f;
            float marginY = screenH < 700f ? 16f : 28f;
            l.Panel = SimLayout.ComputeModalFrame(screenW, screenH, 920f, 648f, 620f, 480f, marginX, marginY);

            // Top Simitone gradient header bar
            float headerH = 48f;
            l.HeaderBar = new SimRect(l.Panel.X, l.Panel.Y, l.Panel.Width, headerH);

            float closeSz = 30f;
            float padR = 14f;
            float gap = 8f;
            l.CloseBtn = new SimRect(l.Panel.Right - padR - closeSz, l.Panel.Y + (headerH - closeSz) * 0.5f, closeSz, closeSz);

            bool compactHeader = l.Panel.Width < 740f;
            float scoreW = compactHeader ? 94f : 112f;
            float roundW = compactHeader ? 116f : 140f;
            float badgeH = 26f;
            float badgeY = l.Panel.Y + (headerH - badgeH) * 0.5f;

            l.ScoreBadge = new SimRect(l.CloseBtn.Left - gap - scoreW, badgeY, scoreW, badgeH);
            l.RoundBadge = new SimRect(l.ScoreBadge.Left - gap - roundW, badgeY, roundW, badgeH);

            float titleLeft = l.Panel.X + 18f;
            float titleW = Math.Max(120f, l.RoundBadge.Left - titleLeft - 12f);
            l.TitleRect = new SimRect(titleLeft, l.Panel.Y + 9f, titleW, 30f);

            // Clean subtitle strip directly below the gradient header
            l.SubtitleBar = new SimRect(l.Panel.X + 18f, l.HeaderBar.Bottom + 6f, l.Panel.Width - 36f, 20f);

            float contentTop = l.SubtitleBar.Bottom + 8f;
            float contentBottom = l.Panel.Bottom - 16f;
            float contentH = Math.Max(220f, contentBottom - contentTop);
            float padX = 18f;
            float contentW = l.Panel.Width - padX * 2f;

            if (isSummary)
            {
                ComputeSummary(l, new SimRect(l.Panel.X + padX, contentTop, contentW, contentH));
                return l;
            }

            // Tactical deck height is reserved first so cameras always flex cleanly without clipping controls.
            l.CompactDeck = contentW < 700f;
            float deckH = l.CompactDeck ? 156f : 118f;
            float gridToDeckGap = 12f;
            float gridAvailH = Math.Max(180f, contentH - deckH - gridToDeckGap);

            float camGapX = 12f;
            float camGapY = 10f;
            float camW = (contentW - camGapX) * 0.5f;
            float camH = Math.Max(84f, (gridAvailH - camGapY) * 0.5f);

            for (int i = 0; i < 4; i++)
            {
                int col = i % 2;
                int row = i / 2;
                l.Cameras[i] = new SimRect(
                    l.Panel.X + padX + col * (camW + camGapX),
                    contentTop + row * (camH + camGapY),
                    camW,
                    camH);
            }

            float deckY = contentTop + camH * 2f + camGapY + gridToDeckGap;
            l.DeckPanel = new SimRect(l.Panel.X + padX, deckY, contentW, Math.Max(108f, contentBottom - deckY));

            var deckInner = l.DeckPanel.Deflate(14f, 10f);

            // Top row of deck: Event headline & clue on left, Timer label + motive bar on right
            float timerW = SimLayout.Clamp(deckInner.Width * 0.25f, 140f, 205f);
            float reportGap = 14f;
            float reportW = Math.Max(160f, deckInner.Width - timerW - reportGap);

            l.ReportTextRect = new SimRect(deckInner.X, deckInner.Y, reportW, 20f);
            l.ClueTextRect = new SimRect(deckInner.X, l.ReportTextRect.Bottom + 3f, reportW, 20f);

            float timerX = l.ReportTextRect.Right + reportGap;
            l.TimerLabelRect = new SimRect(timerX, deckInner.Y, timerW, 18f);
            l.TimerBarRect = new SimRect(timerX, l.TimerLabelRect.Bottom + 5f, timerW, 14f);

            // Controls area below report/clue
            float ctrlTop = l.ClueTextRect.Bottom + 10f;
            float ctrlH = Math.Max(38f, deckInner.Bottom - ctrlTop);

            if (!l.CompactDeck)
            {
                float btnH = Math.Min(42f, ctrlH);
                float btnY = ctrlTop + (ctrlH - btnH) * 0.5f;
                var cols = SimLayout.SplitColumns(new SimRect(deckInner.X, btnY, deckInner.Width, btnH), new[] { 1.12f, 1.18f, 0.96f, 0.90f }, 10f);
                l.AlarmBtn = cols[0];
                l.ClearBtn = cols[1];

                var awakeCol = cols[2];
                l.AwakeLabelRect = new SimRect(awakeCol.X, awakeCol.Y + 1f, awakeCol.Width, 18f);
                l.AwakeBarRect = new SimRect(awakeCol.X, l.AwakeLabelRect.Bottom + 4f, awakeCol.Width, 14f);

                l.CoffeeBtn = cols[3];
            }
            else
            {
                // Two tidy stacked rows on narrower viewports
                float rowGap = 8f;
                float rowH = Math.Max(34f, (ctrlH - rowGap) * 0.5f);
                var row1 = SimLayout.SplitColumns(new SimRect(deckInner.X, ctrlTop, deckInner.Width, rowH), new[] { 1f, 1f }, 10f);
                l.AlarmBtn = row1[0];
                l.ClearBtn = row1[1];

                var row2 = SimLayout.SplitColumns(new SimRect(deckInner.X, ctrlTop + rowH + rowGap, deckInner.Width, rowH), new[] { 1.45f, 1f }, 10f);
                var awakeCol = row2[0];
                l.AwakeLabelRect = new SimRect(awakeCol.X, awakeCol.Y + 1f, awakeCol.Width, 17f);
                l.AwakeBarRect = new SimRect(awakeCol.X, l.AwakeLabelRect.Bottom + 3f, awakeCol.Width, 13f);
                l.CoffeeBtn = row2[1];
            }

            return l;
        }

        static void ComputeSummary(SecurityWatchLayout l, SimRect area)
        {
            float padSide = area.Width > 740f ? 22f : 6f;
            l.SummaryCard = new SimRect(area.X + padSide, area.Y + 4f, area.Width - padSide * 2f, area.Height - 8f);
            var inner = l.SummaryCard.Deflate(20f, 18f);

            // Top band: Grade badge on left + Title & Summary text on right
            float badgeSize = inner.Width < 600f ? 84f : 104f;
            l.GradeBadge = new SimRect(inner.X, inner.Y, badgeSize, badgeSize);
            l.GradeCaptionRect = new SimRect(inner.X, l.GradeBadge.Bottom + 6f, badgeSize, 18f);

            float rightX = l.GradeBadge.Right + 20f;
            float rightW = Math.Max(180f, inner.Right - rightX);
            l.SummaryTitleRect = new SimRect(rightX, inner.Y, rightW, 26f);
            l.SummaryBodyRect = new SimRect(rightX, l.SummaryTitleRect.Bottom + 6f, rightW, Math.Max(46f, l.GradeCaptionRect.Bottom - (l.SummaryTitleRect.Bottom + 6f)));

            // Middle band: 3 equal-width responsive stat cards spanning full inner width
            float statTop = Math.Max(l.GradeCaptionRect.Bottom, l.SummaryBodyRect.Bottom) + 14f;
            float claimH = 42f;
            float claimY = inner.Bottom - claimH;
            float availMidH = Math.Max(136f, claimY - 14f - statTop);

            float statH = SimLayout.Clamp(availMidH * 0.36f, 54f, 68f);
            l.StatCards = SimLayout.SplitColumns(new SimRect(inner.X, statTop, inner.Width, statH), new[] { 1f, 1f, 1f }, 12f);

            // Rewards card cleanly fits between stat cards and claim button
            float rewTop = statTop + statH + 12f;
            float rewH = Math.Max(82f, claimY - 14f - rewTop);
            l.RewardsCard = new SimRect(inner.X, rewTop, inner.Width, rewH);
            var rewInner = l.RewardsCard.Deflate(16f, 10f);

            l.RewardsHeaderRect = new SimRect(rewInner.X, rewInner.Y, rewInner.Width, 20f);
            var rewCols = SimLayout.SplitColumns(new SimRect(rewInner.X, l.RewardsHeaderRect.Bottom + 6f, rewInner.Width, 22f), new[] { 1f, 1f }, 12f);
            l.RewardCashRect = rewCols[0];
            l.RewardPerfRect = rewCols[1];
            l.RewardMotivesRect = new SimRect(rewInner.X, l.RewardCashRect.Bottom + 5f, rewInner.Width, Math.Max(18f, rewInner.Bottom - (l.RewardCashRect.Bottom + 5f)));

            // Bottom centered Claim button
            float claimW = SimLayout.Clamp(inner.Width * 0.46f, 220f, 300f);
            l.ClaimBtn = new SimRect(inner.X + (inner.Width - claimW) * 0.5f, claimY, claimW, claimH);
        }
    }

    // Responsive layout for the floating Sim Memory dossier window.
    public sealed class SimMemoryLayout
    {
        public bool StackedDossierHeader;

        public SimRect Panel;
        public SimRect HeaderBar;
        public SimRect TitleRect;
        public SimRect CloseBtn;
        public SimRect SubtitleRect;
        public SimRect TabStripRect;

        public SimRect LeftColumn;
        public SimRect LeftHeaderRect;
        public SimRect LeftListRect;
        public int MaxVisibleRows;
        public float RowHeight;
        public float RowGap;

        public SimRect RightColumn;
        public SimRect DossierNameRect;
        public SimRect DossierRelRect;
        public SimRect FamiliarityLabelRect;
        public SimRect FamiliarityBarRect;
        public SimRect DossierBodyRect;

        public SimRect FooterRect;

        public static SimMemoryLayout Compute(float screenW, float screenH)
        {
            var l = new SimMemoryLayout();
            float marginX = screenW < 920f ? 16f : 32f;
            float marginY = screenH < 680f ? 16f : 28f;
            l.Panel = SimLayout.ComputeModalFrame(screenW, screenH, 890f, 600f, 580f, 440f, marginX, marginY);

            // Top Simitone gradient header bar
            float headerH = 46f;
            l.HeaderBar = new SimRect(l.Panel.X, l.Panel.Y, l.Panel.Width, headerH);
            float closeSz = 30f;
            l.CloseBtn = new SimRect(l.Panel.Right - 14f - closeSz, l.Panel.Y + (headerH - closeSz) * 0.5f, closeSz, closeSz);
            l.TitleRect = new SimRect(l.Panel.X + 18f, l.Panel.Y + 8f, Math.Max(140f, l.CloseBtn.Left - (l.Panel.X + 18f) - 12f), 30f);

            float padX = 18f;
            float innerW = l.Panel.Width - padX * 2f;

            // Subtitle + Speaker switcher strip
            l.SubtitleRect = new SimRect(l.Panel.X + padX, l.HeaderBar.Bottom + 6f, innerW, 19f);
            l.TabStripRect = new SimRect(l.Panel.X + padX, l.SubtitleRect.Bottom + 6f, innerW, 30f);

            // Footer strip reserved at the bottom so body text can never collide with it
            float footerH = 20f;
            l.FooterRect = new SimRect(l.Panel.X + padX, l.Panel.Bottom - footerH - 8f, innerW, footerH);

            // Two-column responsive body
            float bodyTop = l.TabStripRect.Bottom + 10f;
            float bodyH = Math.Max(200f, l.FooterRect.Top - 8f - bodyTop);
            float colGap = 14f;
            float leftW = SimLayout.Clamp(innerW * 0.28f, 178f, 238f);
            float rightW = Math.Max(260f, innerW - leftW - colGap);

            l.LeftColumn = new SimRect(l.Panel.X + padX, bodyTop, leftW, bodyH);
            l.LeftHeaderRect = new SimRect(l.LeftColumn.X + 12f, l.LeftColumn.Y + 10f, l.LeftColumn.Width - 24f, 18f);
            l.LeftListRect = new SimRect(l.LeftColumn.X + 8f, l.LeftHeaderRect.Bottom + 8f, l.LeftColumn.Width - 16f, Math.Max(60f, l.LeftColumn.Bottom - (l.LeftHeaderRect.Bottom + 8f) - 10f));
            l.RowHeight = 48f;
            l.RowGap = 6f;
            l.MaxVisibleRows = Math.Max(1, (int)((l.LeftListRect.Height + l.RowGap) / (l.RowHeight + l.RowGap)));

            l.RightColumn = new SimRect(l.LeftColumn.Right + colGap, bodyTop, rightW, bodyH);
            var rInner = l.RightColumn.Deflate(16f, 12f);

            // Dossier header: split side-by-side when wide, or stack neatly when narrow so text NEVER collides with the familiarity bar
            l.StackedDossierHeader = rInner.Width < 430f;
            if (!l.StackedDossierHeader)
            {
                float barW = SimLayout.Clamp(rInner.Width * 0.30f, 138f, 172f);
                float headerGap = 14f;
                float nameW = Math.Max(160f, rInner.Width - barW - headerGap);
                l.DossierNameRect = new SimRect(rInner.X, rInner.Y, nameW, 24f);
                l.DossierRelRect = new SimRect(rInner.X, l.DossierNameRect.Bottom + 4f, nameW, 19f);

                float barX = l.DossierNameRect.Right + headerGap;
                l.FamiliarityLabelRect = new SimRect(barX, rInner.Y + 2f, barW, 18f);
                l.FamiliarityBarRect = new SimRect(barX, l.FamiliarityLabelRect.Bottom + 5f, barW, 14f);

                float dBodyTop = Math.Max(l.DossierRelRect.Bottom, l.FamiliarityBarRect.Bottom) + 12f;
                l.DossierBodyRect = new SimRect(rInner.X, dBodyTop, rInner.Width, Math.Max(80f, rInner.Bottom - dBodyTop));
            }
            else
            {
                l.DossierNameRect = new SimRect(rInner.X, rInner.Y, rInner.Width, 24f);
                l.DossierRelRect = new SimRect(rInner.X, l.DossierNameRect.Bottom + 3f, rInner.Width, 19f);
                float labelW = Math.Min(150f, rInner.Width * 0.45f);
                float famY = l.DossierRelRect.Bottom + 6f;
                l.FamiliarityLabelRect = new SimRect(rInner.X, famY, labelW, 18f);
                l.FamiliarityBarRect = new SimRect(l.FamiliarityLabelRect.Right + 10f, famY + 2f, Math.Max(80f, rInner.Right - (l.FamiliarityLabelRect.Right + 10f)), 14f);

                float dBodyTop = Math.Max(l.FamiliarityLabelRect.Bottom, l.FamiliarityBarRect.Bottom) + 10f;
                l.DossierBodyRect = new SimRect(rInner.X, dBodyTop, rInner.Width, Math.Max(80f, rInner.Bottom - dBodyTop));
            }

            return l;
        }

        public SimRect GetRowRect(int visibleIndex)
        {
            float y = LeftListRect.Y + visibleIndex * (RowHeight + RowGap);
            return new SimRect(LeftListRect.X, y, LeftListRect.Width, RowHeight);
        }
    }
}
