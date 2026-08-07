// =============================================================================
// ModEntry.cs  --  Mod Entry Point for Smartphone Live Camera
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using SmartphoneLiveCamera.Data;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SmartphoneLiveCamera
{
    internal sealed class ModEntry : Mod
    {
        // -------------------------------------------------------------------------
        // Constants
        // -------------------------------------------------------------------------

        private const string SmartphoneModId = "d5a1lamdtd.Smartphone";
        private const string LiveCameraAppId  = "live_camera";

        // -------------------------------------------------------------------------
        // Fields
        // -------------------------------------------------------------------------

        internal static ModConfig Config { get; private set; } = new();
        internal static Action? SaveConfigCallback { get; private set; }

        private ISmartPhoneApi? smartphoneApi;
        internal static IMonitor? SMonitor;

        private LiveCameraScreen? activeScreen;
        private LiveCameraControllerHud? controllerHud;

        private List<CameraEntry> cameras = new();
        private string? cameraSaveFilePath;

        private Texture2D? appIcon;

        // -------------------------------------------------------------------------
        // Entry
        // -------------------------------------------------------------------------

        internal static ModEntry? Instance { get; private set; }

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            SMonitor = Monitor;
            Config = helper.ReadConfig<ModConfig>();
            SaveConfigCallback = () => helper.WriteConfig(Config);

            helper.Events.GameLoop.GameLaunched  += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded    += OnSaveLoaded;
            helper.Events.GameLoop.Saving        += OnSaving;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        }

        // -------------------------------------------------------------------------
        // Event Handlers
        // -------------------------------------------------------------------------

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            RegisterGmcm();

            smartphoneApi = Helper.ModRegistry.GetApi<ISmartPhoneApi>(SmartphoneModId);

            if (smartphoneApi == null)
            {
                Monitor.Log("Smartphone API not found. Live Camera app was not registered.", LogLevel.Warn);
                return;
            }

            appIcon = TryLoadTexture("assets/default/1x1.png");

            bool registered = smartphoneApi.RegisterPhoneApp(
                ownerModId:         ModManifest.UniqueID,
                appId:              LiveCameraAppId,
                displayName:        "Live Camera",
                onClick:            OpenApp,
                closePhoneOnLaunch: true,
                supportedSizes:     new[] { AppSize.Size1x1 },
                themedIconTextures: appIcon != null
                    ? new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase)
                      {
                          { "default", appIcon }
                      }
                    : null);

            if (!registered)
                Monitor.Log("Failed to register Live Camera app.", LogLevel.Warn);
            else
            {
                // Register interactive controller overlay (joystick + buttons beside slider).
                // The controller references the screen lazily via a Func so it always
                // sees the most-recently-created screen even if OpenApp() rebuilds it.
                controllerHud = new LiveCameraControllerHud(() => activeScreen);
                smartphoneApi.RegisterPassiveHudOverlay(
                    ownerModId:          ModManifest.UniqueID,
                    appId:               LiveCameraAppId,
                    onDrawHudOverlay:    (b, dest) => controllerHud.Draw(b, dest),
                    onLeftClick:         (x, y)   => controllerHud.OnLeftClick(x, y),
                    onLeftClickHeld:     (x, y)   => controllerHud.OnLeftClickHeld(x, y),
                    onReleaseLeftClick:  ()        => { controllerHud.OnReleaseLeftClick(); SaveCameras(); },
                    getOverlayHeight:    ()        => controllerHud.GetOverlayHeight());
            }
        }

        internal void RegisterLiveViewPassiveHud()
        {
            smartphoneApi?.RegisterPassiveHudCallback(
                ownerModId:        ModManifest.UniqueID,
                appId:             LiveCameraAppId,
                onDrawHudScreen:   DrawPassiveHud,
                onUpdateHudScreen: UpdatePassiveHud,
                landscape:         true);
        }

        internal void UnregisterLiveViewPassiveHud()
        {
            smartphoneApi?.RegisterPassiveHudCallback(
                ownerModId:        ModManifest.UniqueID,
                appId:             LiveCameraAppId,
                onDrawHudScreen:   null!,
                onUpdateHudScreen: null,
                landscape:         true);
        }

        private void RegisterGmcm()
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm == null) return;

            gmcm.Register(
                mod: ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => Helper.WriteConfig(Config)
            );

            gmcm.AddNumberOption(
                mod: ModManifest,
                getValue: () => Config.CaptureRateSeconds,
                setValue: value => Config.CaptureRateSeconds = (float)Math.Round(value * 4f) / 4f,
                name: () => "Capture Rate (seconds)",
                tooltip: () => "Time interval in seconds between live camera feed updates (0.0s to 20.0s, 0 = real-time continuous).",
                min: 0.0f,
                max: 20.0f,
                interval: 0.25f
            );
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            string saveFolder = Constants.CurrentSavePath ?? Helper.DirectoryPath;
            cameraSaveFilePath = Path.Combine(saveFolder, "live_cameras.json");
            LoadCameras();
        }

        private void OnSaving(object? sender, SavingEventArgs e) => SaveCameras();

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            cameras.Clear();
            activeScreen = null;
            cameraSaveFilePath = null;
        }

        // -------------------------------------------------------------------------
        // App Launcher
        // -------------------------------------------------------------------------

        private void OpenApp()
        {
            if (!Context.IsWorldReady || smartphoneApi == null) return;

            bool resume = smartphoneApi.IsHudPinned() &&
                          string.Equals(smartphoneApi.GetPinnedAppId(),
                              $"{ModManifest.UniqueID}::{LiveCameraAppId}");

            if (!resume || activeScreen == null)
            {
                activeScreen = new LiveCameraScreen(
                    api:          smartphoneApi,
                    onBack:       () => smartphoneApi.OpenPhoneHomeScreen(),
                    cameras:      cameras,
                    saveCallback: SaveCameras);
            }

            Game1.activeClickableMenu = activeScreen;
        }

        // -------------------------------------------------------------------------
        // Passive HUD
        // -------------------------------------------------------------------------

        private void DrawPassiveHud(SpriteBatch b, Rectangle dest)
        {
            if (activeScreen != null)
                activeScreen.DrawScreenContent(b, dest);
            else
            {
                Texture2D? bg = smartphoneApi?.GetPhoneBackgroundTexture();
                if (bg != null && !bg.IsDisposed) b.Draw(bg, dest, Color.White);
                else b.Draw(Game1.staminaRect, dest, new Color(12, 14, 22));
            }
        }

        private void UpdatePassiveHud(GameTime time)
        {
            if (Game1.activeClickableMenu == activeScreen) return;
            activeScreen?.update(time);
        }

        // -------------------------------------------------------------------------
        // Persistence
        // -------------------------------------------------------------------------

        private void LoadCameras()
        {
            cameras.Clear();
            if (string.IsNullOrWhiteSpace(cameraSaveFilePath) || !File.Exists(cameraSaveFilePath))
                return;

            try
            {
                string json = File.ReadAllText(cameraSaveFilePath);
                cameras = JsonConvert.DeserializeObject<List<CameraEntry>>(json) ?? new List<CameraEntry>();
                Monitor.Log($"Loaded {cameras.Count} camera(s) from save.", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to load cameras: {ex.Message}", LogLevel.Warn);
            }
        }

        private void SaveCameras()
        {
            if (string.IsNullOrWhiteSpace(cameraSaveFilePath)) return;
            try
            {
                string json = JsonConvert.SerializeObject(cameras, Formatting.Indented);
                File.WriteAllText(cameraSaveFilePath, json);
                Monitor.Log($"Saved {cameras.Count} camera(s).", LogLevel.Trace);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to save cameras: {ex.Message}", LogLevel.Warn);
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        /// <summary>Loads a texture from the mod content folder, returning null on failure.</summary>
        private Texture2D? TryLoadTexture(string relativePath)
        {
            try
            {
                return Helper.ModContent.Load<Texture2D>(relativePath);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Could not load texture '{relativePath}': {ex.Message}", LogLevel.Trace);
                return null;
            }
        }
    }
}