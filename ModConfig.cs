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
        private float captureRateSeconds = 0.0f;

        /// <summary>
        /// Time interval in seconds between live camera frame updates (0.0s to 20.0s).
        /// Default is 0.0 seconds (real-time continuous refresh).
        /// </summary>
        public float CaptureRateSeconds
        {
            get => captureRateSeconds;
            set => captureRateSeconds = System.Math.Clamp(value, 0.0f, 20.0f);
        }
    }
}

