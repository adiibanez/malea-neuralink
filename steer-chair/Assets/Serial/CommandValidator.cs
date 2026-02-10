using UnityEngine;

/// <summary>
/// Static utility for validating and clamping wheelchair serial commands.
/// Command format: S{speed:D2}D{direction:D2}R{robot} where speed/direction are 0-63, robot is 1-8.
/// </summary>
public static class CommandValidator
{
    public const int MinValue = 0;
    public const int MaxValue = 63;
    public const int Neutral = 31;
    public const int MinRobot = 1;
    public const int MaxRobot = 8;

    /// <summary>
    /// Parses and validates a S##D##R# command string.
    /// </summary>
    /// <returns>True if the command is valid.</returns>
    public static bool TryParseCommand(string cmd, out int speed, out int direction, out int robot, out string error)
    {
        speed = 0;
        direction = 0;
        robot = 0;
        error = null;

        if (string.IsNullOrEmpty(cmd))
        {
            error = "Command is null or empty";
            return false;
        }

        // Find marker positions
        int sIndex = cmd.IndexOf('S');
        int dIndex = cmd.IndexOf('D');
        int rIndex = cmd.IndexOf('R');

        if (sIndex < 0 || dIndex < 0 || rIndex < 0)
        {
            error = $"Missing S/D/R markers in '{cmd}'";
            return false;
        }

        if (!(sIndex < dIndex && dIndex < rIndex))
        {
            error = $"Markers out of order in '{cmd}' (expected S..D..R..)";
            return false;
        }

        // Parse speed
        string speedStr = cmd.Substring(sIndex + 1, dIndex - sIndex - 1);
        if (!int.TryParse(speedStr, out speed))
        {
            error = $"Invalid speed '{speedStr}' in '{cmd}'";
            return false;
        }

        // Parse direction
        string dirStr = cmd.Substring(dIndex + 1, rIndex - dIndex - 1);
        if (!int.TryParse(dirStr, out direction))
        {
            error = $"Invalid direction '{dirStr}' in '{cmd}'";
            return false;
        }

        // Parse robot
        string robotStr = cmd.Substring(rIndex + 1);
        if (!int.TryParse(robotStr, out robot))
        {
            error = $"Invalid robot '{robotStr}' in '{cmd}'";
            return false;
        }

        // Range checks
        if (speed < MinValue || speed > MaxValue)
        {
            error = $"Speed {speed} out of range [{MinValue},{MaxValue}]";
            return false;
        }

        if (direction < MinValue || direction > MaxValue)
        {
            error = $"Direction {direction} out of range [{MinValue},{MaxValue}]";
            return false;
        }

        // Allow 0-indexed relay values (0-7) as well as robot 8
        if (robot < 0 || robot > MaxRobot)
        {
            error = $"Robot {robot} out of range [0,{MaxRobot}]";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Clamps speed around neutral (31) by maxDelta.
    /// If maxDelta is 0, no clamping is applied (preserves current behavior).
    /// </summary>
    public static int ClampSpeed(int speed, int maxDelta)
    {
        if (maxDelta <= 0) return speed;

        int min = Mathf.Max(MinValue, Neutral - maxDelta);
        int max = Mathf.Min(MaxValue, Neutral + maxDelta);
        return Mathf.Clamp(speed, min, max);
    }
}
