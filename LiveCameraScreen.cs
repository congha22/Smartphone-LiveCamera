// =============================================================================
// LiveCameraScreen.cs  --  Landscape Live Camera App Screen
// =============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SmartphoneLiveCamera.Data;
using StardewValley;
using StardewValley.Menus;

namespace SmartphoneLiveCamera
{
    public class LiveCameraScreen : IClickableMenu
    {
        private const double LiveFeedRefreshSeconds = 0.5;
        private static readonly Color ColorBackground = new Color(12, 14, 22);
        private static readonly Color ColorCard = new Color(28, 34, 52);
        private static readonly Color ColorCardHover = new Color(40, 50, 78);
        private static readonly Color ColorAccent = new Color(80, 200, 140);
        private static readonly Color ColorDanger = new Color(220, 70, 70);
        private static readonly Color ColorAddBtn = new Color(50, 160, 110);
        private static readonly Color ColorAddBtnHover = new Color(70, 200, 140);
        private static readonly Color ColorText = new Color(220, 230, 240);
        private static readonly Color ColorSubText = new Color(140, 155, 175);
        private static readonly Color ColorBorder = new Color(50, 65, 95);

        private enum View { List, Live }
        private View currentView = View.List;

        private readonly ISmartPhoneApi api;
        private readonly Action onBack;
        private readonly List<CameraEntry> cameras;
        private readonly Action saveCallback;
        private CameraEntry? activeCameraEntry;

        private RenderTarget2D? liveFeedTarget;
        private double liveFeedTimer = LiveFeedRefreshSeconds;
        private bool liveFeedHasFrame = false;
        private bool liveFeedCapturing = false;

        // Controller-accessible state
        private float zoomLevel = 1f;
        private bool flashEnabled = false;
        private double captureFlashRemainingSeconds = 0.0;
        private const double CaptureFlashDurationSeconds = 0.45;
        private const float CaptureFlashMaxOpacity = 0.85f;
        private const float ZoomMin = 0.5f;
        private const float ZoomMax = 2.0f;
        private const float ZoomStep = 0.05f;

        /// <summary>Current zoom level for the live feed (0.5x – 2.0x).</summary>
        public float ZoomLevel
        {
            get => zoomLevel;
            set => zoomLevel = Math.Clamp(value, ZoomMin, ZoomMax);
        }

        /// <summary>Whether flash light fires when a photo is captured.</summary>
        public bool FlashEnabled
        {
            get => flashEnabled;
            set => flashEnabled = value;
        }

        /// <summary>The camera entry currently shown in live view, or null.</summary>
        public CameraEntry? ActiveCamera => activeCameraEntry;

        /// <summary>True while the live camera view is active.</summary>
        public bool IsLiveViewActive => currentView == View.Live;

        private float phoneUiScale;
        private int phoneFrameWidth;
        private int phoneFrameHeight;
        private int phoneContentOffsetX;
        private int phoneContentOffsetY;
        private int contentWidth;
        private int contentHeight;
        private Texture2D? phoneFrameTexture;
        private Texture2D? phoneBackgroundTexture;

        private Rectangle LandscapeFrameRect =>
            new(xPositionOnScreen, yPositionOnScreen, phoneFrameHeight, phoneFrameWidth);

        private Rectangle LandscapeContentRect =>
            new(xPositionOnScreen + phoneContentOffsetY,
                yPositionOnScreen + phoneFrameWidth - phoneContentOffsetX - contentWidth,
                contentHeight, contentWidth);

        private bool isDragging; private int dragOffsetX; private int dragOffsetY;
        private int vertScrollOffset = 0; private int maxVertScroll = 0;
        private bool isScrolling; private int scrollStartY; private int lastScrollMouseY; private bool hasScrolled;
        private int hoveredCard = -1; private bool addBtnHover = false; private int hoveredDeleteBtn = -1;

        private readonly List<(Rectangle card, Rectangle deleteBtn, CameraEntry entry)> cardRects = new();
        private Rectangle addBtnRect = Rectangle.Empty;

