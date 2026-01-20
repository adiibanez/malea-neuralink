using UnityEngine;

/// <summary>
/// Interface for components that receive movement commands.
/// </summary>
public interface IMoveReceiver
{
    /// <summary>
    /// Receives a movement direction command.
    /// </summary>
    /// <param name="direction">The movement direction vector (x: left/right, y: forward/backward)</param>
    void Move(Vector2 direction);
}
