using UnityEngine;
using UnityEngine.UIElements;

public class JoystickMoveEvent : EventBase<JoystickMoveEvent>
{
    public Vector2 Direction { get; private set; }

    public static JoystickMoveEvent Get(Vector2 direction)
    {
        var evt = GetPooled();
        evt.Direction = direction;
        return evt;
    }
}