        public LiveCameraScreen(ISmartPhoneApi api, Action onBack, List<CameraEntry> cameras, Action saveCallback)
            : base()
        {
            this.api = api; this.onBack = onBack; this.cameras = cameras; this.saveCallback = saveCallback;
            var (px, py) = api.GetPhonePosition();
            phoneFrameWidth = api.GetPhoneFrameWidth();
            phoneFrameHeight = api.GetPhoneFrameHeight();
            xPositionOnScreen = px + (phoneFrameWidth - phoneFrameHeight) / 2;
            yPositionOnScreen = py + (phoneFrameHeight - phoneFrameWidth) / 2;
            RefreshLayout();
        }

        private void RefreshLayout()
        {
            phoneUiScale = api.GetPhoneUiScale();
            phoneFrameWidth = api.GetPhoneFrameWidth();
            phoneFrameHeight = api.GetPhoneFrameHeight();
            var (ox, oy) = api.GetPhoneContentOffset();
            phoneContentOffsetX = ox; phoneContentOffsetY = oy;
            phoneFrameTexture = api.GetPhoneFrameTexture();
            phoneBackgroundTexture = api.GetPhoneBackgroundTexture();
            width = phoneFrameHeight; height = phoneFrameWidth;
            if (phoneBackgroundTexture != null && !phoneBackgroundTexture.IsDisposed)
            {
                contentWidth = (int)Math.Round(phoneBackgroundTexture.Width * phoneUiScale);
                contentHeight = (int)Math.Round(phoneBackgroundTexture.Height * phoneUiScale);
            }
            else
            {
                contentWidth = Math.Max(1, phoneFrameWidth - phoneContentOffsetX * 2);
                contentHeight = Math.Max(1, phoneFrameHeight - phoneContentOffsetY - Scale(80));
            }
            RebuildListLayout();
            ReallocLiveFeedTarget();
        }

        private int Scale(int v) => (int)Math.Round(v * phoneUiScale);

        private void SyncPortraitPosition()
        {
            int px = xPositionOnScreen - (phoneFrameWidth - phoneFrameHeight) / 2;
            int py = yPositionOnScreen - (phoneFrameHeight - phoneFrameWidth) / 2;
            api.SetPhonePosition(px, py);
        }

        private void ReallocLiveFeedTarget()
        {
            Rectangle lc = LandscapeContentRect;
            int w = Math.Max(1, lc.Width); int h = Math.Max(1, lc.Height);
            if (liveFeedTarget != null && !liveFeedTarget.IsDisposed && liveFeedTarget.Width == w && liveFeedTarget.Height == h) return;
            liveFeedTarget?.Dispose(); liveFeedTarget = null; liveFeedHasFrame = false;
            try
            {
                liveFeedTarget = new RenderTarget2D(Game1.graphics.GraphicsDevice, w, h, false,
                    Game1.graphics.GraphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.None);
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor?.Log($"LiveCameraScreen: Could not allocate RenderTarget2D ({w}x{h}): {ex.Message}", StardewModdingAPI.LogLevel.Warn);
            }
        }

        private void RebuildListLayout()
        {
            cardRects.Clear();
            Rectangle lc = LandscapeContentRect;
            int padX = Scale(12); int padY = Scale(46); int cardH = Scale(62); int gap = Scale(8); int dBtnW = Scale(36);
            int cardW = lc.Width - padX * 2; int startX = lc.X + padX;
            int y = lc.Y + padY - vertScrollOffset;
            for (int i = 0; i < cameras.Count; i++)
            {
                Rectangle card = new(startX, y, cardW, cardH);
                Rectangle dBtn = new(card.Right - dBtnW - Scale(6), card.Y + (cardH - Scale(26)) / 2, dBtnW, Scale(26));
                cardRects.Add((card, dBtn, cameras[i]));
                y += cardH + gap;
            }
            int addBtnH = Scale(48);
            addBtnRect = new Rectangle(startX, y + Scale(4), cardW, addBtnH);
            int totalH = y + Scale(4) + addBtnH - (lc.Y + padY) + Scale(10);
            maxVertScroll = Math.Max(0, totalH - lc.Height);
        }

