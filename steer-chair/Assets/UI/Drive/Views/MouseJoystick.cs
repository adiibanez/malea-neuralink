using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SteerChair.UI
{
    [UxmlElement]
    public partial class MouseJoystick : VisualElement
    {
    // Vector to skew axis of the joystick, defaults to 
    // X: 0.5f left and right weightet the same
    // Y: 0.6f front weighted 60% vs 40% back
    public Vector2 centerRatio = new Vector2(0.5f, 0.6f);
    private Vector2 mousePosition;
    private Vector2 localCenter;

    // Gravity pull: retract arrow to center when cursor is still
    private const float StillnessThresholdSqr = 4.0f; // ~2px/frame delta; below = "still"
    private const float StillnessDuration = 0.15f;     // seconds of stillness before gravity kicks in
    private const float GravityPullSpeed = 300f;       // px/sec toward center (~1s full retraction)

    private Vector2 _rawCursorPosition;
    private float _lastCursorMovementTime = float.MaxValue; // prevents gravity before first real movement
    private bool _gravityActive;
    private float _lastCheckTime;

    public MouseJoystick()
    {
        generateVisualContent += OnGenerateVisualContent;

        RegisterCallback<AttachToPanelEvent>(evt =>
        {
            schedule
            .Execute(CheckMouseMove)
            .Every(16);
        });
    }

    private void CheckMouseMove()
    {
        if(panel == null)
            return;

        Vector2 inputSystemPosition = Mouse.current.position.ReadValue();                                   // Mouse from InputSystem
        Vector2 screenPosition = new Vector2(inputSystemPosition.x, Screen.height - inputSystemPosition.y); // Mouse in Screen space with y starting top
        Vector2 panelPosition = panel.contextType == ContextType.Player
            ? RuntimePanelUtils.ScreenToPanel(panel, screenPosition)                                        // cast to Panel space
            : contentRect.size/4;
        Vector2 rawPosition = panel.visualTree.ChangeCoordinatesTo(this, panelPosition);                    // mouse in localspace of panel

        // Detect cursor movement
        float cursorDeltaSqr = (rawPosition - _rawCursorPosition).sqrMagnitude;
        _rawCursorPosition = rawPosition;

        float now = Time.unscaledTime;
        float dt = now - _lastCheckTime;
        _lastCheckTime = now;

        if(dt <= 0f || dt > 0.5f)
            dt = 0.016f; // sane fallback on first call or after long stall

        if(cursorDeltaSqr > StillnessThresholdSqr)
        {
            // Cursor is moving — track directly, disable gravity
            _lastCursorMovementTime = now;
            _gravityActive = false;

            Vector2 newMousePosition = IsInDeadZone(rawPosition) ? localCenter : rawPosition;

            if((mousePosition - newMousePosition).sqrMagnitude < 0.5f)
                return;

            mousePosition = newMousePosition;
        }
        else
        {
            // Cursor is still
            float stillTime = now - _lastCursorMovementTime;

            if(stillTime < StillnessDuration)
                return; // grace period — hold current output

            // Gravity active — pull output toward center
            _gravityActive = true;
            Vector2 pulled = Vector2.MoveTowards(mousePosition, localCenter, GravityPullSpeed * dt);

            if((mousePosition - pulled).sqrMagnitude < 0.5f)
            {
                if((mousePosition - localCenter).sqrMagnitude < 0.5f)
                    return; // already at center, nothing to do
                mousePosition = localCenter;
            }
            else
            {
                mousePosition = pulled;
            }
        }

        RaiseJoystickEvent(mousePosition);
        MarkDirtyRepaint();
    }

    private bool IsInDeadZone(Vector2 newMousePosition)
    {
        // Dead zone radii
        float deadZoneX = 125f;
        float deadZoneY = 75f;

        // Distance from center
        float dx = newMousePosition.x - localCenter.x;
        float dy = newMousePosition.y - localCenter.y;

        // Check if inside ellipse
        return (dx * dx) / (deadZoneX * deadZoneX) + (dy * dy) / (deadZoneY * deadZoneY) <= 1f;
    }

    private void RaiseJoystickEvent(Vector2 mousePositon)
    {
        if(!enabledInHierarchy)
            return;

        // Get direction with -x left, +x right, +y up, -y down
        Vector2 direction = new Vector2(
            mousePosition.x - localCenter.x,
            localCenter.y - mousePosition.y
        );
        // Get distorionfactor per Axis
        Vector2 centerFactors = new Vector2(
            direction.x > 0 ? centerRatio.x : 1f - centerRatio.x,
            direction.y > 0 ? centerRatio.y : 1f - centerRatio.y
        );
        // Normalize Vector to unit-circle
        Vector2 normalizedDirection = new Vector2(
            Mathf.Clamp(direction.x / (centerFactors.x * contentRect.size.x), -1, 1),
            Mathf.Clamp(direction.y / (centerFactors.y * contentRect.size.y), -1, 1));

        var evt = JoystickMoveEvent.Get(normalizedDirection);
        evt.target = this;
        SendEvent(evt);
    }


    private void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        localCenter = Vector2.Scale(contentRect.size, centerRatio);

        DrawCross(ctx.painter2D, localCenter);

        if(enabledInHierarchy)
        {
            DrawArrow(ctx.painter2D, localCenter, 
                new Vector2(
                    Mathf.Clamp(mousePosition.x, contentRect.xMin, contentRect.xMax),
                    Mathf.Clamp(mousePosition.y, contentRect.yMin, contentRect.yMax)
                )
            );
        }
    }

    private void DrawCross(Painter2D painter, Vector2 center)
    {
        painter.strokeColor = Color.black;
        painter.lineWidth = 2f;

        painter.BeginPath();
        painter.MoveTo(new Vector2(contentRect.xMin, center.y));
        painter.LineTo(new Vector2(contentRect.xMax, center.y));
        painter.Stroke();

        painter.BeginPath();
        painter.MoveTo(new Vector2(center.x, contentRect.yMin));
        painter.LineTo(new Vector2(center.x, contentRect.yMax));
        painter.Stroke();
    }

    private void DrawArrow(Painter2D painter, Vector2 from, Vector2 to)
    {
        const float arrowHeadLength = 12f;
        const float arrowHeadAngle = 25f;

        painter.strokeColor = Color.red;
        painter.fillColor = Color.red;
        painter.lineWidth = 2f;

        // Main line
        painter.BeginPath();
        painter.MoveTo(from);
        painter.LineTo(to);
        painter.Stroke();

        // Arrow head
        Vector2 direction = (from - to).normalized;
        Vector2 right = Quaternion.Euler(0, 0, arrowHeadAngle) * direction;
        Vector2 left  = Quaternion.Euler(0, 0, -arrowHeadAngle) * direction;

        painter.BeginPath();
        painter.MoveTo(to);
        painter.LineTo(to + right * arrowHeadLength);
        painter.LineTo(to + left * arrowHeadLength);
        painter.ClosePath();
        painter.Fill();
    }
}
}
