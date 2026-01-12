using UnityEngine;

namespace Sensocto
{
    /// <summary>
    /// Interface for vehicles that provide telemetry data.
    /// Implement this on vehicle controllers to provide real-time data.
    /// </summary>
    public interface IVehicleTelemetry
    {
        /// <summary>
        /// Current direction/steering angle normalized to -1 (left) to 1 (right).
        /// </summary>
        float Direction { get; }

        /// <summary>
        /// Current speed normalized to -1 (reverse) to 1 (forward).
        /// </summary>
        float Speed { get; }

        /// <summary>
        /// Current battery level from 0 (empty) to 1 (full).
        /// Return -1 if battery monitoring is not supported.
        /// </summary>
        float BatteryLevel { get; }

        /// <summary>
        /// Current velocity vector in world space.
        /// </summary>
        Vector3 Velocity { get; }

        /// <summary>
        /// Current position in world space.
        /// </summary>
        Vector3 Position { get; }

        /// <summary>
        /// Current rotation/heading in world space.
        /// </summary>
        Quaternion Rotation { get; }
    }
}
