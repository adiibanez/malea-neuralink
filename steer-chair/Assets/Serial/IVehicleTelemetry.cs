using UnityEngine;

/// <summary>
/// Interface for components that provide vehicle telemetry data.
/// </summary>
public interface IVehicleTelemetry
{
    /// <summary>
    /// The current steering direction (-1 to 1, where negative is left, positive is right).
    /// </summary>
    float Direction { get; }

    /// <summary>
    /// The current speed (-1 to 1, where negative is backward, positive is forward).
    /// </summary>
    float Speed { get; }

    /// <summary>
    /// The current battery level (0 to 1, or -1 if not supported).
    /// </summary>
    float BatteryLevel { get; }

    /// <summary>
    /// The current velocity vector in world space.
    /// </summary>
    Vector3 Velocity { get; }

    /// <summary>
    /// The current position in world space.
    /// </summary>
    Vector3 Position { get; }

    /// <summary>
    /// The current rotation in world space.
    /// </summary>
    Quaternion Rotation { get; }
}
