// =============================================================================
// LiveCameraControllerHud.cs  --  HUD Overlay Camera Controller for Live Camera
// =============================================================================
// Renders a compact controller panel containing:
//   - Top-left  [-] : zoom out (widen capture area)
//   - Top-right [+] : zoom in  (narrow capture area)
//   - Centre       : 2-circle joystick to pan the camera tile position
//   - Bottom-left  [F] : toggle flash
//   - Bottom-right [C] : capture photo
//
// The panel is drawn by the Smartphone framework's HUD overlay system,
// adaptively positioned below or above the phone HUD icon slider.
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SmartphoneLiveCamera
{
    /// <summary>
    /// Interactive HUD overlay controller panel for the Live Camera app.
    /// Receives a lazy reference to the active <see cref="LiveCameraScreen"/> so
    /// it stays in sync even if the screen is recreated by OpenApp().
    /// </summary>
    internal sealed class LiveCameraControllerHud
    {
        // -----------------------------------------------------------------------
        // Layout
        // -----------------------------------------------------------------------
        private const int PanelHeight      = 148;
        private const int CornerBtnSize    = 32;
        private const int CornerBtnPadding = 5;
        private const int OuterRadius      = 34;
        private const int InnerRadius      = 12;
        private const int JoystickMaxDrift = 22;

        // How fast (tile units per pixel per held-frame tick) the camera pans
        private const float PanTilesPerPixelPerTick = 0.0018f;

        // -----------------------------------------------------------------------
        // Colors
        // -----------------------------------------------------------------------
        private static readonly Color ColPanel       = new Color(12, 14, 22);
        private static readonly Color ColBorder      = new Color(60, 75, 110);
        private static readonly Color ColAccent      = new Color(80, 200, 140);
        private static readonly Color ColBtnNormal   = new Color(30, 38, 58);
        private static readonly Color ColBtnHover    = new Color(50, 65, 95);
        private static readonly Color ColFlashActive = new Color(255, 210, 80);
        private static readonly Color ColCapture     = new Color(200, 55, 55);
        private static readonly Color ColCaptureHov  = new Color(245, 90, 90);
        private static readonly Color ColJoystickRim = new Color(45, 58, 88);
        private static readonly Color ColJoystickBg  = new Color(18, 22, 35);
        private static readonly Color ColJoystickKnob = new Color(80, 200, 140);
        private static readonly Color ColText        = new Color(220, 230, 240);

        // -----------------------------------------------------------------------
        // State
        // -----------------------------------------------------------------------
        private readonly Func<LiveCameraScreen?> getScreen;

        private bool  joystickHeld    = false;
        private float joystickDx      = 0f;
        private float joystickDy      = 0f;
        private int   joystickCenterX = 0;
        private int   joystickCenterY = 0;

        private Rectangle boundsZoomOut  = Rectangle.Empty;
        private Rectangle boundsZoomIn   = Rectangle.Empty;
        private Rectangle boundsFlash    = Rectangle.Empty;
        private Rectangle boundsCapture  = Rectangle.Empty;
        private Rectangle boundsJoystick = Rectangle.Empty;

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

            // Background panel + border
            b.Draw(Game1.staminaRect, dest, ColPanel * 0.90f);
            DrawBorder(b, dest, ColBorder);

            if (screen == null || !screen.IsLiveViewActive)
            {
                // Dim message
                SpriteFont font = Game1.dialogueFont;
                string msg = "Live view\nnot active";
                float ts   = 0.28f;
                Vector2 sz = font.MeasureString(msg) * ts;
                b.DrawString(font, msg,
                    new Vector2(dest.Center.X - sz.X / 2f, dest.Center.Y - sz.Y / 2f),
                    ColText * 0.45f, 0f, Vector2.Zero, ts, SpriteEffects.None, 1f);
                return;
            }

            // --- Layout corner buttons ---
            int btnSz = CornerBtnSize;
            int pad   = CornerBtnPadding;

            boundsZoomOut  = new Rectangle(dest.X + pad,              dest.Y + pad,               btnSz, btnSz);
            boundsZoomIn   = new Rectangle(dest.Right - pad - btnSz,  dest.Y + pad,               btnSz, btnSz);
            boundsFlash    = new Rectangle(dest.X + pad,              dest.Bottom - pad - btnSz,  btnSz, btnSz);
            boundsCapture  = new Rectangle(dest.Right - pad - btnSz,  dest.Bottom - pad - btnSz,  btnSz, btnSz);

            joystickCenterX = dest.Center.X;
            joystickCenterY = dest.Center.Y;
            boundsJoystick  = new Rectangle(
                joystickCenterX - OuterRadius,
                joystickCenterY - OuterRadius,
                OuterRadius * 2,
                OuterRadius * 2);

            DrawLabelButton(b, boundsZoomOut, "-", ColBtnNormal, ColBtnHover, ColAccent, mx, my);
            DrawLabelButton(b, boundsZoomIn,  "+", ColBtnNormal, ColBtnHover, ColAccent, mx, my);
            DrawFlashButton(b, boundsFlash, screen.FlashEnabled, mx, my);
            DrawCaptureButton(b, boundsCapture, mx, my);
            DrawJoystick(b);
        }

        // -----------------------------------------------------------------------
        // Joystick drawing
        // -----------------------------------------------------------------------
        private void DrawJoystick(SpriteBatch b)
        {
            // Outer ring
            DrawFilledCircle(b, joystickCenterX, joystickCenterY, OuterRadius,     ColJoystickRim);
            DrawFilledCircle(b, joystickCenterX, joystickCenterY, OuterRadius - 3, ColJoystickBg);

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
            DrawFilledCircle(b, kx, ky, InnerRadius - 4, ColJoystickKnob * 1.3f);
        }

        // -----------------------------------------------------------------------
        // Button drawing
        // -----------------------------------------------------------------------
        private static void DrawLabelButton(SpriteBatch b, Rectangle bounds, string label,
                                            Color normal, Color hover, Color labelColor,
                                            int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            b.Draw(Game1.staminaRect, bounds, isHov ? hover : normal);
            DrawBorder(b, bounds, ColBorder);

            SpriteFont font  = Game1.smallFont;
            const float scale = 0.9f;
            Vector2 sz  = font.MeasureString(label) * scale;
            Vector2 pos = new Vector2(bounds.Center.X - sz.X / 2f, bounds.Center.Y - sz.Y / 2f + 3f);
            b.DrawString(font, label, pos + new Vector2(1, 1), Color.Black * 0.35f,
                         0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
            b.DrawString(font, label, pos, labelColor,
                         0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }

        private static void DrawFlashButton(SpriteBatch b, Rectangle bounds, bool active, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            Color bg   = active ? ColFlashActive * 0.85f : (isHov ? ColBtnHover : ColBtnNormal);
            b.Draw(Game1.staminaRect, bounds, bg);
            DrawBorder(b, bounds, ColBorder);

            string label    = "F";
            Color  labelCol = active ? Color.Black : ColFlashActive * 0.85f;
            SpriteFont font = Game1.smallFont;
            const float scale = 0.9f;
            Vector2 sz  = font.MeasureString(label) * scale;
            Vector2 pos = new Vector2(bounds.Center.X - sz.X / 2f, bounds.Center.Y - sz.Y / 2f + 3f);
            b.DrawString(font, label, pos, labelCol, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }

        private static void DrawCaptureButton(SpriteBatch b, Rectangle bounds, int mx, int my)
        {
            bool isHov = bounds.Contains(mx, my);
            Color bg   = (isHov ? ColCaptureHov : ColCapture) * 0.88f;
            b.Draw(Game1.staminaRect, bounds, bg);
            DrawBorder(b, bounds, ColBorder);

            // Minimal camera icon: large + small filled circles
            DrawFilledCircle(b, bounds.Center.X, bounds.Center.Y, 7, Color.White * 0.90f);
            DrawFilledCircle(b, bounds.Center.X, bounds.Center.Y, 4, Color.White * 0.40f);
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
            if (!joystickHeld) return;
            LiveCameraScreen? screen = getScreen();
            if (screen == null || !screen.IsLiveViewActive) return;

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
            if (joystickHeld)
            {
                joystickHeld = false;
                joystickDx   = 0f;
                joystickDy   = 0f;
                // Camera tile position stays updated; caller (ModEntry) persists via saveCallback
            }
        }

        // -----------------------------------------------------------------------
        // Pixel-circle helper (used for joystick + capture button icon)
        // -----------------------------------------------------------------------
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
