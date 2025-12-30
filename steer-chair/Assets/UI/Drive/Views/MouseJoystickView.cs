using UnityEngine;
using UnityEngine.UIElements;

[UxmlElement]
public partial class MouseJoystickView : VisualElement
{
    private Vector2 centerPosition;
    private Vector2 mousePosition;
    private bool hasPointer;

    public MouseJoystickView()
    {
        Debug.Log("MouseJoystickView Ctor");

        RegisterCallback<PointerMoveEvent>(OnPointerMove);
        RegisterCallback<PointerEnterEvent>(_ => hasPointer = true);
        RegisterCallback<PointerLeaveEvent>(_ => hasPointer = false);

        generateVisualContent += OnGenerateVisualContent;
    }

    private void OnPointerMove(PointerMoveEvent evt)
    {
        Debug.Log("OnPointerMove");

        mousePosition = evt.localPosition;
        MarkDirtyRepaint();
    }

    private void OnGenerateVisualContent(MeshGenerationContext ctx)
    {
        if (!hasPointer)
            return;

        centerPosition = contentRect.center;

        DrawArrow(ctx.painter2D, centerPosition, mousePosition);
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