        public override void update(GameTime time)
        {
            float cur = api.GetPhoneUiScale();
            if (Math.Abs(cur - phoneUiScale) > 0.001f)
            {
                int cx = xPositionOnScreen + phoneFrameHeight / 2;
                int cy = yPositionOnScreen + phoneFrameWidth / 2;
                phoneUiScale = cur; RefreshLayout();
                xPositionOnScreen = cx - phoneFrameHeight / 2;
                yPositionOnScreen = cy - phoneFrameWidth / 2;
                SyncPortraitPosition();
            }
            base.update(time);
            if (isDragging)
            {
                xPositionOnScreen = Game1.getMouseX() - dragOffsetX;
                yPositionOnScreen = Game1.getMouseY() - dragOffsetY;
                RebuildListLayout(); SyncPortraitPosition();
            }
            if (currentView == View.Live && activeCameraEntry != null && !liveFeedCapturing)
            {
                if (liveFeedTarget == null || liveFeedTarget.IsDisposed) ReallocLiveFeedTarget();
                liveFeedTimer -= time.ElapsedGameTime.TotalSeconds;
                if (liveFeedTimer <= 0) { liveFeedTimer = LiveFeedRefreshSeconds; TryCaptureFrame(); }
            }
            // Count down capture flash
            if (captureFlashRemainingSeconds > 0.0)
                captureFlashRemainingSeconds = Math.Max(0.0, captureFlashRemainingSeconds - time.ElapsedGameTime.TotalSeconds);
        }

        private void TryCaptureFrame()
        {
            if (liveFeedTarget == null || liveFeedTarget.IsDisposed || activeCameraEntry == null) return;
            GameLocation? loc = Game1.getLocationFromName(activeCameraEntry.LocationName);
            if (loc == null) { ModEntry.SMonitor?.Log($"LiveCamera: location '{activeCameraEntry.LocationName}' not found", StardewModdingAPI.LogLevel.Trace); return; }
            liveFeedCapturing = true;
            try { if (api.CaptureLiveFeedFrame(loc, activeCameraEntry.TilePosition, liveFeedTarget, zoomLevel, flashEnabled)) liveFeedHasFrame = true; }
            finally { liveFeedCapturing = false; }
        }

        /// <summary>
        /// Requests a photo capture of the active camera. Triggers flash if enabled.
        /// </summary>
        public void RequestPhotoCapture()
        {
            if (activeCameraEntry == null) return;
            GameLocation? loc = Game1.getLocationFromName(activeCameraEntry.LocationName);
            if (loc == null) return;

            string savedPath = api.CaptureNpcPhoto(
                targetLocation: loc,
                captureCenter:  activeCameraEntry.TilePosition,
                landscape:      true,
                zoomLevel:      zoomLevel,
                forceFlash:     flashEnabled);

            captureFlashRemainingSeconds = CaptureFlashDurationSeconds;
            Game1.playSound("cameraNoise");

            if (!string.IsNullOrEmpty(savedPath))
                Game1.addHUDMessage(new HUDMessage("Photo saved!", HUDMessage.newQuest_type));
        }

        public override void draw(SpriteBatch b)
        {
            b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.55f);
            Rectangle lc = LandscapeContentRect;
            int lx = lc.X; int ly = lc.Y; int lw = lc.Width; int lh = lc.Height;
            if (phoneBackgroundTexture != null && !phoneBackgroundTexture.IsDisposed)
            {
                float sx = (float)contentWidth / phoneBackgroundTexture.Width;
                float sy = (float)contentHeight / phoneBackgroundTexture.Height;
                b.Draw(phoneBackgroundTexture, new Vector2(lx, ly + lh), null, Color.White,
                       -MathHelper.PiOver2, Vector2.Zero, new Vector2(sx, sy), SpriteEffects.None, 0f);
            }
            else { b.Draw(Game1.staminaRect, lc, ColorBackground); }

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    null, new RasterizerState { ScissorTestEnable = true });
            Rectangle prevScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
            Game1.graphics.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(lc, Game1.graphics.GraphicsDevice.Viewport.Bounds);

