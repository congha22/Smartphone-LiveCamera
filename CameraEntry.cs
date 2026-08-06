// =============================================================================
// CameraEntry.cs  --  Data model for a single placed camera
// =============================================================================

using System;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace SmartphoneLiveCamera
{
    /// <summary>
    /// Represents a single camera that the player has registered in the Live Camera app.
    /// Instances are persisted to the save data JSON file.
    /// </summary>
    public class CameraEntry
    {
        /// <summary>Unique identifier for this camera.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// User-visible display name (auto-generated as "Location (X, Y)" when placed).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The internal name of the GameLocation (e.g. "Farm", "Town", "Barn").</summary>
        public string LocationName { get; set; } = string.Empty;

        /// <summary>The tile coordinates the camera is centred on.</summary>
        public float TileX { get; set; }
        public float TileY { get; set; }

        /// <summary>Zoom level for this camera (default 1.0f).</summary>
        public float ZoomLevel { get; set; } = 1.0f;

        /// <summary>Returns the tile position as a Vector2 (ignored during JSON serialization).</summary>
        [JsonIgnore]
        public Vector2 TilePosition => new Vector2(TileX, TileY);
    }
}
