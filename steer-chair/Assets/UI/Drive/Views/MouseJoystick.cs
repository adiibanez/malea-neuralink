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
    private Vector2 rawMousePosition;
    private Vector2 localCenter;
    private bool _inDeadZone;
    private VisualElement _deadZoneElement;

    public MouseJoystick()
    {
        generateVisualContent += OnGenerateVisualContent;

        RegisterCallback<AttachToPanelEvent>(evt =>
        {
            _deadZoneElement = panel.visualTree.Q("DeadZone");
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
        Vector2 newMousePosition = panel.visualTree.ChangeCoordinatesTo(this, panelPosition);               // mouse in localspace of panel

        if((rawMousePosition - newMousePosition).sqrMagnitude < 0.5f)
            return;

        _inDeadZone = IsInDeadZone(newMousePosition);
        rawMousePosition = newMousePosition;

        Vector2 effectivePosition = _inDeadZone ? localCenter : newMousePosition;
        mousePosition = effectivePosition;

        RaiseJoystickEvent(effectivePosition);
        MarkDirtyRepaint();
    }

    private bool IsInDeadZone(Vector2 localPosition)
    {
        if(_deadZoneElement == null)
            return false;

        // Convert from MouseJoystick local space to DeadZone local space
        Vector2 inDeadZone = this.ChangeCoordinatesTo(_deadZoneElement, localPosition);
        // Use localBound size (includes padding) so the check matches the visual ellipse
        Rect rect = new Rect(0, 0, _deadZoneElement.localBound.width, _deadZoneElement.localBound.height);

        // Ellipse check against the DeadZone element's content rect
        float cx = rect.center.x;
        float cy = rect.center.y;
        float rx = rect.width * 0.5f;
        float ry = rect.height * 0.5f;

        float dx = inDeadZone.x - cx;
        float dy = inDeadZone.y - cy;

        return (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry) <= 1f;
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
                    Mathf.Clamp(rawMousePosition.x, contentRect.xMin, contentRect.xMax),
                    Mathf.Clamp(rawMousePosition.y, contentRect.yMin, contentRect.yMax)
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

        Color arrowColor = _inDeadZone ? Color.black : Color.red;
        painter.strokeColor = arrowColor;
        painter.fillColor = arrowColor;
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
