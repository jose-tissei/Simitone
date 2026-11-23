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

        // Corner Radius Tokens (consistent rounded geometry across all Simitone overlays)
        public const int RadiusModal = 16;
        public const int RadiusToast = 14;
        public const int RadiusCard = 12;
        public const int RadiusCamera = 10;
        public const int RadiusControl = 8;
        public const int RadiusSmall = 5;

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

    // Lazy cache of Simitone's native Content/uigraphics textures + procedural anti-aliased SDF rounded primitives.
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

        readonly Dictionary<int, Texture2D> roundedFills = new Dictionary<int, Texture2D>();
        readonly Dictionary<int, Texture2D> roundedBorders = new Dictionary<int, Texture2D>();
        readonly Dictionary<int, Texture2D> roundedTopGrads = new Dictionary<int, Texture2D>();

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

        // Generates a pure-white premultiplied-alpha 9-slice rounded-rect fill texture (size = 2*r + 4, corner = r + 1).
        public Texture2D GetRoundedFill(GraphicsDevice gd, int radius)
        {
            int r = Math.Max(2, Math.Min(32, radius));
            if (roundedFills.TryGetValue(r, out var existing) && existing != null && !existing.IsDisposed)
                return existing;

            int size = r * 2 + 4;
            float half = size * 0.5f;
            float inner = half - r;
            var data = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float py = y + 0.5f;
                float dy = Math.Abs(py - half) - inner;
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float dx = Math.Abs(px - half) - inner;
                    float dist = (float)Math.Sqrt(Math.Max(dx, 0f) * Math.Max(dx, 0f) + Math.Max(dy, 0f) * Math.Max(dy, 0f))
                                 + Math.Min(Math.Max(dx, dy), 0f) - r;
                    float cov = SimLayout.Clamp(0.5f - dist, 0f, 1f);
                    byte v = (byte)Math.Round(cov * 255f);
                    data[y * size + x] = new Color(v, v, v, v);
                }
            }

            var tex = new Texture2D(gd, size, size);
            tex.SetData(data);
            roundedFills[r] = tex;
            return tex;
        }

        // Generates a pure-white premultiplied-alpha 9-slice hollow rounded border ring texture.
        public Texture2D GetRoundedBorder(GraphicsDevice gd, int radius, int thickness)
        {
            int r = Math.Max(2, Math.Min(32, radius));
            int t = Math.Max(1, Math.Min(4, thickness));
            int key = (r << 4) | t;
            if (roundedBorders.TryGetValue(key, out var existing) && existing != null && !existing.IsDisposed)
                return existing;

            int size = r * 2 + 4;
            float half = size * 0.5f;
            float inner = half - r;
            var data = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                float py = y + 0.5f;
                float dy = Math.Abs(py - half) - inner;
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f;
                    float dx = Math.Abs(px - half) - inner;
                    float dist = (float)Math.Sqrt(Math.Max(dx, 0f) * Math.Max(dx, 0f) + Math.Max(dy, 0f) * Math.Max(dy, 0f))
                                 + Math.Min(Math.Max(dx, dy), 0f) - r;
                    float outerCov = SimLayout.Clamp(0.5f - dist, 0f, 1f);
                    float innerCov = SimLayout.Clamp(0.5f - (dist + t), 0f, 1f);
                    float cov = SimLayout.Clamp(outerCov - innerCov, 0f, 1f);
                    byte v = (byte)Math.Round(cov * 255f);
                    data[y * size + x] = new Color(v, v, v, v);
                }
            }

            var tex = new Texture2D(gd, size, size);
            tex.SetData(data);
            roundedBorders[key] = tex;
            return tex;
        }

        // Generates a 6-slice (top-left, top-mid, top-right + flat bottom row) Simitone gradient header texture
        // whose top-left and top-right corners match the dialog's exact circular corner radius without distortion.
        public Texture2D GetRoundedTopTitleGrad(GraphicsDevice gd, int radius)
        {
            int r = Math.Max(4, Math.Min(28, radius));
            if (roundedTopGrads.TryGetValue(r, out var existing) && existing != null && !existing.IsDisposed)
                return existing;

            int cap = r + 1;
            int midW = 128;
            int w = cap * 2 + midW;
            int h = cap + 4; // top row = cap (1:1 corners), bottom row = 4px (stretches cleanly vertically)

            Color[] ramp = SampleTitleGradientRamp();
            var data = new Color[w * h];

            for (int y = 0; y < h; y++)
            {
                float py = y + 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float px = x + 0.5f;
                    // Map x to [0, 1] along Simitone's signature horizontal mint -> teal -> ocean gradient
                    float u;
                    if (x < cap) u = 0.04f * (x / (float)cap);
                    else if (x >= cap + midW) u = 0.96f + 0.04f * ((x - (cap + midW)) / (float)cap);
                    else u = 0.04f + 0.92f * ((x - cap) / (float)midW);

                    Color c = SampleRamp(ramp, u);

                    // Apply circular SDF mask strictly on the top-left and top-right corners
                    float cov = 1f;
                    if (py < r)
                    {
                        if (px < r)
                        {
                            float dx = r - px;
                            float dy = r - py;
                            float dist = (float)Math.Sqrt(dx * dx + dy * dy) - r;
                            cov = SimLayout.Clamp(0.5f - dist, 0f, 1f);
                        }
                        else if (px > w - r)
                        {
                            float dx = px - (w - r);
                            float dy = r - py;
                            float dist = (float)Math.Sqrt(dx * dx + dy * dy) - r;
                            cov = SimLayout.Clamp(0.5f - dist, 0f, 1f);
                        }
                    }

                    // Premultiplied alpha output
                    float a = (c.A / 255f) * cov;
                    data[y * w + x] = new Color(
                        (byte)Math.Round(c.R * cov),
                        (byte)Math.Round(c.G * cov),
                        (byte)Math.Round(c.B * cov),
                        (byte)Math.Round(a * 255f));
                }
            }

            var tex = new Texture2D(gd, w, h);
            tex.SetData(data);
            roundedTopGrads[r] = tex;
            return tex;
        }

        Color[] SampleTitleGradientRamp()
        {
            if (TitleGrad != null && !TitleGrad.IsDisposed && TitleGrad.Width > 1)
            {
                try
                {
                    var row = new Color[TitleGrad.Width];
                    TitleGrad.GetData(0, new Rectangle(0, Math.Min(TitleGrad.Height - 1, TitleGrad.Height / 2), TitleGrad.Width, 1), row, 0, TitleGrad.Width);
                    return row;
                }
                catch { }
            }
            // Faithful fallback matching native dialog_title_grad.png (mint -> teal -> deep ocean)
            var fallback = new Color[64];
            var c0 = new Color(0, 218, 111);
            var c1 = new Color(0, 152, 134);
            var c2 = new Color(12, 47, 56);
            for (int i = 0; i < fallback.Length; i++)
            {
                float t = i / (float)(fallback.Length - 1);
                fallback[i] = t < 0.5f ? Color.Lerp(c0, c1, t * 2f) : Color.Lerp(c1, c2, (t - 0.5f) * 2f);
            }
            return fallback;
        }

        static Color SampleRamp(Color[] ramp, float u)
        {
            float pos = SimLayout.Clamp(u, 0f, 1f) * (ramp.Length - 1);
            int i0 = (int)pos;
            int i1 = Math.Min(ramp.Length - 1, i0 + 1);
            return Color.Lerp(ramp[i0], ramp[i1], pos - i0);
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

        // Anti-aliased 9-slice rounded rectangle fill (clamped safely for small rects).
        protected void FillRoundedRect(UISpriteBatch batch, SimRect rect, int radius, Color color)
        {
            if (rect.Width <= 0.5f || rect.Height <= 0.5f) return;
            int r = Math.Min(radius, (int)Math.Floor(Math.Min(rect.Width, rect.Height) * 0.5f));
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            if (r < 2)
            {
                FillRect(batch, assets.WhitePx, rect, color);
                return;
            }
            var tex = assets.GetRoundedFill(batch.GraphicsDevice, r);
            DrawNineSlice(batch, tex, r + 1, rect, color);
        }

        // Anti-aliased top-corners-rounded fill (flat bottom edge, fits flush inside rounded containers).
        protected void FillRoundedTopRect(UISpriteBatch batch, SimRect rect, int radius, Color color)
        {
            if (rect.Width <= 0.5f || rect.Height <= 0.5f) return;
            int r = Math.Min(radius, (int)Math.Floor(Math.Min(rect.Width * 0.5f, rect.Height - 1f)));
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            if (r < 2)
            {
                FillRect(batch, assets.WhitePx, rect, color);
                return;
            }
            var tex = assets.GetRoundedFill(batch.GraphicsDevice, r);
            int c = r + 1;
            int mw = tex.Width - c * 2;
            int mh = tex.Height - c * 2;
            float innerW = Math.Max(0f, rect.Width - c * 2f);
            float sx = mw > 0 ? innerW / mw : 1f;
            float topH = Math.Min(c, rect.Height);
            float remH = Math.Max(0f, rect.Height - topH);

            DrawLocalTexture(batch, tex, new Rectangle(0, 0, c, c), new Vector2(rect.X, rect.Y), new Vector2(1f, topH / c), color);
            if (innerW > 0.5f)
                DrawLocalTexture(batch, tex, new Rectangle(c, 0, mw, c), new Vector2(rect.X + c, rect.Y), new Vector2(sx, topH / c), color);
            DrawLocalTexture(batch, tex, new Rectangle(c + mw, 0, c, c), new Vector2(rect.Right - c, rect.Y), new Vector2(1f, topH / c), color);

            if (remH > 0.5f)
                FillRect(batch, assets.WhitePx, rect.X, rect.Y + topH, rect.Width, remH, color);
        }

        // Anti-aliased bottom-corners-rounded fill (flat top edge, fits flush inside rounded monitors/cards).
        protected void FillRoundedBottomRect(UISpriteBatch batch, SimRect rect, int radius, Color color)
        {
            if (rect.Width <= 0.5f || rect.Height <= 0.5f) return;
            int r = Math.Min(radius, (int)Math.Floor(Math.Min(rect.Width * 0.5f, rect.Height - 1f)));
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            if (r < 2)
            {
                FillRect(batch, assets.WhitePx, rect, color);
                return;
            }
            var tex = assets.GetRoundedFill(batch.GraphicsDevice, r);
            int c = r + 1;
            int mw = tex.Width - c * 2;
            int mh = tex.Height - c * 2;
            float innerW = Math.Max(0f, rect.Width - c * 2f);
            float sx = mw > 0 ? innerW / mw : 1f;
            float botH = Math.Min(c, rect.Height);
            float topH = Math.Max(0f, rect.Height - botH);

            if (topH > 0.5f)
                FillRect(batch, assets.WhitePx, rect.X, rect.Y, rect.Width, topH, color);

            float by = rect.Bottom - botH;
            DrawLocalTexture(batch, tex, new Rectangle(0, c + mh, c, c), new Vector2(rect.X, by), new Vector2(1f, botH / c), color);
            if (innerW > 0.5f)
                DrawLocalTexture(batch, tex, new Rectangle(c, c + mh, mw, c), new Vector2(rect.X + c, by), new Vector2(sx, botH / c), color);
            DrawLocalTexture(batch, tex, new Rectangle(c + mw, c + mh, c, c), new Vector2(rect.Right - c, by), new Vector2(1f, botH / c), color);
        }

        // Anti-aliased 9-slice rounded rectangle outline border.
        protected void DrawRoundedBorder(UISpriteBatch batch, SimRect rect, int radius, Color color, int thickness = 1)
        {
            if (rect.Width <= 1f || rect.Height <= 1f) return;
            int r = Math.Min(radius, (int)Math.Floor(Math.Min(rect.Width, rect.Height) * 0.5f));
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            if (r < 2)
            {
                DrawBorder(batch, assets.WhitePx, rect, color, thickness);
                return;
            }
            var tex = assets.GetRoundedBorder(batch.GraphicsDevice, r, thickness);
            DrawNineSlice(batch, tex, r + 1, rect, color);
        }

        // Draws a Simitone-styled rounded translucent dark glass modal with optional Sims ocean-blue veil.
        protected void DrawDialogShell(UISpriteBatch batch, SimRect panel, bool drawVeil, float screenW, float screenH, int cornerRadius = SimUiTokens.RadiusModal)
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

            // Soft rounded outer drop shadow + translucent dark glass body + crisp Sims teal/mint rounded rim
            var shadowRect = new SimRect(panel.X - 4f, panel.Y + 4f, panel.Width + 8f, panel.Height + 6f);
            FillRoundedRect(batch, shadowRect, cornerRadius + 3, Color.Black * 0.36f);
            FillRoundedRect(batch, panel, cornerRadius, SimUiTokens.DialogBg);
            DrawRoundedBorder(batch, new SimRect(panel.X - 1f, panel.Y - 1f, panel.Width + 2f, panel.Height + 2f), cornerRadius + 1, SimUiTokens.OuterBorder, 2);
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

        // Draws Simitone's signature mint-to-teal-to-blue gradient header bar with matching rounded top corners.
        protected void DrawTitleBar(UISpriteBatch batch, SimRect headerBar, SimRect titleRect, string title, TextStyle titleStyle, int cornerRadius = SimUiTokens.RadiusModal)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;
            int r = Math.Min(cornerRadius, (int)Math.Floor(Math.Min(headerBar.Width * 0.45f, headerBar.Height - 2f)));

            if (r >= 4)
            {
                var grad = assets.GetRoundedTopTitleGrad(batch.GraphicsDevice, r);
                int cap = r + 1;
                int midSrcW = grad.Width - cap * 2;
                int botSrcH = grad.Height - cap;
                float innerW = Math.Max(0f, headerBar.Width - cap * 2f);
                float sxMid = midSrcW > 0 ? innerW / midSrcW : 1f;
                float botDstH = Math.Max(0f, headerBar.Height - cap);
                float syBot = botSrcH > 0 ? botDstH / botSrcH : 1f;

                // Top rounded row (1:1 aspect ratio corners!)
                DrawLocalTexture(batch, grad, new Rectangle(0, 0, cap, cap), new Vector2(headerBar.X, headerBar.Y), Vector2.One, Color.White);
                if (innerW > 0.5f)
                    DrawLocalTexture(batch, grad, new Rectangle(cap, 0, midSrcW, cap), new Vector2(headerBar.X + cap, headerBar.Y), new Vector2(sxMid, 1f), Color.White);
                DrawLocalTexture(batch, grad, new Rectangle(cap + midSrcW, 0, cap, cap), new Vector2(headerBar.Right - cap, headerBar.Y), Vector2.One, Color.White);

                // Flat lower band matching exact horizontal gradient
                if (botDstH > 0.5f)
                {
                    DrawLocalTexture(batch, grad, new Rectangle(0, cap, cap, botSrcH), new Vector2(headerBar.X, headerBar.Y + cap), new Vector2(1f, syBot), Color.White);
                    if (innerW > 0.5f)
                        DrawLocalTexture(batch, grad, new Rectangle(cap, cap, midSrcW, botSrcH), new Vector2(headerBar.X + cap, headerBar.Y + cap), new Vector2(sxMid, syBot), Color.White);
                    DrawLocalTexture(batch, grad, new Rectangle(cap + midSrcW, cap, cap, botSrcH), new Vector2(headerBar.Right - cap, headerBar.Y + cap), new Vector2(1f, syBot), Color.White);
                }
            }
            else if (assets.TitleGrad != null)
            {
                DrawLocalTexture(batch, assets.TitleGrad, null,
                    new Vector2(headerBar.X, headerBar.Y),
                    new Vector2(headerBar.Width / assets.TitleGrad.Width, headerBar.Height / assets.TitleGrad.Height),
                    Color.White);
            }
            else
            {
                FillRoundedTopRect(batch, headerBar, Math.Max(2, r), SimUiTokens.TealHighlight);
            }

            // Crisp Sims mint sheen along bottom of header bar
            FillRect(batch, px, headerBar.X + 1f, headerBar.Bottom - 2f, Math.Max(0f, headerBar.Width - 2f), 2f, SimUiTokens.Mint * 0.65f);

            if (!string.IsNullOrEmpty(title))
                DrawFittedLine(batch, title, titleRect, titleStyle, SimTextAlign.Left);
        }

        // Draws a Simitone translucent rounded glass sub-card with matching rounded border and inset Sims top accent.
        protected void DrawGlassCard(UISpriteBatch batch, SimRect rect, Color fillColor, Color? accentTop = null, int cornerRadius = SimUiTokens.RadiusCard)
        {
            var assets = SimUiAssets.Get(batch.GraphicsDevice);
            var px = assets.WhitePx;

            FillRoundedRect(batch, rect, cornerRadius, fillColor);
            DrawRoundedBorder(batch, rect, cornerRadius, SimUiTokens.InnerBorder, 1);
            if (accentTop.HasValue)
            {
                float inset = Math.Min(cornerRadius * 0.75f, rect.Width * 0.2f);
                FillRect(batch, px, rect.X + inset, rect.Y + 1f, Math.Max(0f, rect.Width - inset * 2f), 2f, accentTop.Value);
            }
        }

        void DrawNineSlice(UISpriteBatch batch, Texture2D tex, int corner, SimRect rect, Color color)
        {
            int c = corner;
            int mw = tex.Width - c * 2;
            int mh = tex.Height - c * 2;
            float dstC = Math.Min(c, Math.Min(rect.Width, rect.Height) * 0.5f);
            float sc = dstC / c;
            float innerW = Math.Max(0f, rect.Width - dstC * 2f);
            float innerH = Math.Max(0f, rect.Height - dstC * 2f);
            float sx = mw > 0 ? innerW / mw : 1f;
            float sy = mh > 0 ? innerH / mh : 1f;

            // Top row
            DrawLocalTexture(batch, tex, new Rectangle(0, 0, c, c), new Vector2(rect.X, rect.Y), new Vector2(sc, sc), color);
            if (innerW > 0.25f)
                DrawLocalTexture(batch, tex, new Rectangle(c, 0, mw, c), new Vector2(rect.X + dstC, rect.Y), new Vector2(sx, sc), color);
            DrawLocalTexture(batch, tex, new Rectangle(c + mw, 0, c, c), new Vector2(rect.Right - dstC, rect.Y), new Vector2(sc, sc), color);

            // Middle row
            if (innerH > 0.25f)
            {
                DrawLocalTexture(batch, tex, new Rectangle(0, c, c, mh), new Vector2(rect.X, rect.Y + dstC), new Vector2(sc, sy), color);
                if (innerW > 0.25f)
                    DrawLocalTexture(batch, tex, new Rectangle(c, c, mw, mh), new Vector2(rect.X + dstC, rect.Y + dstC), new Vector2(sx, sy), color);
                DrawLocalTexture(batch, tex, new Rectangle(c + mw, c, c, mh), new Vector2(rect.Right - dstC, rect.Y + dstC), new Vector2(sc, sy), color);
            }

            // Bottom row
            DrawLocalTexture(batch, tex, new Rectangle(0, c + mh, c, c), new Vector2(rect.X, rect.Bottom - dstC), new Vector2(sc, sc), color);
            if (innerW > 0.25f)
                DrawLocalTexture(batch, tex, new Rectangle(c, c + mh, mw, c), new Vector2(rect.X + dstC, rect.Bottom - dstC), new Vector2(sx, sc), color);
            DrawLocalTexture(batch, tex, new Rectangle(c + mw, c + mh, c, c), new Vector2(rect.Right - dstC, rect.Bottom - dstC), new Vector2(sc, sc), color);
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
