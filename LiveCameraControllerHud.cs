// =============================================================================
// LiveCameraControllerHud.cs  --  HUD Overlay Camera Controller for Live Camera
// =============================================================================
// Futuristic CCTV-styled HUD controller overlay:
//   - Dark glassmorphism chassis with electric blue glow
//   - 1:1 Square main controller panel (96x96) with height-matched vertical slider
//   - Enlarged 32px circular glass pill buttons with cyan accent highlights
//   - Top-left  [-] : zoom out (widen capture area)
//   - Top-right [+] : zoom in  (narrow capture area)
//   - Centre       : Directional crosshair joystick to pan tile position
//   - Bottom-left  [Flash] : toggle camera flash light
//   - Bottom-right [Shutter] : capture photo
//   - Right side   : Vertical capture rate slider with glowing electric blue track
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SmartphoneLiveCamera
{
    /// <summary>
    /// Interactive HUD overlay controller panel for the Live Camera app.
    /// Futuristic dark glassmorphism design with electric blue CCTV accents.
    /// Main controller panel is structured as a 1:1 square with enlarged 32px buttons.
    /// </summary>
    internal sealed class LiveCameraControllerHud
    {
        // -----------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------
        private const int PanelHeight      = 100; // Panel height for 1:1 square main area
        private const int CornerBtnSize    = 32; // Enlarged corner buttons (from 25 to 32)
        private const int CornerBtnPadding = 3;
        private const int OuterRadius      = 17;
        private const int InnerRadius      = 8;
        private const int JoystickMaxDrift = 11;

        // How fast (tile units per pixel per held-frame tick) the camera pans
        private const float PanTilesPerPixelPerTick = 0.0018f;

        // -----------------------------------------------------------------------
        // Electric CCTV Glassmorphism Palette
        // -----------------------------------------------------------------------
        private static readonly Color ColChassisBg     = new Color(10, 14, 24) * 0.88f;
        private static readonly Color ColChassisBorder = new Color(60, 175, 255) * 0.45f;
        private static readonly Color ColBtnBgNormal   = new Color(20, 28, 44) * 0.90f;
        private static readonly Color ColBtnBgHover    = new Color(40, 140, 220) * 0.65f;
        private static readonly Color ColBtnBorder     = new Color(60, 175, 255) * 0.45f;
        private static readonly Color ColBtnBorderHov  = new Color(100, 210, 255);
        private static readonly Color ColIconNormal    = new Color(240, 248, 255);
        private static readonly Color ColFlashActive   = new Color(255, 215, 0); // Golden yellow
        private static readonly Color ColJoystickRing  = new Color(60, 175, 255) * 0.65f;
        private static readonly Color ColJoystickBg    = new Color(12, 18, 30) * 0.92f;
        private static readonly Color ColJoystickKnob  = new Color(220, 240, 255);
        private static readonly Color ColJoystickDot   = new Color(60, 175, 255);
        private static readonly Color ColAccentBlue    = new Color(60, 175, 255);
        private static readonly Color ColText        = Color.White * 0.95f;

        // Mouse Cursor Icon Sources (from Stardew Valley HelperCamera)
        private static readonly Rectangle CameraFlashIconSource = new Rectangle(193, 373, 9, 9);

        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------
        private readonly Func<LiveCameraScreen?> getScreen;

        private bool  joystickHeld    = false;
        private float joystickDx      = 0f;
        private float joystickDy      = 0f;
        private int   joystickCenterX = 0;
        private int   joystickCenterY = 0;

        private bool  rateSliderHeld   = false;
        private Rectangle destSquare       = Rectangle.Empty;
        private Rectangle boundsZoomOut    = Rectangle.Empty;
        private Rectangle boundsZoomIn     = Rectangle.Empty;
        private Rectangle boundsFlash      = Rectangle.Empty;
        private Rectangle boundsCapture    = Rectangle.Empty;
        private Rectangle boundsJoystick   = Rectangle.Empty;
        private Rectangle boundsRateSlider = Rectangle.Empty;
        private Rectangle rateTrackRect    = Rectangle.Empty;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------
        internal LiveCameraControllerHud(Func<LiveCameraScreen?> getScreen)
        {
            this.getScreen = getScreen;
        }

        // -----------------------------------------------------------------------
        // Height query
        // -----------------------------------------------------------------------
        internal int GetOverlayHeight() => PanelHeight;

        // -----------------------------------------------------------------------
        // Drawing
        // -----------------------------------------------------------------------
        internal void Draw(SpriteBatch b, Rectangle dest)
        {
            LiveCameraScreen? screen = getScreen();
            int mx = Game1.getMouseX(true);
            int my = Game1.getMouseY(true);

            // 1:1 Square main controller panel (96x96)
            destSquare = new Rectangle(dest.X + 2, dest.Y + 2, 96, 96);

            // Draw square controller background panel & border
            b.Draw(Game1.staminaRect, destSquare, ColChassisBg);
            DrawBorder(b, destSquare, ColChassisBorder);

            // Right vertical rate slider panel (29x96) height-matched to main square
            boundsRateSlider = new Rectangle(dest.X + 101, dest.Y + 2, 29, 96);
            rateTrackRect    = new Rectangle(boundsRateSlider.Center.X - 3, boundsRateSlider.Y + 10, 6, boundsRateSlider.Height - 20);

            if (screen == null || !screen.IsLiveViewActive)
            {
                // Dim message
                SpriteFont font = Game1.dialogueFont;
                string msg = "Live view\nnot active";
                float ts   = 0.28f;
                Vector2 sz = font.MeasureString(msg) * ts;
                b.DrawString(font, msg,
                    new Vector2(destSquare.Center.X - sz.X / 2f, destSquare.Center.Y - sz.Y / 2f),
                    ColText * 0.45f, 0f, Vector2.Zero, ts, SpriteEffects.None, 1f);
                return;
            }

            // --- Layout buttons & joystick ---
            int btnSz = CornerBtnSize; // 32
            int pad   = CornerBtnPadding; // 3

            boundsZoomOut = new Rectangle(destSquare.X + pad,              destSquare.Y + pad,              btnSz, btnSz);
            boundsZoomIn  = new Rectangle(destSquare.Right - pad - btnSz,  destSquare.Y + pad,              btnSz, btnSz);
            boundsFlash   = new Rectangle(destSquare.X + pad,              destSquare.Bottom - pad - btnSz, btnSz, btnSz);
            boundsCapture = new Rectangle(destSquare.Right - pad - btnSz,  destSquare.Bottom - pad - btnSz, btnSz, btnSz);

            joystickCenterX = destSquare.Center.X;
            joystickCenterY = destSquare.Center.Y;
            boundsJoystick  = new Rectangle(
                joystickCenterX - OuterRadius,
                joystickCenterY - OuterRadius,
                OuterRadius * 2,
                OuterRadius * 2);

            // Draw elements
            DrawZoomOutButton(b, boundsZoomOut, mx, my);
            DrawZoomInButton(b,  boundsZoomIn,  mx, my);
            DrawFlashButton(b,   boundsFlash, screen.FlashEnabled, mx, my);
            DrawCaptureButton(b, boundsCapture, mx, my);
            DrawJoystick(b);
            DrawRateSlider(b, mx, my);
        }

        // -----------------------------------------------------------------------
        // Vertical Rate Slider drawing (Within overlay bounds)
        // -----------------------------------------------------------------------
        private void DrawRateSlider(SpriteBatch b, int mx, int my)
        {
            // Panel background & border
            b.Draw(Game1.staminaRect, boundsRateSlider, ColChassisBg);
            DrawBorder(b, boundsRateSlider, ColChassisBorder);

            float rate = ModEntry.Config.CaptureRateSeconds;
            float frac = Math.Clamp((rate - 0.25f) / 19.75f, 0f, 1f);

            // Track background
            b.Draw(Game1.staminaRect, rateTrackRect, Color.Black * 0.60f);
            DrawBorder(b, rateTrackRect, ColChassisBorder * 0.7f);

            // Active track fill (Electric Blue)
            int knobY = rateTrackRect.Y + (int)MathF.Round(frac * rateTrackRect.Height);
            Rectangle activeFill = new Rectangle(rateTrackRect.X, rateTrackRect.Y, rateTrackRect.Width, Math.Max(1, knobY - rateTrackRect.Y));
            b.Draw(Game1.staminaRect, activeFill, ColAccentBlue * 0.85f);

            // Knob
            int knobW = 18;
            int knobH = 9;
            Rectangle knobRect = new Rectangle(boundsRateSlider.Center.X - knobW / 2, knobY - knobH / 2, knobW, knobH);
            bool isHov = boundsRateSlider.Contains(mx, my) || rateSliderHeld;
            Color knobCol = isHov ? Color.White : ColJoystickKnob;
            b.Draw(Game1.staminaRect, knobRect, knobCol);
            DrawBorder(b, knobRect, isHov ? ColAccentBlue : ColBtnBorder);

            // Knob center accent line
            b.Draw(Game1.staminaRect, new Rectangle(knobRect.X + 2, knobRect.Center.Y, knobRect.Width - 4, 1), ColAccentBlue);

            // Tooltip text showing current rate (rendered inside rate slider panel)
            if (isHov)
            {
                SpriteFont font = Game1.smallFont;
                string label = $"{rate:0.##}s";
                const float scale = 0.55f;
                Vector2 sz = font.MeasureString(label) * scale;
                Vector2 pos = new Vector2(boundsRateSlider.Center.X - sz.X / 2f, knobRect.Center.Y - sz.Y / 2f);

                // If knob is near center, offset text slightly above or below knob
                if (knobY < rateTrackRect.Y + rateTrackRect.Height / 2)
                    pos.Y = knobRect.Bottom + 2;
                else
                    pos.Y = knobRect.Y - sz.Y - 2;

                Rectangle lblBox = new Rectangle((int)pos.X - 3, (int)pos.Y - 1, (int)sz.X + 6, (int)sz.Y + 2);
                b.Draw(Game1.staminaRect, lblBox, ColChassisBg);
                DrawBorder(b, lblBox, ColAccentBlue);
                b.DrawString(font, label, pos, ColAccentBlue, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
            }
        }

        // -----------------------------------------------------------------------
        // Joystick drawing (Directional Crosshair & Metallic Stick)
        // -----------------------------------------------------------------------
        private void DrawJoystick(SpriteBatch b)
        {
            // Outer ring base
            DrawFilledCircle(b, joystickCenterX, joystickCenterY, OuterRadius,     ColJoystickRing);
            DrawFilledCircle(b, joystickCenterX, joystickCenterY, OuterRadius - 2, ColJoystickBg);

            // Directional crosshair ticks (N, S, E, W)
            Color tickCol = ColAccentBlue * 0.65f;
            b.Draw(Game1.staminaRect, new Rectangle(joystickCenterX - 1, joystickCenterY - OuterRadius + 1, 2, 3), tickCol);
            b.Draw(Game1.staminaRect, new Rectangle(joystickCenterX - 1, joystickCenterY + OuterRadius - 4, 2, 3), tickCol);
            b.Draw(Game1.staminaRect, new Rectangle(joystickCenterX - OuterRadius + 1, joystickCenterY - 1, 3, 2), tickCol);
            b.Draw(Game1.staminaRect, new Rectangle(joystickCenterX + OuterRadius - 4, joystickCenterY - 1, 3, 2), tickCol);

            // Clamp knob displacement
            float len  = MathF.Sqrt(joystickDx * joystickDx + joystickDy * joystickDy);
            float cx   = joystickDx;
            float cy   = joystickDy;
            if (len > JoystickMaxDrift && len > 0.001f)
            {
                cx = joystickDx / len * JoystickMaxDrift;
                cy = joystickDy / len * JoystickMaxDrift;
            }

            int kx = joystickCenterX + (int)MathF.Round(cx);
            int ky = joystickCenterY + (int)MathF.Round(cy);

            // Metallic knob with center electric blue dot
            DrawFilledCircle(b, kx, ky, InnerRadius,     ColJoystickKnob);
            DrawFilledCircle(b, kx, ky, InnerRadius - 3, ColJoystickDot);
            DrawFilledCircle(b, kx, ky, InnerRadius - 5, Color.White);
        }

        // -----------------------------------------------------------------------
        // Button drawing (Enlarged 32px Circular glass capsules)
        // -----------------------------------------------------------------------
        private static void DrawZoomOutButton(SpriteBatch b, Rectangle bounds, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            int r = bounds.Width / 2;
            int cx = bounds.Center.X;
            int cy = bounds.Center.Y;

            DrawFilledCircle(b, cx, cy, r, isHov ? ColBtnBgHover : ColBtnBgNormal);
            DrawCircleRing(b,   cx, cy, r, isHov ? ColBtnBorderHov : ColBtnBorder);

            Color col = isHov ? Color.White : ColIconNormal;
            // Clean vector minus symbol
            b.Draw(Game1.staminaRect, new Rectangle(cx - 6, cy - 1, 12, 3), Color.Black * 0.40f);
            b.Draw(Game1.staminaRect, new Rectangle(cx - 6, cy - 1, 12, 2), col);
        }

        private static void DrawZoomInButton(SpriteBatch b, Rectangle bounds, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            int r = bounds.Width / 2;
            int cx = bounds.Center.X;
            int cy = bounds.Center.Y;

            DrawFilledCircle(b, cx, cy, r, isHov ? ColBtnBgHover : ColBtnBgNormal);
            DrawCircleRing(b,   cx, cy, r, isHov ? ColBtnBorderHov : ColBtnBorder);

            Color col = isHov ? Color.White : ColIconNormal;
            // Clean vector plus symbol
            b.Draw(Game1.staminaRect, new Rectangle(cx - 6, cy - 1, 12, 3), Color.Black * 0.40f);
            b.Draw(Game1.staminaRect, new Rectangle(cx - 1, cy - 6, 3, 12), Color.Black * 0.40f);

            b.Draw(Game1.staminaRect, new Rectangle(cx - 6, cy - 1, 12, 2), col);
            b.Draw(Game1.staminaRect, new Rectangle(cx - 1, cy - 6, 2, 12), col);
        }

        private static void DrawFlashButton(SpriteBatch b, Rectangle bounds, bool active, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            int r = bounds.Width / 2;
            int cx = bounds.Center.X;
            int cy = bounds.Center.Y;

            Color bg = active ? ColFlashActive * 0.85f : (isHov ? ColBtnBgHover : ColBtnBgNormal);
            Color border = active ? ColFlashActive : (isHov ? ColBtnBorderHov : ColBtnBorder);

            DrawFilledCircle(b, cx, cy, r, bg);
            DrawCircleRing(b,   cx, cy, r, border);

            if (Game1.mouseCursors != null && !Game1.mouseCursors.IsDisposed)
            {
                int iconSz = 22; // Enlarged icon size inside 32px button
                Rectangle iconBounds = new Rectangle(cx - iconSz / 2, cy - iconSz / 2, iconSz, iconSz);
                Color iconCol = active ? Color.Black * 0.90f : (isHov ? Color.White : ColIconNormal);

                if (!active)
                {
                    Rectangle shadowBounds = new Rectangle(iconBounds.X + 1, iconBounds.Y + 1, iconBounds.Width, iconBounds.Height);
                    b.Draw(Game1.mouseCursors, shadowBounds, CameraFlashIconSource, Color.Black * 0.45f);
                }
                b.Draw(Game1.mouseCursors, iconBounds, CameraFlashIconSource, iconCol);
            }
        }

        private static void DrawCaptureButton(SpriteBatch b, Rectangle bounds, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            int r = bounds.Width / 2;
            int cx = bounds.Center.X;
            int cy = bounds.Center.Y;

            DrawFilledCircle(b, cx, cy, r, isHov ? ColBtnBgHover : ColBtnBgNormal);
            DrawCircleRing(b,   cx, cy, r, isHov ? ColBtnBorderHov : ColBtnBorder);

            // Shutter Ring & Red Record Dot (Scaled for 32px button)
            int outerR = r - 5;
            int innerR = outerR - 4;

            DrawFilledCircle(b, cx, cy, outerR,     Color.White * 0.50f);
            DrawFilledCircle(b, cx, cy, outerR - 2, ColChassisBg);
            DrawFilledCircle(b, cx, cy, innerR,     isHov ? new Color(240, 60, 60) : new Color(210, 45, 45));
        }

        private static void DrawBorder(SpriteBatch b, Rectangle r, Color c)
        {
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Y,          r.Width, 1),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Bottom - 1, r.Width, 1),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Y,          1,       r.Height), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.Right - 1, r.Y,          1,       r.Height), c);
        }

        private static void DrawCircleRing(SpriteBatch b, int cx, int cy, int r, Color color)
        {
            if (r <= 0) return;
            int rInnerSq = (r - 1) * (r - 1);
            int rOuterSq = r * r;
            for (int dy = -r; dy <= r; dy++)
            {
                int dySq = dy * dy;
                for (int dx = -r; dx <= r; dx++)
                {
                    int distSq = dx * dx + dySq;
                    if (distSq <= rOuterSq && distSq >= rInnerSq)
                        b.Draw(Game1.staminaRect, new Rectangle(cx + dx, cy + dy, 1, 1), color);
                }
            }
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

        // -----------------------------------------------------------------------
        // Interaction callbacks (forwarded from RegisterPassiveHudOverlay)
        // -----------------------------------------------------------------------

        internal bool OnLeftClick(int x, int y)
        {
            LiveCameraScreen? screen = getScreen();
            if (screen == null || !screen.IsLiveViewActive) return false;

            if (boundsZoomOut.Contains(x, y))
            {
                screen.ZoomLevel -= 0.05f;
                Game1.playSound("drumkit6");
                return true;
            }
            if (boundsZoomIn.Contains(x, y))
            {
                screen.ZoomLevel += 0.05f;
                Game1.playSound("drumkit6");
                return true;
            }
            if (boundsFlash.Contains(x, y))
            {
                screen.FlashEnabled = !screen.FlashEnabled;
                Game1.playSound("smallSelect");
                return true;
            }
            if (boundsCapture.Contains(x, y))
            {
                screen.RequestPhotoCapture();
                return true;
            }
            if (boundsRateSlider.Contains(x, y))
            {
                rateSliderHeld = true;
                UpdateRateFromMouseY(y);
                screen.ForceImmediateFrameRefresh();
                return true;
            }
            if (boundsJoystick.Contains(x, y))
            {
                joystickHeld = true;
                joystickDx   = x - joystickCenterX;
                joystickDy   = y - joystickCenterY;
                return true;
            }

            return false;
        }

        internal void OnLeftClickHeld(int x, int y)
        {
            LiveCameraScreen? screen = getScreen();
            if (screen == null || !screen.IsLiveViewActive) return;

            if (rateSliderHeld)
            {
                UpdateRateFromMouseY(y);
                screen.ForceImmediateFrameRefresh();
                return;
            }

            if (!joystickHeld) return;

            joystickDx = x - joystickCenterX;
            joystickDy = y - joystickCenterY;

            CameraEntry? cam = screen.ActiveCamera;
            if (cam == null) return;

            float len = MathF.Sqrt(joystickDx * joystickDx + joystickDy * joystickDy);
            if (len < 2f) return; // dead zone

            float normDx = joystickDx / Math.Max(1f, len);
            float normDy = joystickDy / Math.Max(1f, len);
            float speed  = Math.Min(len, JoystickMaxDrift) * PanTilesPerPixelPerTick;

            cam.TileX = Math.Clamp(cam.TileX + normDx * speed, 0f, 9999f);
            cam.TileY = Math.Clamp(cam.TileY + normDy * speed, 0f, 9999f);
        }

        internal void OnReleaseLeftClick()
        {
            LiveCameraScreen? screen = getScreen();

            if (rateSliderHeld)
            {
                rateSliderHeld = false;
                ModEntry.SaveConfigCallback?.Invoke();
                screen?.ForceImmediateFrameRefresh();
            }
            if (joystickHeld)
            {
                joystickHeld = false;
                joystickDx   = 0f;
                joystickDy   = 0f;

                CameraEntry? cam = screen?.ActiveCamera;
                if (cam != null)
                {
                    GameLocation? loc = Game1.getLocationFromName(cam.LocationName);
                    string locName = loc?.DisplayName ?? cam.LocationName;
                    cam.Name = $"{locName} ({(int)MathF.Round(cam.TileX)}, {(int)MathF.Round(cam.TileY)})";
                }
            }
        }

        private void UpdateRateFromMouseY(int my)
        {
            if (rateTrackRect.Height <= 0) return;
            float frac = Math.Clamp((float)(my - rateTrackRect.Y) / rateTrackRect.Height, 0f, 1f);
            float newRate = 0.25f + frac * 19.75f;
            newRate = MathF.Round(newRate * 4f) / 4f;
            if (Math.Abs(ModEntry.Config.CaptureRateSeconds - newRate) > 0.01f)
            {
                ModEntry.Config.CaptureRateSeconds = newRate;
            }
        }
    }
}
