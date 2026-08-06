// =============================================================================
// ISmartPhoneApi.cs  --  Local copy of the Smartphone framework public API
// =============================================================================
// Keep this file in sync with the framework''s Api/ISmartPhoneApi.cs.
// =============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;

namespace SmartphoneLiveCamera.Data
{
    public enum AppIconType
    {
        Notification,
        AppStore,
        Camera,
        Photo,
        Setting,
        Calendar
    }

    public enum AppSize
    {
        Size1x1,
        Size2x1,
        Size2x2,
        Size2x3,
        Size2x4,
        Size4x2,
        Size4x3,
        Size4x4,
    }

    public class SelectedPhotoResult
    {
        public string AbsolutePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public byte[]? TextureData { get; set; }
    }

    public interface IContactActionCardButton
    {
        string Text { get; set; }
        Color BackgroundColor { get; set; }
        Color TextColor { get; set; }
        Action<string>? OnClick { get; set; }
    }

    public interface ISmartPhoneApi
    {
        bool RegisterPhoneApp(
            string ownerModId,
            string appId,
            string displayName,
            Action onClick,
            bool closePhoneOnLaunch = true,
            Rectangle? sourceRect = null,
            Func<int>? getBadgeCount = null,
            AppSize[]? supportedSizes = null,
            Action<SpriteBatch, Rectangle, AppSize>? onDrawWidget = null,
            Dictionary<string, Texture2D>? themedIconTextures = null
        );

        bool UnregisterPhoneApp(string ownerModId, string appId);
        Texture2D? GetAppIconTexture(string appId);
        Texture2D? GetAppTexture(AppIconType appIconType);

        bool OpenPhoneHomeScreen();
        (int x, int y) GetPhonePosition();
        void SetPhonePosition(int x, int y);
        bool HandlePhoneAppBottomNavClick(int x, int y, int phoneX, int phoneY, Action? onBack = null);

        float GetPhoneUiScale();
        int GetPhoneFrameWidth();
        int GetPhoneFrameHeight();
        (int offsetX, int offsetY) GetPhoneContentOffset();
        Texture2D? GetPhoneFrameTexture();
        Texture2D? GetPhoneBackgroundTexture();
        Texture2D? GetCardTexture();
        void SetComponentTheme(string component, string theme);
        string GetComponentTheme(string component);

        void DrawPhoneSizeButtons(SpriteBatch b, int phoneX, int phoneY, bool landscape = false, bool forceOn = false);
        bool HandlePhoneSizeButtonsClick(int x, int y, int phoneX, int phoneY);
        string GetDecreaseSizeKey();
        string GetIncreaseSizeKey();
        void AdjustPhoneSize(float amount);

        bool RegisterContactActionCard(string modId, string cardTitle, IList<IContactActionCardButton> buttons, List<string> npcNames = null);

        void SendSmartphoneNotification(string message, string notificationName = "", string playerId = "");

        string CaptureNpcPhoto(GameLocation targetLocation, Vector2 captureCenter, NPC npc = null,
            bool landscape = false, bool square = false, List<NPC>? visibleNpcAtTarget = null,
            float zoomLevel = 1f, int? captureTimeOfDay = null, string saveLocation = null);

        /// <summary>
        /// Renders targetLocation centred on captureCenter into an existing renderTarget without saving to disk.
        /// Intended for low-frequency live-feed rendering (e.g. 1 frame every 3 seconds).
        /// </summary>
        bool CaptureLiveFeedFrame(GameLocation targetLocation, Vector2 captureCenter, RenderTarget2D renderTarget, float zoomLevel = 1f);

        Texture2D GetPlayerPhotoTexture(string photoName);
        string GetPlayerPhotoMetadata(string photoName);
        void RetrievePhotos(int limit, bool getTexture, bool getMetadata, Action<string> onComplete, bool squareOnly = false);

        bool RegisterPassiveHudCallback(
            string ownerModId,
            string appId,
            Action<SpriteBatch, Rectangle> onDrawHudScreen,
            Action<GameTime>? onUpdateHudScreen = null,
            bool landscape = false
        );

        bool IsHudPinned();
        void SetHudPinned(bool pinned);
        string? GetPinnedAppId();

        /// <summary>
        /// Registers an interactive overlay panel drawn adjacent to the HUD phone icon slider.
        /// Only visible while the HUD is active and the user hovers over the icon, slider, or overlay.
        /// </summary>
        bool RegisterPassiveHudOverlay(
            string ownerModId,
            string appId,
            Action<SpriteBatch, Rectangle> onDrawHudOverlay,
            Func<int, int, bool>? onLeftClick = null,
            Action<int, int>? onLeftClickHeld = null,
            Action? onReleaseLeftClick = null,
            Func<int>? getOverlayHeight = null
        );
    }
}
