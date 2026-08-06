// =============================================================================
// LiveCameraControllerHud.cs  --  HUD Overlay Camera Controller for Live Camera
// =============================================================================
// Renders a compact, square controller panel matching the phone HUD style:
//   - Faded black background with crisp white elements
//   - Top-left  [-] : zoom out (widen capture area)
//   - Top-right [+] : zoom in  (narrow capture area)
//   - Centre       : 2-circle joystick to pan tile position
//   - Bottom-left  [Flash] : toggle camera flash light
//   - Bottom-right [Shutter] : capture photo
//   - Right side   : Vertical capture rate slider (inside overlay bounds)
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SmartphoneLiveCamera
{
    /// <summary>
    /// Interactive HUD overlay controller panel for the Live Camera app.
    /// Styled to match the Phone HUD size slider (faded black background, white elements).
    /// </summary>
    internal sealed class LiveCameraControllerHud
    {
        // -----------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------
        private const int PanelHeight      = 132;
        private const int CornerBtnSize    = 28;
        private const int CornerBtnPadding = 5;
        private const int OuterRadius      = 22;
        private const int InnerRadius      = 9;
        private const int JoystickMaxDrift = 14;

        // How fast (tile units per pixel per held-frame tick) the camera pans
        private const float PanTilesPerPixelPerTick = 0.0018f;

        // -----------------------------------------------------------------------
        // Faded Black + White Theme Colors (Matching Phone HUD Size Slider)
        // -----------------------------------------------------------------------
        private static readonly Color ColPanelBg     = Color.Black * 0.65f;
        private static readonly Color ColPanelBorder = Color.White * 0.30f;
        private static readonly Color ColBtnNormal   = Color.Black * 0.45f;
        private static readonly Color ColBtnHover    = Color.White * 0.25f;
        private static readonly Color ColBtnBorder   = Color.White * 0.35f;
        private static readonly Color ColIconNormal  = Color.White * 0.95f;
        private static readonly Color ColFlashActive = Color.White * 0.88f;
        private static readonly Color ColJoystickRim = Color.White * 0.35f;
        private static readonly Color ColJoystickBg  = Color.Black * 0.50f;
        private static readonly Color ColJoystickKnob= Color.White * 0.90f;
        private static readonly Color ColText        = Color.White * 0.95f;

        // Mouse Cursor Icon Sources (from Stardew Valley HelperCamera)
        private static readonly Rectangle CameraZoomMinusIconSource = new Rectangle(177, 345, 7, 8);
        private static readonly Rectangle CameraZoomPlusIconSource  = new Rectangle(184, 345, 7, 8);
        private static readonly Rectangle CameraFlashIconSource     = new Rectangle(193, 373, 9, 9);

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

            // Left square panel (96x128) inside dest (132x132)
            destSquare = new Rectangle(dest.X + 2, dest.Y + 2, 96, dest.Height - 4);

            // Draw square controller background panel & border
            b.Draw(Game1.staminaRect, destSquare, ColPanelBg);
            DrawBorder(b, destSquare, ColPanelBorder);

            // Right vertical rate slider panel (28x128) inside dest (132x132)
            boundsRateSlider = new Rectangle(dest.X + 101, dest.Y + 2, 29, dest.Height - 4);
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
            int btnSz = CornerBtnSize; // 28
            int pad   = CornerBtnPadding; // 5

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
            DrawIconButton(b, boundsZoomOut, CameraZoomMinusIconSource, mx, my);
            DrawIconButton(b, boundsZoomIn,  CameraZoomPlusIconSource,  mx, my);
            DrawFlashButton(b, boundsFlash, screen.FlashEnabled, mx, my);
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
            b.Draw(Game1.staminaRect, boundsRateSlider, ColPanelBg);
            DrawBorder(b, boundsRateSlider, ColPanelBorder);

            float rate = ModEntry.Config.CaptureRateSeconds;
            float frac = Math.Clamp((rate - 0.5f) / 19.5f, 0f, 1f);

            // Track background
            b.Draw(Game1.staminaRect, rateTrackRect, Color.Black * 0.50f);
            DrawBorder(b, rateTrackRect, ColPanelBorder * 0.8f);

            // Active track fill
            int knobY = rateTrackRect.Y + (int)MathF.Round(frac * rateTrackRect.Height);
            Rectangle activeFill = new Rectangle(rateTrackRect.X, rateTrackRect.Y, rateTrackRect.Width, Math.Max(1, knobY - rateTrackRect.Y));
            b.Draw(Game1.staminaRect, activeFill, Color.White * 0.75f);

            // Knob
            int knobW = 20;
            int knobH = 10;
            Rectangle knobRect = new Rectangle(boundsRateSlider.Center.X - knobW / 2, knobY - knobH / 2, knobW, knobH);
            bool isHov = boundsRateSlider.Contains(mx, my) || rateSliderHeld;
            Color knobCol = isHov ? Color.White : Color.White * 0.88f;
            b.Draw(Game1.staminaRect, knobRect, knobCol);
            DrawBorder(b, knobRect, isHov ? Color.White : ColPanelBorder);

            // Tooltip text showing current rate (rendered inside rate slider panel)
            if (isHov)
            {
                SpriteFont font = Game1.smallFont;
                string label = $"{rate:0.0}s";
                const float scale = 0.60f;
                Vector2 sz = font.MeasureString(label) * scale;
                Vector2 pos = new Vector2(boundsRateSlider.Center.X - sz.X / 2f, knobRect.Center.Y - sz.Y / 2f);

                // If knob is near center, offset text slightly above or below knob
                if (knobY < rateTrackRect.Y + rateTrackRect.Height / 2)
                    pos.Y = knobRect.Bottom + 2;
                else
                    pos.Y = knobRect.Y - sz.Y - 2;

                b.Draw(Game1.staminaRect, new Rectangle((int)pos.X - 2, (int)pos.Y - 1, (int)sz.X + 4, (int)sz.Y + 2), ColPanelBg);
                DrawBorder(b, new Rectangle((int)pos.X - 2, (int)pos.Y - 1, (int)sz.X + 4, (int)sz.Y + 2), ColPanelBorder);
                b.DrawString(font, label, pos, ColText, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
            }
        }

        // -----------------------------------------------------------------------
        // Joystick drawing
        // -----------------------------------------------------------------------
        private void DrawJoystick(SpriteBatch b)
        {
            // Outer ring
            DrawFilledCircle(b, joystickCenterX, joystickCenterY, OuterRadius,     ColJoystickRim);
            DrawFilledCircle(b, joystickCenterX, joystickCenterY, OuterRadius - 2, ColJoystickBg);

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

            DrawFilledCircle(b, kx, ky, InnerRadius,     ColJoystickKnob * 0.9f);
            DrawFilledCircle(b, kx, ky, InnerRadius - 3, Color.White);
        }

        // -----------------------------------------------------------------------
        // Button drawing (Enlarged icons matching Camera App style)
        // -----------------------------------------------------------------------
        private static void DrawIconButton(SpriteBatch b, Rectangle bounds, Rectangle iconSource, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            b.Draw(Game1.staminaRect, bounds, isHov ? ColBtnHover : ColBtnNormal);
            DrawBorder(b, bounds, ColBtnBorder);

            if (Game1.mouseCursors != null && !Game1.mouseCursors.IsDisposed)
            {
                int iconSz = 20; // Enlarged icon size (from 14 to 20)
                Rectangle iconBounds = new Rectangle(bounds.Center.X - iconSz / 2, bounds.Center.Y - iconSz / 2, iconSz, iconSz);
                Rectangle shadowBounds = new Rectangle(iconBounds.X + 1, iconBounds.Y + 1, iconBounds.Width, iconBounds.Height);
                b.Draw(Game1.mouseCursors, shadowBounds, iconSource, Color.Black * 0.45f);
                b.Draw(Game1.mouseCursors, iconBounds, iconSource, ColIconNormal);
            }
        }

        private static void DrawFlashButton(SpriteBatch b, Rectangle bounds, bool active, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            Color bg   = active ? ColFlashActive : (isHov ? ColBtnHover : ColBtnNormal);
            b.Draw(Game1.staminaRect, bounds, bg);
            DrawBorder(b, bounds, ColBtnBorder);

            if (Game1.mouseCursors != null && !Game1.mouseCursors.IsDisposed)
            {
                int iconSz = 20; // Enlarged icon size (from 16 to 20)
                Rectangle iconBounds = new Rectangle(bounds.Center.X - iconSz / 2, bounds.Center.Y - iconSz / 2, iconSz, iconSz);
                Color iconCol = active ? Color.Black * 0.85f : ColIconNormal;

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
            Color bg   = isHov ? ColBtnHover : ColBtnNormal;
            b.Draw(Game1.staminaRect, bounds, bg);
            DrawBorder(b, bounds, ColBtnBorder);

            // Concentric Camera Shutter Rings (Enlarged)
            int outerR = Math.Min(bounds.Width, bounds.Height) / 2 - 4;
            int innerR = outerR - 3;

            DrawFilledCircle(b, bounds.Center.X, bounds.Center.Y, outerR,     Color.White * 0.45f);
            DrawFilledCircle(b, bounds.Center.X, bounds.Center.Y, outerR - 2, Color.Black * 0.60f);
            DrawFilledCircle(b, bounds.Center.X, bounds.Center.Y, innerR,     isHov ? Color.White : Color.White * 0.88f);
        }

        private static void DrawBorder(SpriteBatch b, Rectangle r, Color c)
        {
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Y,          r.Width, 1),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Bottom - 1, r.Width, 1),        c);
            b.Draw(Game1.staminaRect, new Rectangle(r.X,         r.Y,          1,       r.Height), c);
            b.Draw(Game1.staminaRect, new Rectangle(r.Right - 1, r.Y,          1,       r.Height), c);
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
            }
        }

        private void UpdateRateFromMouseY(int my)
        {
            if (rateTrackRect.Height <= 0) return;
            float frac = Math.Clamp((float)(my - rateTrackRect.Y) / rateTrackRect.Height, 0f, 1f);
            float newRate = 0.5f + frac * 19.5f;
            newRate = MathF.Round(newRate * 2f) / 2f;
            if (Math.Abs(ModEntry.Config.CaptureRateSeconds - newRate) > 0.01f)
            {
                ModEntry.Config.CaptureRateSeconds = newRate;
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
    }
}
