// =============================================================================
// ModConfig.cs  --  Configuration settings for Smartphone Live Camera
// =============================================================================

namespace SmartphoneLiveCamera
{
    /// <summary>
    /// Configuration options for the Smartphone Live Camera mod.
    /// Persisted via SMAPI to config.json.
    /// </summary>
    public class ModConfig
    {
        private float captureRateSeconds = 2.0f;

        /// <summary>
        /// Time interval in seconds between live camera frame updates (0.5s to 20.0s).
        /// Default is 2.0 seconds.
        /// </summary>
        public float CaptureRateSeconds
        {
            get => captureRateSeconds;
            set => captureRateSeconds = System.Math.Clamp(value, 0.5f, 20.0f);
        }
    }
}
