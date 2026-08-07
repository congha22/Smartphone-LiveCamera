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
        private double liveFeedTimer = 0.0;
        private bool liveFeedHasFrame = false;
        private bool liveFeedCapturing = false;

        // Paged 2x2 Grid State & CCTV Captures
        private int currentPage = 0;
        private const int ItemsPerPage = 4;
        private int TotalPages => Math.Max(1, (int)Math.Ceiling(cameras.Count / (double)ItemsPerPage));

        private readonly RenderTarget2D?[] gridFeedTargets = new RenderTarget2D?[ItemsPerPage];
        private readonly bool[] gridFeedHasFrame = new bool[ItemsPerPage];

        private Rectangle prevPageBtnRect = Rectangle.Empty;
        private Rectangle nextPageBtnRect = Rectangle.Empty;
        private bool prevPageBtnHover = false;
        private bool nextPageBtnHover = false;

        // Controller-accessible state
        private float zoomLevel = 1f;
        private bool flashEnabled = false;
        private double captureFlashRemainingSeconds = 0.0;
        private const double CaptureFlashDurationSeconds = 0.45;
        private const float CaptureFlashMaxOpacity = 0.85f;
        private const float ZoomMin = 0.5f;
        private const float ZoomMax = 2.0f;
        private const float ZoomStep = 0.05f;

        /// <summary>Current zoom level for the live feed (0.5x – 2.0x), persisted per camera.</summary>
        public float ZoomLevel
        {
            get => activeCameraEntry != null ? activeCameraEntry.ZoomLevel : zoomLevel;
            set
            {
                float val = Math.Clamp(value, ZoomMin, ZoomMax);
                zoomLevel = val;
                if (activeCameraEntry != null)
                {
                    activeCameraEntry.ZoomLevel = val;
                    saveCallback();
                }
            }
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
            RefreshPageCaptures();
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

        private void ReallocGridFeedTargets()
        {
            Rectangle lc = LandscapeContentRect;
            int w = Math.Max(1, lc.Width / 2);
            int h = Math.Max(1, lc.Height / 2);
            for (int i = 0; i < ItemsPerPage; i++)
            {
                if (gridFeedTargets[i] != null && !gridFeedTargets[i]!.IsDisposed && gridFeedTargets[i]!.Width == w && gridFeedTargets[i]!.Height == h)
                    continue;

                gridFeedTargets[i]?.Dispose();
                gridFeedTargets[i] = null;
                gridFeedHasFrame[i] = false;
                try
                {
                    gridFeedTargets[i] = new RenderTarget2D(Game1.graphics.GraphicsDevice, w, h, false,
                        Game1.graphics.GraphicsDevice.PresentationParameters.BackBufferFormat, DepthFormat.None);
                }
                catch (Exception ex)
                {
                    ModEntry.SMonitor?.Log($"LiveCameraScreen: Could not allocate grid RenderTarget2D ({w}x{h}): {ex.Message}", StardewModdingAPI.LogLevel.Warn);
                }
            }
        }

        private void RefreshPageCaptures()
        {
            for (int i = 0; i < ItemsPerPage; i++)
            {
                gridFeedHasFrame[i] = false;
                int camIdx = currentPage * ItemsPerPage + i;
                if (camIdx >= cameras.Count) continue;

                CameraEntry entry = cameras[camIdx];
                GameLocation? loc = Game1.getLocationFromName(entry.LocationName);
                if (loc == null) continue;

                RenderTarget2D? target = gridFeedTargets[i];
                if (target == null || target.IsDisposed) continue;

                float z = entry.ZoomLevel > 0f ? entry.ZoomLevel : 1.0f;
                try
                {
                    if (api.CaptureLiveFeedFrame(loc, entry.TilePosition, target, z, forceFlash: false))
                    {
                        gridFeedHasFrame[i] = true;
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.SMonitor?.Log($"LiveCameraScreen: Failed to capture grid frame for '{entry.Name}': {ex.Message}", StardewModdingAPI.LogLevel.Trace);
                }
            }
        }

        private void RebuildListLayout()
        {
            cardRects.Clear();
            Rectangle lc = LandscapeContentRect;
            int padX = Scale(10);
            int headerH = Scale(56);
            int padY = Scale(8);
            int gapX = Scale(8);
            int gapY = Scale(8);
            int dBtnW = Scale(26);
            int dBtnH = Scale(26);

            int colWidth = (lc.Width - padX * 2 - gapX) / 2;
            int rowHeight = (lc.Height - headerH - padY * 2 - gapY) / 2;
            int startX = lc.X + padX;
            int startY = lc.Y + headerH + padY;

            ReallocGridFeedTargets();

            if (currentPage >= TotalPages)
                currentPage = Math.Max(0, TotalPages - 1);

            int startIndex = currentPage * ItemsPerPage;
            int endIndex = Math.Min(cameras.Count, startIndex + ItemsPerPage);

            for (int idx = startIndex; idx < endIndex; idx++)
            {
                int pageOffset = idx - startIndex;
                int col = pageOffset % 2;
                int row = pageOffset / 2;
                int cardX = startX + col * (colWidth + gapX);
                int cardY = startY + row * (rowHeight + gapY);

                Rectangle card = new(cardX, cardY, colWidth, rowHeight);
                Rectangle dBtn = new(card.Right - dBtnW - Scale(4), card.Y + Scale(4), dBtnW, dBtnH);
                cardRects.Add((card, dBtn, cameras[idx]));
            }

            // Top Header Navigation & Action buttons
            SpriteFont font = Game1.dialogueFont;
            string title = "Live Camera";
            float ts = 0.54f * phoneUiScale;
            Vector2 tsz = font.MeasureString(title) * ts;

            int navBtnW = Scale(30);
            int navBtnH = Scale(30);
            int navY = lc.Y + (headerH - navBtnH) / 2;

            int titleRight = lc.X + Scale(14) + (int)tsz.X;
            prevPageBtnRect = new Rectangle(titleRight + Scale(14), navY, navBtnW, navBtnH);

            string pageStr = $"{currentPage + 1}/{TotalPages}";
            float pts = 0.44f * phoneUiScale;
            Vector2 psz = font.MeasureString(pageStr) * pts;

            nextPageBtnRect = new Rectangle(prevPageBtnRect.Right + (int)psz.X + Scale(16), navY, navBtnW, navBtnH);

            // "+ Add Camera" Button on the right side of the header banner
            int addBtnW = Scale(140);
            int addBtnH = Scale(34);
            addBtnRect = new Rectangle(lc.Right - addBtnW - Scale(10), lc.Y + (headerH - addBtnH) / 2, addBtnW, addBtnH);
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
                if (IsPlayerInActiveCameraLocation)
                {
                    liveFeedTimer = LiveFeedRefreshSeconds;
                }
                else
                {
                    if (liveFeedTarget == null || liveFeedTarget.IsDisposed) ReallocLiveFeedTarget();
                    liveFeedTimer -= time.ElapsedGameTime.TotalSeconds;
                    if (liveFeedTimer <= 0) { liveFeedTimer = LiveFeedRefreshSeconds; TryCaptureFrame(); }
                }
            }
            // Count down capture flash
            if (captureFlashRemainingSeconds > 0.0)
                captureFlashRemainingSeconds = Math.Max(0.0, captureFlashRemainingSeconds - time.ElapsedGameTime.TotalSeconds);
        }

        /// <summary>
        /// Instantly resets the live feed update timer to 0, forcing an immediate frame refresh.
        /// </summary>
        public void ForceImmediateFrameRefresh()
        {
            liveFeedTimer = 0.0;
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

            int headerH = Scale(56);

            if (currentView == View.List)
            {
                b.End();
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                        null, new RasterizerState { ScissorTestEnable = true });
                Rectangle prevScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
                Rectangle scrollArea = new Rectangle(lc.X, lc.Y + headerH, lc.Width, lc.Height - headerH);
                Game1.graphics.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(scrollArea, Game1.graphics.GraphicsDevice.Viewport.Bounds);

                DrawListView(b, lc);

                b.End();
                Game1.graphics.GraphicsDevice.ScissorRectangle = prevScissor;
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);

                DrawListHeader(b, lc);
            }
            else
            {
                b.End();
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                        null, new RasterizerState { ScissorTestEnable = true });
                Rectangle prevScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
                Game1.graphics.GraphicsDevice.ScissorRectangle = Rectangle.Intersect(lc, Game1.graphics.GraphicsDevice.Viewport.Bounds);

                DrawLiveView(b, lc);

                b.End();
                Game1.graphics.GraphicsDevice.ScissorRectangle = prevScissor;
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            }

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
            int headerH = Scale(56);
            int scissoredTop = lc.Y + headerH;

            for (int i = 0; i < cardRects.Count; i++)
            {
                var (cardRect, deleteRect, entry) = cardRects[i];
                if (cardRect.Bottom < scissoredTop || cardRect.Y > lc.Bottom) continue;

                bool hovered = i == hoveredCard;

                // Base card frame
                CardDrawing.DrawCard(api, b, cardRect, hovered ? new Color(40, 50, 75) : ColorCard, scale: 0.70f * phoneUiScale);

                // CCTV live feed frame thumbnail (scaled down preserving aspect ratio and centered)
                if (i < gridFeedHasFrame.Length && gridFeedHasFrame[i] && gridFeedTargets[i] != null && !gridFeedTargets[i]!.IsDisposed)
                {
                    Texture2D tex = gridFeedTargets[i]!;
                    float targetRatio = (float)tex.Width / tex.Height;
                    float cardRatio = (float)cardRect.Width / cardRect.Height;

                    int fitW, fitH;
                    if (cardRatio > targetRatio)
                    {
                        fitH = cardRect.Height;
                        fitW = (int)Math.Round(fitH * targetRatio);
                    }
                    else
                    {
                        fitW = cardRect.Width;
                        fitH = (int)Math.Round(fitW / targetRatio);
                    }

                    int destX = cardRect.X + (cardRect.Width - fitW) / 2;
                    int destY = cardRect.Y + (cardRect.Height - fitH) / 2;
                    Rectangle destRect = new Rectangle(destX, destY, fitW, fitH);

                    b.Draw(Game1.staminaRect, cardRect, new Color(15, 20, 32));
                    b.Draw(tex, destRect, Color.White);
                }
                else
                {
                    b.Draw(Game1.staminaRect, cardRect, new Color(15, 20, 32));
                    string loadMsg = "No Signal";
                    float ls = 0.38f * phoneUiScale;
                    Vector2 lsz = font.MeasureString(loadMsg) * ls;
                    b.DrawString(font, loadMsg, new Vector2(cardRect.Center.X - lsz.X / 2f, cardRect.Center.Y - lsz.Y / 2f), ColorSubText * 0.7f, 0f, Vector2.Zero, ls, SpriteEffects.None, 1f);
                }

                // Hover accent border overlay
                if (hovered)
                {
                    DrawBorder(b, cardRect, ColorAccent, Scale(2));
                }

                // CCTV Camera Badge at top-left
                int camNum = currentPage * ItemsPerPage + i + 1;
                string badgeStr = $"CAM {camNum:D2}";
                float bts = 0.36f * phoneUiScale;
                Vector2 bsz = font.MeasureString(badgeStr) * bts;
                Rectangle badgeBox = new Rectangle(cardRect.X + Scale(4), cardRect.Y + Scale(4), (int)bsz.X + Scale(10), (int)bsz.Y + Scale(4));
                b.Draw(Game1.staminaRect, badgeBox, Color.Black * 0.65f);
                b.DrawString(font, badgeStr, new Vector2(badgeBox.X + Scale(5), badgeBox.Y + Scale(2)), ColorAccent, 0f, Vector2.Zero, bts, SpriteEffects.None, 1f);

                // Bottom dark gradient banner for Camera Display Name
                int bannerH = Scale(24);
                Rectangle bannerRect = new Rectangle(cardRect.X, cardRect.Bottom - bannerH, cardRect.Width, bannerH);
                b.Draw(Game1.staminaRect, bannerRect, Color.Black * 0.70f);

                float nameScale = 0.40f * phoneUiScale;
                float maxTextW = cardRect.Width - Scale(12);
                string displayName = entry.Name;
                string shownName = displayName;
                if (font.MeasureString(displayName).X * nameScale > maxTextW)
                {
                    int len = displayName.Length;
                    while (len > 0 && font.MeasureString(displayName[..len] + "...").X * nameScale > maxTextW)
                    {
                        len--;
                    }
                    shownName = len > 0 ? displayName[..len] + "..." : "";
                }
                Vector2 nSz = font.MeasureString(shownName) * nameScale;
                b.DrawString(font, shownName, new Vector2(bannerRect.X + Scale(6), bannerRect.Y + (bannerH - nSz.Y) / 2f), Color.White, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 1f);

                // Delete 'X' Button on top-right of card
                bool delHov = i == hoveredDeleteBtn;
                Color delCol = delHov ? new Color(220, 50, 50) : new Color(170, 70, 70);
                b.Draw(Game1.staminaRect, deleteRect, delCol * 0.85f);

                float dts = 0.36f * phoneUiScale;
                Vector2 dSz = font.MeasureString("X") * dts;
                b.DrawString(font, "X", new Vector2(deleteRect.Center.X - dSz.X / 2f, deleteRect.Center.Y - dSz.Y / 2f), Color.White, 0f, Vector2.Zero, dts, SpriteEffects.None, 1f);
            }

            if (cameras.Count == 0)
            {
                string emptyStr = "No cameras placed yet.";
                float ets = 0.48f * phoneUiScale;
                Vector2 eSz = font.MeasureString(emptyStr) * ets;
                b.DrawString(font, emptyStr, new Vector2(lc.Center.X - eSz.X / 2f, lc.Center.Y - eSz.Y / 2f), ColorSubText, 0f, Vector2.Zero, ets, SpriteEffects.None, 1f);
            }
        }

        private static void DrawBorder(SpriteBatch b, Rectangle r, Color c, int thickness)
        {
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, r.Width, thickness), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X, r.Y, thickness, r.Height), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
        }

        private void DrawListHeader(SpriteBatch b, Rectangle lc)
        {
            SpriteFont font = Game1.dialogueFont;
            int headerH = Scale(56);

            // Dark Header background bar matching phone theme
            b.Draw(Game1.staminaRect, new Rectangle(lc.X, lc.Y, lc.Width, headerH), new Color(20, 26, 40) * 0.96f);
            b.Draw(Game1.staminaRect, new Rectangle(lc.X, lc.Y + headerH - Scale(1), lc.Width, Scale(1)), ColorBorder);

            // Title on the left
            string title = "Live Camera";
            float ts = 0.54f * phoneUiScale;
            Vector2 tsz = font.MeasureString(title) * ts;
            b.DrawString(font, title, new Vector2(lc.X + Scale(14), lc.Y + (headerH - tsz.Y) / 2f), ColorAccent, 0f, Vector2.Zero, ts, SpriteEffects.None, 1f);

            // Page navigation: < Page/Total > buttons next to title
            bool canPrev = currentPage > 0;
            bool canNext = currentPage < TotalPages - 1;

            Color prevCol = canPrev ? (prevPageBtnHover ? new Color(70, 190, 255) : new Color(35, 100, 160)) : Color.Gray * 0.4f;
            CardDrawing.DrawCard(api, b, prevPageBtnRect, prevCol, scale: 0.50f * phoneUiScale);
            float arrowScale = 0.44f * phoneUiScale;
            Vector2 leftArrowSz = font.MeasureString("<") * arrowScale;
            b.DrawString(font, "<", new Vector2(prevPageBtnRect.Center.X - leftArrowSz.X / 2f, prevPageBtnRect.Center.Y - leftArrowSz.Y / 2f), canPrev ? Color.White : Color.Gray * 0.5f, 0f, Vector2.Zero, arrowScale, SpriteEffects.None, 1f);

            string pageStr = $"{currentPage + 1}/{TotalPages}";
            float pts = 0.44f * phoneUiScale;
            Vector2 psz = font.MeasureString(pageStr) * pts;
            int pageTextX = prevPageBtnRect.Right + Scale(8);
            b.DrawString(font, pageStr, new Vector2(pageTextX, lc.Y + (headerH - psz.Y) / 2f), ColorText, 0f, Vector2.Zero, pts, SpriteEffects.None, 1f);

            Color nextCol = canNext ? (nextPageBtnHover ? new Color(70, 190, 255) : new Color(35, 100, 160)) : Color.Gray * 0.4f;
            CardDrawing.DrawCard(api, b, nextPageBtnRect, nextCol, scale: 0.50f * phoneUiScale);
            Vector2 rightArrowSz = font.MeasureString(">") * arrowScale;
            b.DrawString(font, ">", new Vector2(nextPageBtnRect.Center.X - rightArrowSz.X / 2f, nextPageBtnRect.Center.Y - rightArrowSz.Y / 2f), canNext ? Color.White : Color.Gray * 0.5f, 0f, Vector2.Zero, arrowScale, SpriteEffects.None, 1f);

            // "+ Add Camera" Button on the right side of the header banner!
            Color addCol = addBtnHover ? new Color(210, 250, 230) : Color.White;
            CardDrawing.DrawCard(api, b, addBtnRect, addCol, scale: 0.55f * phoneUiScale);

            string addStr = "+ Add Camera";
            float ats = 0.44f * phoneUiScale;
            Vector2 aSz = font.MeasureString(addStr) * ats;
            b.DrawString(font, addStr, new Vector2(addBtnRect.Center.X - aSz.X / 2f, addBtnRect.Center.Y - aSz.Y / 2f), new Color(40, 120, 80), 0f, Vector2.Zero, ats, SpriteEffects.None, 1f);
        }

        private double LiveFeedRefreshSeconds => ModEntry.Config.CaptureRateSeconds;

        private bool IsPlayerInActiveCameraLocation =>
            activeCameraEntry != null &&
            Game1.player?.currentLocation != null &&
            string.Equals(Game1.player.currentLocation.Name, activeCameraEntry.LocationName, StringComparison.OrdinalIgnoreCase);

        private void DrawLiveView(SpriteBatch b, Rectangle lc)
        {
            SpriteFont font = Game1.dialogueFont;

            if (IsPlayerInActiveCameraLocation)
            {
                b.Draw(Game1.staminaRect, lc, new Color(16, 22, 34));

                string msg = "You are here";
                float scale = 0.65f * phoneUiScale;
                Vector2 sz = font.MeasureString(msg) * scale;

                int cardW = (int)sz.X + Scale(48);
                int cardH = (int)sz.Y + Scale(32);
                Rectangle msgBox = new Rectangle(lc.Center.X - cardW / 2, lc.Center.Y - cardH / 2, cardW, cardH);

                CardDrawing.DrawCard(api, b, msgBox, new Color(240, 245, 255), scale: 0.75f * phoneUiScale);
                b.DrawString(font, msg, new Vector2(msgBox.Center.X - sz.X / 2f, msgBox.Center.Y - sz.Y / 2f), Color.DarkSlateGray, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);

                DrawCctvOverlay(b, lc);
                return;
            }

            if (liveFeedHasFrame && liveFeedTarget != null && !liveFeedTarget.IsDisposed)
            {
                b.Draw(Game1.staminaRect, lc, Color.Black);
                b.Draw(liveFeedTarget, lc, Color.White);
                DrawCctvOverlay(b, lc);
            }
            else
            {
                b.Draw(Game1.staminaRect, lc, ColorBackground);
                string loadStr = "Loading feed..."; float ls = 0.40f * phoneUiScale;
                Vector2 lSz = font.MeasureString(loadStr) * ls;
                b.DrawString(font, loadStr, new Vector2(lc.Center.X - lSz.X / 2f, lc.Center.Y - lSz.Y / 2f), ColorSubText, 0f, Vector2.Zero, ls, SpriteEffects.None, 1f);
            }

            // Capture flash overlay
            if (captureFlashRemainingSeconds > 0.0 && CaptureFlashDurationSeconds > 0.0)
            {
                float progress = (float)(captureFlashRemainingSeconds / CaptureFlashDurationSeconds);
                float opacity = CaptureFlashMaxOpacity * progress * progress;
                if (opacity > 0f)
                    b.Draw(Game1.staminaRect, lc, Color.White * opacity);
            }
        }

        private void DrawCctvOverlay(SpriteBatch b, Rectangle lc)
        {
            if (activeCameraEntry == null) return;
            SpriteFont font = Game1.dialogueFont;

            // --- Top-Left: Radial LIVE progress circle (2x enlarged, electric blue) ---
            int circleR = Scale(32);
            int circleX = lc.X + circleR + Scale(12);
            int circleY = lc.Y + circleR + Scale(12);
            float refreshSecs = (float)Math.Max(0.1, LiveFeedRefreshSeconds);
            float progress = Math.Clamp((float)(1.0 - (liveFeedTimer / refreshSecs)), 0f, 1f);

            DrawRadialCircleBadge(b, circleX, circleY, circleR, progress, "LIVE", font, 0.48f * phoneUiScale);

            // --- Bottom-Left: 2 lines (target location display name & timestamp, 2x enlarged) ---
            GameLocation? loc = Game1.getLocationFromName(activeCameraEntry.LocationName);
            string locDisplayName = loc?.DisplayName ?? activeCameraEntry.LocationName;
            if (string.IsNullOrWhiteSpace(locDisplayName)) locDisplayName = activeCameraEntry.Name;

            string timeStr = Game1.getTimeOfDayString(Game1.timeOfDay);
            string dateStr = $"Yr {Game1.year}, {Utility.capitalizeFirstLetter(Game1.currentSeason ?? "Spring")} {Game1.dayOfMonth}  {timeStr}";

            float line1Scale = 0.64f * phoneUiScale;
            float line2Scale = 0.56f * phoneUiScale;
            int margin = Scale(12);
            Vector2 sz1 = font.MeasureString(locDisplayName) * line1Scale;
            Vector2 sz2 = font.MeasureString(dateStr) * line2Scale;
            float maxW = Math.Max(sz1.X, sz2.X);
            int bannerH = Scale(62);

            Rectangle bannerRect = new Rectangle(lc.X + margin - Scale(4), lc.Bottom - margin - bannerH, (int)maxW + Scale(16), bannerH);
            b.Draw(Game1.staminaRect, bannerRect, Color.Black * 0.55f);

            b.DrawString(font, locDisplayName, new Vector2(bannerRect.X + Scale(6), bannerRect.Y + Scale(4)), Color.White, 0f, Vector2.Zero, line1Scale, SpriteEffects.None, 1f);
            b.DrawString(font, dateStr, new Vector2(bannerRect.X + Scale(6), bannerRect.Y + Scale(32)), new Color(220, 230, 180), 0f, Vector2.Zero, line2Scale, SpriteEffects.None, 1f);
        }

        private static void DrawRadialCircleBadge(SpriteBatch b, int cx, int cy, int radius, float progress, string label, SpriteFont font, float fontScale)
        {
            // Dark background circle
            DrawFilledCircle(b, cx, cy, radius, Color.Black * 0.60f);

            // Radial arc steps
            int steps = 64;
            Color bgArcCol = new Color(30, 90, 160) * 0.45f;
            Color activeArcCol = new Color(60, 175, 255);

            for (int i = 0; i < steps; i++)
            {
                float stepFrac = i / (float)steps;
                float angle = -MathHelper.PiOver2 + stepFrac * MathHelper.TwoPi;
                int rx = cx + (int)MathF.Round((radius - 1) * MathF.Cos(angle));
                int ry = cy + (int)MathF.Round((radius - 1) * MathF.Sin(angle));
                bool isActive = stepFrac <= progress;
                Color col = isActive ? activeArcCol : bgArcCol;
                b.Draw(Game1.staminaRect, new Rectangle(rx - 1, ry - 1, 3, 3), col);
            }

            // Inner text "LIVE" (blue)
            Vector2 sz = font.MeasureString(label) * fontScale;
            Vector2 pos = new Vector2(cx - sz.X / 2f, cy - sz.Y / 2f);
            b.DrawString(font, label, pos + new Vector2(1, 1), Color.Black * 0.7f, 0f, Vector2.Zero, fontScale, SpriteEffects.None, 1f);
            b.DrawString(font, label, pos, activeArcCol, 0f, Vector2.Zero, fontScale, SpriteEffects.None, 1f);
        }

        private static void DrawFilledCircle(SpriteBatch b, int cx, int cy, int r, Color color)
        {
            if (r <= 0) return;
            int rSq = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int dySq = dy * dy;
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dySq <= rSq)
                        b.Draw(Game1.staminaRect, new Rectangle(cx + dx, cy + dy, 1, 1), color);
                }
            }
        }

        public void DrawScreenContent(SpriteBatch b, Rectangle dest)
        {
            if (currentView == View.Live)
            {
                if (IsPlayerInActiveCameraLocation)
                {
                    b.Draw(Game1.staminaRect, dest, new Color(16, 22, 34));
                    SpriteFont font = Game1.dialogueFont;
                    string msg = "You are here";
                    float scale = 0.48f;
                    Vector2 sz = font.MeasureString(msg) * scale;
                    int cardW = (int)sz.X + 24;
                    int cardH = (int)sz.Y + 16;
                    Rectangle msgBox = new Rectangle(dest.Center.X - cardW / 2, dest.Center.Y - cardH / 2, cardW, cardH);
                    CardDrawing.DrawCard(api, b, msgBox, new Color(240, 245, 255), scale: 0.55f);
                    b.DrawString(font, msg, new Vector2(msgBox.Center.X - sz.X / 2f, msgBox.Center.Y - sz.Y / 2f), Color.DarkSlateGray, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
                    DrawCctvOverlay(b, dest);
                    return;
                }
                if (liveFeedHasFrame && liveFeedTarget != null && !liveFeedTarget.IsDisposed)
                {
                    b.Draw(Game1.staminaRect, dest, Color.Black);
                    b.Draw(liveFeedTarget, dest, Color.White);
                    DrawCctvOverlay(b, dest);
                    return;
                }
            }

            Texture2D? bg = api.GetPhoneBackgroundTexture();
            if (bg != null && !bg.IsDisposed) b.Draw(bg, dest, Color.White);
            else b.Draw(Game1.staminaRect, dest, ColorBackground);
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
            if (api.HandlePhoneSizeButtonsClick(x, y, xPositionOnScreen, yPositionOnScreen)) return;
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);
            Rectangle lFrame = LandscapeFrameRect; Rectangle lContent = LandscapeContentRect;
            if (!isDragging)
            {
                if (lFrame.Contains(x, y) && !lContent.Contains(x, y)) { isDragging = true; dragOffsetX = x - xPositionOnScreen; dragOffsetY = y - yPositionOnScreen; }
            }
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);
            if (!isDragging && LandscapeContentRect.Contains(x, y)) HandleTap(x, y);
            isDragging = false;
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (currentView == View.List)
            {
                if (direction > 0 && currentPage > 0)
                {
                    currentPage--;
                    RebuildListLayout();
                    RefreshPageCaptures();
                    Game1.playSound("smallSelect");
                }
                else if (direction < 0 && currentPage < TotalPages - 1)
                {
                    currentPage++;
                    RebuildListLayout();
                    RefreshPageCaptures();
                    Game1.playSound("smallSelect");
                }
            }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            hoveredCard = -1; addBtnHover = false; hoveredDeleteBtn = -1;
            prevPageBtnHover = currentView == View.List && prevPageBtnRect.Contains(x, y);
            nextPageBtnHover = currentView == View.List && nextPageBtnRect.Contains(x, y);

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

            if (currentPage > 0 && prevPageBtnRect.Contains(x, y))
            {
                currentPage--;
                RebuildListLayout();
                RefreshPageCaptures();
                Game1.playSound("smallSelect");
                return;
            }
            if (currentPage < TotalPages - 1 && nextPageBtnRect.Contains(x, y))
            {
                currentPage++;
                RebuildListLayout();
                RefreshPageCaptures();
                Game1.playSound("smallSelect");
                return;
            }

            for (int i = 0; i < cardRects.Count; i++)
            {
                var (_, deleteRect, entry) = cardRects[i];
                if (deleteRect.Contains(x, y))
                {
                    cameras.Remove(entry);
                    saveCallback();
                    if (currentPage >= TotalPages) currentPage = Math.Max(0, TotalPages - 1);
                    RebuildListLayout();
                    RefreshPageCaptures();
                    Game1.playSound("trashcan");
                    return;
                }
            }
            for (int i = 0; i < cardRects.Count; i++)
            {
                var (cardRect, _, entry) = cardRects[i];
                if (cardRect.Contains(x, y))
                {
                    OpenLiveView(entry);
                    Game1.playSound("smallSelect");
                    return;
                }
            }
            if (addBtnRect.Contains(x, y)) AddCameraAtPlayerPosition();
        }

        private void AddCameraAtPlayerPosition()
        {
            if (!StardewModdingAPI.Context.IsWorldReady || Game1.player == null || Game1.currentLocation == null)
            {
                Game1.addHUDMessage(new HUDMessage("Cannot add camera right now.", HUDMessage.error_type));
                return;
            }
            var entry = new CameraEntry
            {
                LocationName = Game1.currentLocation.Name,
                TileX = Game1.player.TilePoint.X,
                TileY = Game1.player.TilePoint.Y,
                Name = $"{Game1.currentLocation.DisplayName} ({Game1.player.TilePoint.X}, {Game1.player.TilePoint.Y})"
            };
            cameras.Add(entry);
            saveCallback();
            currentPage = TotalPages - 1;
            RebuildListLayout();
            RefreshPageCaptures();
            Game1.playSound("cameraNoise");
            Game1.addHUDMessage(new HUDMessage($"Camera added: {entry.Name}", HUDMessage.newQuest_type));
        }

        private void OpenLiveView(CameraEntry entry) { activeCameraEntry = entry; zoomLevel = entry.ZoomLevel > 0f ? entry.ZoomLevel : 1.0f; liveFeedTimer = 0; currentView = View.Live; ReallocLiveFeedTarget(); ModEntry.Instance?.RegisterLiveViewPassiveHud(); api.SetHudPinned(true); Game1.activeClickableMenu = null; }
        private void GoToList() { currentView = View.List; activeCameraEntry = null; RebuildListLayout(); RefreshPageCaptures(); api.SetHudPinned(false); ModEntry.Instance?.UnregisterLiveViewPassiveHud(); }

        private void LandscapeToPortraitClick(int cx, int cy, out int px, out int py)
        {
            int pox = xPositionOnScreen - (phoneFrameWidth - phoneFrameHeight) / 2;
            int poy = yPositionOnScreen - (phoneFrameHeight - phoneFrameWidth) / 2;
            px = pox + (yPositionOnScreen + phoneFrameWidth - cy);
            py = poy + (cx - xPositionOnScreen);
        }

        protected override void cleanupBeforeExit()
        {
            if (!api.IsHudPinned())
            {
                liveFeedTarget?.Dispose();
                liveFeedTarget = null;
                ModEntry.Instance?.UnregisterLiveViewPassiveHud();
            }
            for (int i = 0; i < gridFeedTargets.Length; i++)
            {
                gridFeedTargets[i]?.Dispose();
                gridFeedTargets[i] = null;
                gridFeedHasFrame[i] = false;
            }
            base.cleanupBeforeExit();
        }
    }
}