            if (currentView == View.List) DrawListView(b, lc);
            else DrawLiveView(b, lc);

            b.End();
            Game1.graphics.GraphicsDevice.ScissorRectangle = prevScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

            if (phoneFrameTexture != null && !phoneFrameTexture.IsDisposed)
            {
                float sx = (float)phoneFrameWidth / phoneFrameTexture.Width;
                float sy = (float)phoneFrameHeight / phoneFrameTexture.Height;
                b.Draw(phoneFrameTexture, new Vector2(xPositionOnScreen, yPositionOnScreen + phoneFrameWidth),
                       null, Color.White, -MathHelper.PiOver2, Vector2.Zero, new Vector2(sx, sy), SpriteEffects.None, 0f);
            }
            api.DrawPhoneSizeButtons(b, xPositionOnScreen, yPositionOnScreen, landscape: true);
            drawMouse(b);
        }

        private void DrawListView(SpriteBatch b, Rectangle lc)
        {
            SpriteFont font = Game1.dialogueFont;
            int headerH = Scale(38);
            b.Draw(Game1.staminaRect, new Rectangle(lc.X, lc.Y, lc.Width, headerH), ColorBackground * 0.95f);
            string title = "Live Camera"; float ts = 0.45f * phoneUiScale;
            Vector2 tsz = font.MeasureString(title) * ts;
            b.DrawString(font, title, new Vector2(lc.X + Scale(14), lc.Y + (headerH - tsz.Y) / 2f), ColorAccent, 0f, Vector2.Zero, ts, SpriteEffects.None, 1f);
            string cntStr = $"{cameras.Count} camera{(cameras.Count == 1 ? "" : "s")}"; float cts = 0.32f * phoneUiScale;
            Vector2 ctsz = font.MeasureString(cntStr) * cts;
            b.DrawString(font, cntStr, new Vector2(lc.Right - ctsz.X - Scale(14), lc.Y + (headerH - ctsz.Y) / 2f), ColorSubText, 0f, Vector2.Zero, cts, SpriteEffects.None, 1f);
            b.Draw(Game1.staminaRect, new Rectangle(lc.X, lc.Y + headerH, lc.Width, Scale(1)), ColorBorder);

            for (int i = 0; i < cardRects.Count; i++)
            {
                var (cardRect, deleteRect, entry) = cardRects[i];
                if (cardRect.Bottom < lc.Y || cardRect.Y > lc.Bottom) continue;
                bool hovered = i == hoveredCard;
                b.Draw(Game1.staminaRect, cardRect, hovered ? ColorCardHover : ColorCard);
                b.Draw(Game1.staminaRect, new Rectangle(cardRect.X, cardRect.Y, Scale(4), cardRect.Height), ColorAccent * 0.8f);
                int iconSz = Scale(24);
                Rectangle iconRect = new(cardRect.X + Scale(12), cardRect.Y + (cardRect.Height - iconSz) / 2, iconSz, iconSz);
                b.Draw(Game1.staminaRect, iconRect, ColorAccent * 0.3f);
                b.Draw(Game1.staminaRect, new Rectangle(iconRect.X + Scale(3), iconRect.Y + Scale(3), iconSz - Scale(6), iconSz - Scale(6)), ColorAccent * 0.7f);
                float nts = 0.38f * phoneUiScale;
                string nameStr = entry.Name.Length > 28 ? entry.Name[..25] + "..." : entry.Name;
                b.DrawString(font, nameStr, new Vector2(cardRect.X + Scale(44), cardRect.Y + Scale(10)), ColorText, 0f, Vector2.Zero, nts, SpriteEffects.None, 1f);
                float sts = 0.30f * phoneUiScale;
                string subStr = $"{entry.LocationName}  ({(int)entry.TileX}, {(int)entry.TileY})";
                b.DrawString(font, subStr, new Vector2(cardRect.X + Scale(44), cardRect.Y + Scale(32)), ColorSubText, 0f, Vector2.Zero, sts, SpriteEffects.None, 1f);
                bool delHov = i == hoveredDeleteBtn;
                b.Draw(Game1.staminaRect, deleteRect, delHov ? ColorDanger : new Color(80, 30, 30));
                float dts = 0.30f * phoneUiScale;
                Vector2 dSz = font.MeasureString("X") * dts;
                b.DrawString(font, "X", new Vector2(deleteRect.Center.X - dSz.X / 2f, deleteRect.Center.Y - dSz.Y / 2f), Color.White, 0f, Vector2.Zero, dts, SpriteEffects.None, 1f);
                b.Draw(Game1.staminaRect, new Rectangle(cardRect.X, cardRect.Bottom - Scale(1), cardRect.Width, Scale(1)), ColorBorder * 0.4f);
            }

            if (addBtnRect.Y < lc.Bottom && addBtnRect.Bottom > lc.Y)
            {
                Color fill = addBtnHover ? ColorAddBtnHover : ColorAddBtn;
                b.Draw(Game1.staminaRect, addBtnRect, fill * 0.85f);
                b.Draw(Game1.staminaRect, new Rectangle(addBtnRect.X, addBtnRect.Y, addBtnRect.Width, Scale(2)), ColorAddBtn);
                b.Draw(Game1.staminaRect, new Rectangle(addBtnRect.X, addBtnRect.Bottom - Scale(2), addBtnRect.Width, Scale(2)), ColorAddBtn);
                b.Draw(Game1.staminaRect, new Rectangle(addBtnRect.X, addBtnRect.Y, Scale(2), addBtnRect.Height), ColorAddBtn);
                b.Draw(Game1.staminaRect, new Rectangle(addBtnRect.Right - Scale(2), addBtnRect.Y, Scale(2), addBtnRect.Height), ColorAddBtn);
                string addStr = "+ Add Camera at Current Location"; float ats = 0.36f * phoneUiScale;
                Vector2 aSz = font.MeasureString(addStr) * ats;
                b.DrawString(font, addStr, new Vector2(addBtnRect.Center.X - aSz.X / 2f, addBtnRect.Center.Y - aSz.Y / 2f), Color.White, 0f, Vector2.Zero, ats, SpriteEffects.None, 1f);
            }

            if (cameras.Count == 0)
            {
                string emptyStr = "No cameras placed yet."; float ets = 0.36f * phoneUiScale;
                Vector2 eSz = font.MeasureString(emptyStr) * ets;
                b.DrawString(font, emptyStr, new Vector2(lc.Center.X - eSz.X / 2f, lc.Y + Scale(80) - eSz.Y / 2f), ColorSubText, 0f, Vector2.Zero, ets, SpriteEffects.None, 1f);
            }
        }

        private void DrawLiveView(SpriteBatch b, Rectangle lc)
        {
            SpriteFont font = Game1.dialogueFont;
            if (liveFeedHasFrame && liveFeedTarget != null && !liveFeedTarget.IsDisposed)
            {
                b.Draw(Game1.staminaRect, lc, Color.Black);
                b.Draw(liveFeedTarget, lc, Color.White);
            }
            else
            {
                b.Draw(Game1.staminaRect, lc, ColorBackground);
                string loadStr = "Loading feed..."; float ls = 0.40f * phoneUiScale;
                Vector2 lSz = font.MeasureString(loadStr) * ls;
                b.DrawString(font, loadStr, new Vector2(lc.Center.X - lSz.X / 2f, lc.Center.Y - lSz.Y / 2f), ColorSubText, 0f, Vector2.Zero, ls, SpriteEffects.None, 1f);
            }
            int hudH = Scale(28);
            b.Draw(Game1.staminaRect, new Rectangle(lc.X, lc.Y, lc.Width, hudH), Color.Black * 0.65f);
            string name = activeCameraEntry?.Name ?? "Unknown"; float nts = 0.33f * phoneUiScale;
            b.DrawString(font, name, new Vector2(lc.X + Scale(8), lc.Y + Scale(6)), ColorText, 0f, Vector2.Zero, nts, SpriteEffects.None, 1f);
            double pulse = Math.Sin(Game1.currentGameTime.TotalGameTime.TotalSeconds * 3.0) * 0.3 + 0.7;
            Color liveCol = ColorAccent * (float)pulse;
            int dotSz = Scale(8); int dotX = lc.Right - Scale(60); int dotY = lc.Y + (hudH - dotSz) / 2;
            b.Draw(Game1.staminaRect, new Rectangle(dotX, dotY, dotSz, dotSz), liveCol);
            b.DrawString(font, "LIVE", new Vector2(dotX + dotSz + Scale(4), lc.Y + Scale(7)), liveCol, 0f, Vector2.Zero, 0.28f * phoneUiScale, SpriteEffects.None, 1f);
            int barH = Scale(3); float frac = Math.Clamp((float)(1.0 - liveFeedTimer / LiveFeedRefreshSeconds), 0f, 1f);
            b.Draw(Game1.staminaRect, new Rectangle(lc.X, lc.Bottom - barH, lc.Width, barH), ColorBorder);
            b.Draw(Game1.staminaRect, new Rectangle(lc.X, lc.Bottom - barH, (int)(lc.Width * frac), barH), ColorAccent * 0.8f);

            // Capture flash overlay
            if (captureFlashRemainingSeconds > 0.0 && CaptureFlashDurationSeconds > 0.0)
            {
                float progress = (float)(captureFlashRemainingSeconds / CaptureFlashDurationSeconds);
                float opacity = CaptureFlashMaxOpacity * progress * progress;
                if (opacity > 0f)
                    b.Draw(Game1.staminaRect, lc, Color.White * opacity);
            }
        }

        public void DrawScreenContent(SpriteBatch b, Rectangle dest)
        {
            if (currentView == View.Live && liveFeedHasFrame && liveFeedTarget != null && !liveFeedTarget.IsDisposed)
            {
                b.Draw(Game1.staminaRect, dest, Color.Black);
                b.Draw(liveFeedTarget, dest, Color.White);
            }
            else { Texture2D? bg = api.GetPhoneBackgroundTexture(); if (bg != null && !bg.IsDisposed) b.Draw(bg, dest, Color.White); else b.Draw(Game1.staminaRect, dest, ColorBackground); }
        }

        public override void receiveKeyPress(Keys key)
        {
            if (key == Keys.Escape) { if (currentView == View.Live) GoToList(); else onBack?.Invoke(); return; }
            string ks = key.ToString();
            if (ks == api.GetDecreaseSizeKey()) { api.AdjustPhoneSize(-0.1f); return; }
            if (ks == api.GetIncreaseSizeKey()) { api.AdjustPhoneSize(0.1f); return; }
            base.receiveKeyPress(key);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            LandscapeToPortraitClick(x, y, out int px, out int py);
            int pox = xPositionOnScreen - (phoneFrameWidth - phoneFrameHeight) / 2;
            int poy = yPositionOnScreen - (phoneFrameHeight - phoneFrameWidth) / 2;
            if (api.HandlePhoneAppBottomNavClick(px, py, pox, poy, onBack: currentView == View.Live ? (Action)GoToList : onBack)) return;
            if (api.HandlePhoneSizeButtonsClick(px, py, pox, poy)) return;
            scrollStartY = y; lastScrollMouseY = y; hasScrolled = false; isScrolling = false;
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);
            Rectangle lFrame = LandscapeFrameRect; Rectangle lContent = LandscapeContentRect;
            if (!isDragging && !isScrolling)
            {
                int dy = y - scrollStartY;
                if (lContent.Contains(x, y) && currentView == View.List && Math.Abs(dy) > Scale(4)) isScrolling = true;
                else if (lFrame.Contains(x, y) && !lContent.Contains(x, y)) { isDragging = true; dragOffsetX = x - xPositionOnScreen; dragOffsetY = y - yPositionOnScreen; }
            }
            if (isScrolling && currentView == View.List)
            {
                int delta = y - lastScrollMouseY; lastScrollMouseY = y;
                vertScrollOffset = Math.Clamp(vertScrollOffset - delta, 0, maxVertScroll);
                hasScrolled = true; RebuildListLayout();
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);
            if (!hasScrolled && !isDragging && LandscapeContentRect.Contains(x, y)) HandleTap(x, y);
            isDragging = false; isScrolling = false;
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (currentView == View.List)
            {
                int amount = Scale(40);
                vertScrollOffset = Math.Clamp(vertScrollOffset + (direction > 0 ? -amount : amount), 0, maxVertScroll);
                RebuildListLayout();
            }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            hoveredCard = -1; addBtnHover = false; hoveredDeleteBtn = -1;
            if (currentView != View.List || !LandscapeContentRect.Contains(x, y)) return;
            for (int i = 0; i < cardRects.Count; i++)
            {
                var (cardRect, deleteRect, _) = cardRects[i];
                if (deleteRect.Contains(x, y)) { hoveredDeleteBtn = i; return; }
                if (cardRect.Contains(x, y)) { hoveredCard = i; return; }
            }
            if (addBtnRect.Contains(x, y)) addBtnHover = true;
        }

        private void HandleTap(int x, int y)
        {
            if (currentView == View.Live) return;
            for (int i = 0; i < cardRects.Count; i++)
            {
                var (_, deleteRect, entry) = cardRects[i];
                if (deleteRect.Contains(x, y)) { cameras.Remove(entry); saveCallback(); vertScrollOffset = Math.Clamp(vertScrollOffset, 0, Math.Max(0, maxVertScroll - Scale(70))); RebuildListLayout(); Game1.playSound("trashcan"); return; }
            }
            for (int i = 0; i < cardRects.Count; i++)
            {
                var (cardRect, _, entry) = cardRects[i];
                if (cardRect.Contains(x, y)) { OpenLiveView(entry); Game1.playSound("smallSelect"); return; }
            }
            if (addBtnRect.Contains(x, y)) AddCameraAtPlayerPosition();
        }

        private void AddCameraAtPlayerPosition()
        {
            if (!StardewModdingAPI.Context.IsWorldReady || Game1.player == null || Game1.currentLocation == null) { Game1.addHUDMessage(new HUDMessage("Cannot add camera right now.", HUDMessage.error_type)); return; }
            var entry = new CameraEntry { LocationName = Game1.currentLocation.Name, TileX = Game1.player.TilePoint.X, TileY = Game1.player.TilePoint.Y, Name = $"{Game1.currentLocation.DisplayName} ({Game1.player.TilePoint.X}, {Game1.player.TilePoint.Y})" };
            cameras.Add(entry); saveCallback(); RebuildListLayout();
            Game1.playSound("cameraNoise");
            Game1.addHUDMessage(new HUDMessage($"Camera added: {entry.Name}", HUDMessage.newQuest_type));
        }

        private void OpenLiveView(CameraEntry entry) { activeCameraEntry = entry; liveFeedTimer = 0; currentView = View.Live; ReallocLiveFeedTarget(); api.SetHudPinned(true); Game1.activeClickableMenu = null; }
        private void GoToList() { currentView = View.List; activeCameraEntry = null; RebuildListLayout(); api.SetHudPinned(false); }

        private void LandscapeToPortraitClick(int cx, int cy, out int px, out int py)
        {
            int pox = xPositionOnScreen - (phoneFrameWidth - phoneFrameHeight) / 2;
            int poy = yPositionOnScreen - (phoneFrameHeight - phoneFrameWidth) / 2;
            px = pox + (yPositionOnScreen + phoneFrameWidth - cy);
            py = poy + (cx - xPositionOnScreen);
        }

        protected override void cleanupBeforeExit() { if (!api.IsHudPinned()) { liveFeedTarget?.Dispose(); liveFeedTarget = null; } base.cleanupBeforeExit(); }
    }
}