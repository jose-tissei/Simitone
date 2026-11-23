using System;
using System.Collections.Generic;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using FSO.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Conversations
{
    public enum SimButtonVariant
    {
        Primary,    // Native Simitone greenbutton.png with dark green text
        Secondary,  // Native Simitone button.png with deep navy text
        Danger,     // Native button.png tinted coral red with crisp white text
        Warning,    // Native button.png tinted warm amber with dark text
        Close       // Native compact dark-glass / icy close pill
    }

    public enum SimTextAlign
    {
        Left,
        Center,
        Right
    }

    // Authentic Simitone / The Sims design tokens aligned with Simitone.Client.UI.Model.UIStyle.DARK
    // and native Content/uigraphics textures (dialog_title_grad, button, greenbutton, motive_bg, money_bg, diag, circle10px).
    public static class SimUiTokens
    {
        // Surfaces & Backdrops (derived from UIStyle.DARK + UIMobileDialog)
        public static readonly Color ModalVeil = new Color(0, 41, 69) * 0.50f;
        public static readonly Color BackStripe = new Color(0, 70, 140) * 0.30f;
        public static readonly Color DialogBg = Color.Black * 0.84f;
        public static readonly Color PanelBg = Color.Black * 0.74f;
        public static readonly Color CardBg = new Color(8, 22, 36, 212);
        public static readonly Color CardBgAlt = new Color(12, 30, 46, 218);
        public static readonly Color RowBg = new Color(14, 30, 46, 185);
        public static readonly Color RowSelectedBg = new Color(10, 74, 82, 232);
        public static readonly Color PillDarkBg = Color.Black * 0.76f;

        // Borders & Dividers
        public static readonly Color OuterBorder = new Color(0, 198, 168, 130);
        public static readonly Color InnerBorder = new Color(36, 118, 148, 125);
        public static readonly Color Divider = new Color(46, 134, 158, 115);

        // Signature Sims Accents (UIStyle.DARK)
        public static readonly Color Mint = new Color(0, 255, 128);        // BtnActive / SecondaryText / PosMoney
        public static readonly Color MintSoft = new Color(92, 236, 168);
        public static readonly Color Cyan = new Color(0, 238, 255);        // SkillActive
        public static readonly Color Amber = new Color(255, 191, 0);       // SkillNeeded
        public static readonly Color Orange = new Color(255, 128, 0);      // NegMoney
        public static readonly Color Danger = new Color(255, 86, 82);      // Alert / Threat / Tense
        public static readonly Color DangerBg = new Color(128, 30, 36, 218);
        public static readonly Color TealHighlight = new Color(24, 168, 144);

        // Typography
        public static readonly Color TextPrimary = Color.White;
        public static readonly Color TextMuted = new Color(184, 212, 226);
        public static readonly Color TextSubtle = new Color(138, 174, 194);
        public static readonly Color DialogTitle = Color.Black;
        public static readonly Color BtnTxt = new Color(0, 31, 63);
        public static readonly Color GreenBtnTxt = new Color(0, 63, 16);
        public static readonly Color WarnBtnTxt = new Color(40, 22, 4);
        public static readonly Color BtnDisable = new Color(146, 158, 168);

        // Button Tints (applied to native 4-state button.png / greenbutton.png)
        public static readonly Color DangerBtnTint = new Color(236, 78, 74);
        public static readonly Color DangerBtnHover = new Color(255, 104, 98);
        public static readonly Color WarnBtnTint = new Color(246, 186, 62);
        public static readonly Color WarnBtnHover = new Color(255, 208, 96);

        // ALWAYS clone TextStyle.DefaultLabel — never construct new TextStyle directly (VFont requirement).
        public static TextStyle MakeStyle(int size, Color color, bool shadow = true)
        {
            var s = TextStyle.DefaultLabel.Clone();
            s.Size = size;
            s.Color = color;
            s.Shadow = shadow;
            return s;
        }
    }

    // Lazy cache of Simitone's native Content/uigraphics textures, with safe white-pixel fallback.
    internal sealed class SimUiAssets
    {
        static GraphicsDevice cachedDevice;
        static SimUiAssets cached;

        public Texture2D WhitePx;
        public Texture2D TitleGrad;
        public Texture2D Button;
        public Texture2D GreenButton;
        public Texture2D MotiveBg;
        public Texture2D MoneyBg;
        public Texture2D Diag;
        public Texture2D Circle10;

        public static SimUiAssets Get(GraphicsDevice gd)
        {
            if (cached != null && ReferenceEquals(cachedDevice, gd) && cached.WhitePx != null && !cached.WhitePx.IsDisposed)
                return cached;

            var a = new SimUiAssets();
            a.WhitePx = TextureGenerator.GetPxWhite(gd);
            a.TitleGrad = TryLoad(gd, "dialog_title_grad.png");
            a.Button = TryLoad(gd, "button.png");
            a.GreenButton = TryLoad(gd, "greenbutton.png");
            a.MotiveBg = TryLoad(gd, "motive_bg.png");
            a.MoneyBg = TryLoad(gd, "money_bg.png");
            a.Diag = TryLoad(gd, "diag.png");
            a.Circle10 = TryLoad(gd, "circle10px.png");

            cachedDevice = gd;
            cached = a;
            return a;
        }

        static Texture2D TryLoad(GraphicsDevice gd, string name)
        {
            try
            {
                var content = Content.Get();
                var item = content?.CustomUI?.Get(name);
                return item != null ? item.Get(gd) : null;
            }
            catch { return null; }
        }
    }

    // Shared UIElement base exposing authentic Simitone drawing primitives and non-overlapping text helpers.
    public abstract class SimUiElement : UIElement
    {
        readonly TextStyle btnDarkTextStyle;
        readonly TextStyle btnGreenTextStyle;
        readonly TextStyle btnWhiteTextStyle;
        readonly TextStyle btnWarnTextStyle;
        readonly TextStyle btnDisabledTextStyle;
        readonly TextStyle closeTextStyle;

        protected SimUiElement()
        {
            btnDarkTextStyle = SimUiTokens.MakeStyle(13, SimUiTokens.BtnTxt, false);
            btnGreenTextStyle = SimUiTokens.MakeStyle(13, SimUiTokens.GreenBtnTxt, false);
            btnWhiteTextStyle = SimUiTokens.MakeStyle(13, Color.White, true);
            btnWarnTextStyle = SimUiTokens.MakeStyle(13, SimUiTokens.WarnBtnTxt, false);
            btnDisabledTextStyle = SimUiTokens.MakeStyle(13, SimUiTokens.BtnDisable, false);
            closeTextStyle = SimUiTokens.MakeStyle(14, Color.White, true);
        }

        protected static void GetScreenSize(UISpriteBatch batch, out float screenW, out float screenH)
        {
            var cur = UIScreen.Current;
            if (cur != null && cur.ScreenWidth > 0 && cur.ScreenHeight > 0)
            {
                screenW = cur.ScreenWidth;
                screenH = cur.ScreenHeight;
                return;
            }
            float scale = Math.Max(0.5f, FSO.Common.FSOEnvironment.DPIScaleFactor);
            screenW = batch.GraphicsDevice.Viewport.Width / scale;
            screenH = batch.GraphicsDevice.Viewport.Height / scale;
        }

        protected static Rectangle ToXnaRect(SimRect r)
        {
            return new Rectangle((int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height));
        }

        protected void FillRect(UISpriteBatch batch, Texture2D px, float x, float y, float w, float h, Color color)
        {
            if (w <= 0f || h <= 0f) return;
            DrawLocalTexture(batch, px, null, new Vector2(x, y), new Vector2(w, h), color);
        }

        protected void FillRect(UISpriteBatch batch, Texture2D px, SimRect r, Color color)
        {
            FillRect(batch, px, r.X, r.Y, r.Width, r.Height, color);
        }

        protected void DrawBorder(UISpriteBatch batch, Texture2D px, SimRect r, Color color, float thickness = 1f)
        {
            if (r.IsEmpty) return;
            FillRect(batch, px, r.X, r.Y, r.Width, thickness, color);
            FillRect(batch, px, r.X, r.Bottom - thickness, r.Width, thickness, color);
            FillRect(batch, px, r.X, r.Y, thickness, r.Height, color);
            FillRect(batch, px, r.Right - thickness, r.Y, thickness, r.Height, color);
        }

        // Draws a Simitone-styled translucent dark glass modal with optional Sims ocean-blue veil and diagonal edge flair.
        protected void DrawDialogShell(UISpriteBatch batch, SimRect panel, bool drawVeil, float screenW, float screenH)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;

            if (drawVeil)
            {
                FillRect(batch, px, 0, 0, screenW, screenH, SimUiTokens.ModalVeil);
                // Subtle diagonal Simitone backdrop band on the left edge (echoing UIMobileDialog.BackStripe)
                float stripeW = Math.Min(panel.X + 42f, screenW * 0.24f);
                if (stripeW > 20f)
                {
                    FillRect(batch, px, 0, 0, stripeW, screenH, SimUiTokens.BackStripe);
                    if (assets.Diag != null)
                        DrawDiagonalEdge(batch, assets.Diag, stripeW, 0, screenH, SimUiTokens.BackStripe);
                }
            }

            // Soft outer drop shadow + crisp Sims teal/mint rim + dark glass body
            FillRect(batch, px, panel.X - 4, panel.Y + 4, panel.Width + 8, panel.Height + 6, Color.Black * 0.36f);
            FillRect(batch, px, panel.X - 1, panel.Y - 1, panel.Width + 2, panel.Height + 2, SimUiTokens.OuterBorder);
            FillRect(batch, px, panel, SimUiTokens.DialogBg);

            // Signature subtle Simitone diagonal trim on right outer edge
            if (assets.Diag != null)
            {
                DrawDiagonalEdge(batch, assets.Diag, panel.Right, panel.Y, panel.Height, SimUiTokens.DialogBg * 0.55f);
            }
        }

        void DrawDiagonalEdge(UISpriteBatch batch, Texture2D diag, float x, float y, float height, Color color)
        {
            int fullTiles = (int)(height / 64f);
            float rem = height - fullTiles * 64f;
            for (int i = 0; i < fullTiles; i++)
                DrawLocalTexture(batch, diag, new Rectangle(0, 0, 32, 64), new Vector2(x, y + i * 64f), Vector2.One, color);
            if (rem >= 1f)
                DrawLocalTexture(batch, diag, new Rectangle(0, 0, 32, Math.Min(64, (int)Math.Ceiling(rem))), new Vector2(x, y + fullTiles * 64f), Vector2.One, color);
        }

        // Draws Simitone's signature mint-to-teal-to-blue gradient header bar with crisp black title text.
        protected void DrawTitleBar(UISpriteBatch batch, SimRect headerBar, SimRect titleRect, string title, TextStyle titleStyle)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;

            if (assets.TitleGrad != null)
            {
                DrawLocalTexture(batch, assets.TitleGrad, null,
                    new Vector2(headerBar.X, headerBar.Y),
                    new Vector2(headerBar.Width / assets.TitleGrad.Width, headerBar.Height / assets.TitleGrad.Height),
                    Color.White);
            }
            else
            {
                FillRect(batch, px, headerBar, SimUiTokens.TealHighlight);
            }

            // Crisp Sims mint sheen along bottom of header bar
            FillRect(batch, px, headerBar.X, headerBar.Bottom - 2f, headerBar.Width, 2f, SimUiTokens.Mint * 0.65f);

            if (!string.IsNullOrEmpty(title))
                DrawFittedLine(batch, title, titleRect, titleStyle, SimTextAlign.Left);
        }

        // Draws a Simitone translucent glass sub-card with crisp border and optional Sims accent top edge.
        protected void DrawGlassCard(UISpriteBatch batch, SimRect rect, Color fillColor, Color? accentTop = null)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;

            if (assets.Circle10 != null && fillColor.R == 0 && fillColor.G == 0 && fillColor.B == 0 && rect.Width >= 24f && rect.Height >= 24f)
            {
                DrawNineSlice(batch, assets.Circle10, 10, rect, Color.White * (fillColor.A / 255f));
            }
            else
            {
                FillRect(batch, px, rect, fillColor);
            }

            DrawBorder(batch, px, rect, SimUiTokens.InnerBorder, 1f);
            if (accentTop.HasValue)
                FillRect(batch, px, rect.X + 1f, rect.Y, Math.Max(0f, rect.Width - 2f), 2f, accentTop.Value);
        }

        void DrawNineSlice(UISpriteBatch batch, Texture2D tex, int corner, SimRect rect, Color color)
        {
            int c = corner;
            int mw = tex.Width - c * 2;
            int mh = tex.Height - c * 2;
            float innerW = Math.Max(0f, rect.Width - c * 2f);
            float innerH = Math.Max(0f, rect.Height - c * 2f);
            float sx = mw > 0 ? innerW / mw : 1f;
            float sy = mh > 0 ? innerH / mh : 1f;

            // Top row
            DrawLocalTexture(batch, tex, new Rectangle(0, 0, c, c), new Vector2(rect.X, rect.Y), Vector2.One, color);
            DrawLocalTexture(batch, tex, new Rectangle(c, 0, mw, c), new Vector2(rect.X + c, rect.Y), new Vector2(sx, 1f), color);
            DrawLocalTexture(batch, tex, new Rectangle(c + mw, 0, c, c), new Vector2(rect.Right - c, rect.Y), Vector2.One, color);

            // Middle row
            DrawLocalTexture(batch, tex, new Rectangle(0, c, c, mh), new Vector2(rect.X, rect.Y + c), new Vector2(1f, sy), color);
            DrawLocalTexture(batch, tex, new Rectangle(c, c, mw, mh), new Vector2(rect.X + c, rect.Y + c), new Vector2(sx, sy), color);
            DrawLocalTexture(batch, tex, new Rectangle(c + mw, c, c, mh), new Vector2(rect.Right - c, rect.Y + c), new Vector2(1f, sy), color);

            // Bottom row
            DrawLocalTexture(batch, tex, new Rectangle(0, c + mh, c, c), new Vector2(rect.X, rect.Bottom - c), Vector2.One, color);
            DrawLocalTexture(batch, tex, new Rectangle(c, c + mh, mw, c), new Vector2(rect.X + c, rect.Bottom - c), new Vector2(sx, 1f), color);
            DrawLocalTexture(batch, tex, new Rectangle(c + mw, c + mh, c, c), new Vector2(rect.Right - c, rect.Bottom - c), Vector2.One, color);
        }

        // Draws a native 3-slice Simitone pill button using greenbutton.png or button.png (600x100 = 4 states of 150x100).
        protected void DrawPillButton(UISpriteBatch batch, SimRect rect, string label, SimButtonVariant variant, bool hovered, bool enabled = true)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;

            if (variant == SimButtonVariant.Close)
            {
                Color bg = hovered ? new Color(210, 56, 62, 238) : Color.Black * 0.68f;
                if (assets.MoneyBg != null)
                    DrawThreeSlice(batch, assets.MoneyBg, 12, rect, bg);
                else
                    FillRect(batch, px, rect, bg);
                DrawFittedLine(batch, string.IsNullOrEmpty(label) ? "X" : label, rect, closeTextStyle, SimTextAlign.Center);
                return;
            }

            Texture2D tex = (variant == SimButtonVariant.Primary) ? assets.GreenButton : assets.Button;
            int frame = !enabled ? 3 : (hovered ? 2 : 0);

            Color tint = Color.White;
            TextStyle textStyle;
            if (!enabled)
            {
                tint = new Color(92, 108, 122, 215);
                textStyle = btnDisabledTextStyle;
            }
            else
            {
                switch (variant)
                {
                    case SimButtonVariant.Primary:
                        textStyle = btnGreenTextStyle;
                        break;
                    case SimButtonVariant.Danger:
                        tint = hovered ? SimUiTokens.DangerBtnHover : SimUiTokens.DangerBtnTint;
                        textStyle = btnWhiteTextStyle;
                        break;
                    case SimButtonVariant.Warning:
                        tint = hovered ? SimUiTokens.WarnBtnHover : SimUiTokens.WarnBtnTint;
                        textStyle = btnWarnTextStyle;
                        break;
                    default:
                        textStyle = btnDarkTextStyle;
                        break;
                }
            }

            if (tex != null && tex.Width >= 600 && tex.Height >= 50)
            {
                int stateW = tex.Width / 4; // 150
                int stateH = tex.Height;    // 100
                int srcX = frame * stateW;
                int capSrc = 38;
                int midSrc = stateW - capSrc * 2;
                float sy = rect.Height / stateH;
                float capDst = Math.Min(rect.Width * 0.44f, capSrc * sy);
                float sxCap = capDst / capSrc;
                float midDst = Math.Max(0f, rect.Width - capDst * 2f);
                float sxMid = midSrc > 0 ? midDst / midSrc : 0f;

                DrawLocalTexture(batch, tex, new Rectangle(srcX, 0, capSrc, stateH),
                    new Vector2(rect.X, rect.Y), new Vector2(sxCap, sy), tint);
                if (midDst > 0.5f)
                    DrawLocalTexture(batch, tex, new Rectangle(srcX + capSrc, 0, midSrc, stateH),
                        new Vector2(rect.X + capDst, rect.Y), new Vector2(sxMid, sy), tint);
                DrawLocalTexture(batch, tex, new Rectangle(srcX + stateW - capSrc, 0, capSrc, stateH),
                    new Vector2(rect.Right - capDst, rect.Y), new Vector2(sxCap, sy), tint);
            }
            else
            {
                Color fallbackBg = variant == SimButtonVariant.Primary ? SimUiTokens.Mint
                                 : (variant == SimButtonVariant.Danger ? SimUiTokens.DangerBtnTint
                                 : (variant == SimButtonVariant.Warning ? SimUiTokens.WarnBtnTint : Color.White * 0.92f));
                FillRect(batch, px, rect, enabled ? fallbackBg : SimUiTokens.RowBg);
            }

            var textBounds = new SimRect(rect.X + 8f, rect.Y, Math.Max(8f, rect.Width - 16f), rect.Height);
            DrawFittedLine(batch, label, textBounds, textStyle, SimTextAlign.Center);
        }

        // Draws a native 3-slice Simitone pill badge using money_bg.png (36x24, 12px caps).
        protected void DrawPillBadge(UISpriteBatch batch, SimRect rect, string text, Color bgTint, TextStyle textStyle)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;

            if (assets.MoneyBg != null)
                DrawThreeSlice(batch, assets.MoneyBg, 12, rect, bgTint);
            else
                FillRect(batch, px, rect, bgTint);

            var textBounds = new SimRect(rect.X + 8f, rect.Y, Math.Max(8f, rect.Width - 16f), rect.Height);
            DrawFittedLine(batch, text, textBounds, textStyle, SimTextAlign.Center);
        }

        // Draws an authentic Simitone motive progress bar using motive_bg.png (30x20, 10px 3-slice)
        // with exact UIValueBar green-to-red interpolation and leading white highlight edge.
        protected void DrawMotiveBar(UISpriteBatch batch, SimRect rect, float fraction, bool overrideDanger = false)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;
            float p = SimLayout.Clamp(fraction, 0f, 1f);
            float colorP = overrideDanger ? Math.Min(p, 0.15f) : p;

            Color barcol = new Color(
                (byte)(57 * (1f - colorP)),
                (byte)(213 * colorP + 97 * (1f - colorP)),
                (byte)(49 * colorP + 90 * (1f - colorP)));
            Color bgcol = new Color(
                (byte)(57 * colorP + 214 * (1f - colorP)),
                (byte)(97 * colorP),
                (byte)(90 * colorP));

            if (assets.MotiveBg != null)
            {
                DrawThreeSlice(batch, assets.MotiveBg, assets.MotiveBg.Width / 3, rect, bgcol);
                float activeW = rect.Width * p;
                if (activeW >= 4f)
                {
                    var activeRect = new SimRect(rect.X, rect.Y, activeW, rect.Height);
                    DrawThreeSlice(batch, assets.MotiveBg, assets.MotiveBg.Width / 3, activeRect, Color.White);
                    if (activeW >= 6f)
                    {
                        var fillRect = new SimRect(rect.X, rect.Y, Math.Max(2f, activeW - 2f), rect.Height);
                        DrawThreeSlice(batch, assets.MotiveBg, assets.MotiveBg.Width / 3, fillRect, barcol);
                    }
                }
            }
            else
            {
                FillRect(batch, px, rect, bgcol);
                FillRect(batch, px, rect.X, rect.Y, rect.Width * p, rect.Height, barcol);
            }
        }

        void DrawThreeSlice(UISpriteBatch batch, Texture2D tex, int capSrc, SimRect rect, Color color)
        {
            if (rect.Width <= 0.5f || rect.Height <= 0.5f) return;
            int midSrc = Math.Max(1, tex.Width - capSrc * 2);
            float sy = rect.Height / tex.Height;
            float capDst = Math.Min(rect.Width * 0.5f, capSrc * sy);
            float sxCap = capDst / capSrc;
            float midDst = Math.Max(0f, rect.Width - capDst * 2f);

            DrawLocalTexture(batch, tex, new Rectangle(0, 0, capSrc, tex.Height),
                new Vector2(rect.X, rect.Y), new Vector2(sxCap, sy), color);
            if (midDst > 0.5f)
                DrawLocalTexture(batch, tex, new Rectangle(capSrc, 0, midSrc, tex.Height),
                    new Vector2(rect.X + capDst, rect.Y), new Vector2(midDst / midSrc, sy), color);
            DrawLocalTexture(batch, tex, new Rectangle(tex.Width - capSrc, 0, capSrc, tex.Height),
                new Vector2(rect.Right - capDst, rect.Y), new Vector2(sxCap, sy), color);
        }

        // Draws a single line guaranteed to fit inside rect (ellipsized if needed, vertically centered).
        protected void DrawFittedLine(UISpriteBatch batch, string text, SimRect rect, TextStyle style, SimTextAlign align = SimTextAlign.Left)
        {
            if (string.IsNullOrEmpty(text) || rect.Width <= 4f || rect.Height <= 4f) return;
            string fitted = SimLayout.Ellipsize(text, rect.Width, s => style.MeasureString(s).X);
            if (string.IsNullOrEmpty(fitted)) return;

            var sz = style.MeasureString(fitted);
            float x;
            if (align == SimTextAlign.Center) x = rect.X + Math.Max(0f, (rect.Width - sz.X) * 0.5f);
            else if (align == SimTextAlign.Right) x = rect.Right - sz.X;
            else x = rect.X;

            float y = rect.Y + Math.Max(0f, (rect.Height - sz.Y) * 0.5f);
            DrawLocalString(batch, fitted, new Vector2(x, y), style);
        }

        // Draws wrapped text strictly clamped to rect width, rect bottom, and maxLines. Returns the new Y cursor.
        protected float DrawFittedWrapped(UISpriteBatch batch, string text, SimRect rect, TextStyle style, int maxLines = 4)
        {
            if (string.IsNullOrWhiteSpace(text) || rect.Width <= 8f || rect.Height <= 8f) return rect.Y;
            float lineH = Math.Max(style.Size + 4f, style.MeasureString("Ag").Y + 2f);
            int capLines = Math.Min(maxLines, Math.Max(0, (int)Math.Floor((rect.Height + 2f) / lineH)));
            if (capLines <= 0) return rect.Y;

            var lines = SimLayout.WrapClamped(text, rect.Width, capLines, s => style.MeasureString(s).X);
            float curY = rect.Y;
            for (int i = 0; i < lines.Count; i++)
            {
                if (curY + lineH - 2f > rect.Bottom) break;
                DrawLocalString(batch, lines[i], new Vector2(rect.X, curY), style);
                curY += lineH;
            }
            return curY;
        }
    }
}
