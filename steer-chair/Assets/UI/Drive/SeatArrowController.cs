using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Seat adjustment via JoystickController.Move().
/// Up/Down: continuous movement while mouse hovered.
/// Left/Right: single burst per click (resilient to mouse jitter).
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class SeatArrowController : MonoBehaviour
{
    [SerializeField] private ModeIndicator modeIndicator;

    private JoystickController _joystickController;
    private VisualElement _root;
    private VisualElement _seatArrowContainer;
    private Button _upBtn, _downBtn, _leftBtn, _rightBtn;
    private Coroutine _sendCoroutine;
    private Button _activeBtn;

    private const string ActiveClass = "seat-arrow-btn-active";

    // Stored callbacks for proper unregistration
    private readonly Dictionary<Button, EventCallback<MouseEnterEvent>> _enterCallbacks = new();
    private readonly Dictionary<Button, EventCallback<MouseLeaveEvent>> _leaveCallbacks = new();
    private readonly Dictionary<Button, EventCallback<ClickEvent>> _clickCallbacks = new();

    // Direction vectors matching joystick axes
    private static readonly Vector2 DirUp    = new Vector2(0, 1);
    private static readonly Vector2 DirDown  = new Vector2(0, -1);
    private static readonly Vector2 DirLeft  = new Vector2(-1, 0);
    private static readonly Vector2 DirRight = new Vector2(1, 0);

    private const float TickRate = 0.05f;
    private const float ClickBurstDuration = 0.3f;

    void OnEnable()
    {
        StartCoroutine(InitializeDelayed());
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        _joystickController = FindFirstObjectByType<JoystickController>();

        if (modeIndicator == null)
            modeIndicator = GetComponent<ModeIndicator>();
        if (modeIndicator == null)
            modeIndicator = FindFirstObjectByType<ModeIndicator>();

        _root = GetComponent<UIDocument>().rootVisualElement;
        if (_root == null) yield break;

        _seatArrowContainer = _root.Q<VisualElement>(DriveUIElementNames.SeatArrowContainer);

        _upBtn    = _root.Q<Button>(DriveUIElementNames.SeatUpBtn);
        _downBtn  = _root.Q<Button>(DriveUIElementNames.SeatDownBtn);
        _leftBtn  = _root.Q<Button>(DriveUIElementNames.SeatLeftBtn);
        _rightBtn = _root.Q<Button>(DriveUIElementNames.SeatRightBtn);

        // Up/Down: mouseover continuous
        RegisterHoverButton(_upBtn, DirUp);
        RegisterHoverButton(_downBtn, DirDown);

        // Left/Right: click burst
        RegisterClickButton(_leftBtn, DirLeft);
        RegisterClickButton(_rightBtn, DirRight);

        if (modeIndicator != null)
        {
            modeIndicator.OnModeChanged += OnModeChanged;
            SetArrowsVisible(modeIndicator.IsSeatAdjustmentMode);
        }

        Debug.Log("[SeatArrowController] Initialized");
    }

    void OnDisable()
    {
        StopSending();

        if (modeIndicator != null)
            modeIndicator.OnModeChanged -= OnModeChanged;

        UnregisterHoverButton(_upBtn);
        UnregisterHoverButton(_downBtn);
        UnregisterClickButton(_leftBtn);
        UnregisterClickButton(_rightBtn);
    }

    private void OnModeChanged(ModeIndicator.OperatingMode newMode)
    {
        bool show = newMode == ModeIndicator.OperatingMode.SeatAdjustment;
        if (!show)
            StopSending();
        SetArrowsVisible(show);
    }

    private void SetArrowsVisible(bool visible)
    {
        if (_seatArrowContainer != null)
            _seatArrowContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // --- Hover buttons (Up/Down) ---

    private void RegisterHoverButton(Button btn, Vector2 direction)
    {
        if (btn == null) return;

        EventCallback<MouseEnterEvent> enterCb = evt => OnHoverEnter(btn, direction);
        EventCallback<MouseLeaveEvent> leaveCb = evt => OnHoverLeave();

        btn.RegisterCallback(enterCb);
        btn.RegisterCallback(leaveCb);

        _enterCallbacks[btn] = enterCb;
        _leaveCallbacks[btn] = leaveCb;
    }

    private void UnregisterHoverButton(Button btn)
    {
        if (btn == null) return;

        if (_enterCallbacks.TryGetValue(btn, out var enterCb))
        {
            btn.UnregisterCallback(enterCb);
            _enterCallbacks.Remove(btn);
        }
        if (_leaveCallbacks.TryGetValue(btn, out var leaveCb))
        {
            btn.UnregisterCallback(leaveCb);
            _leaveCallbacks.Remove(btn);
        }
    }

    private void OnHoverEnter(Button btn, Vector2 direction)
    {
        if (!btn.enabledSelf) return;
        StopSending();
        _activeBtn = btn;
        btn.AddToClassList(ActiveClass);
        _sendCoroutine = StartCoroutine(SendContinuousLoop(direction));
    }

    private void OnHoverLeave()
    {
        StopSending();
        _joystickController?.Move(Vector2.zero);
    }

    // --- Click buttons (Left/Right) ---

    private void RegisterClickButton(Button btn, Vector2 direction)
    {
        if (btn == null) return;

        EventCallback<ClickEvent> clickCb = evt => OnClickActivate(btn, direction);
        btn.RegisterCallback(clickCb);
        _clickCallbacks[btn] = clickCb;
    }

    private void UnregisterClickButton(Button btn)
    {
        if (btn == null) return;

        if (_clickCallbacks.TryGetValue(btn, out var clickCb))
        {
            btn.UnregisterCallback(clickCb);
            _clickCallbacks.Remove(btn);
        }
    }

    private void OnClickActivate(Button btn, Vector2 direction)
    {
        if (!btn.enabledSelf) return;
        StopSending();
        _activeBtn = btn;
        btn.AddToClassList(ActiveClass);
        _sendCoroutine = StartCoroutine(SendClickBurst(direction));
    }

    // --- Shared ---

    private void ClearActiveState()
    {
        if (_activeBtn != null)
        {
            _activeBtn.RemoveFromClassList(ActiveClass);
            _activeBtn = null;
        }
    }

    private void StopSending()
    {
        if (_sendCoroutine != null)
        {
            StopCoroutine(_sendCoroutine);
            _sendCoroutine = null;
        }
        ClearActiveState();
    }

    private IEnumerator SendContinuousLoop(Vector2 direction)
    {
        while (true)
        {
            _joystickController?.Move(direction);
            yield return new WaitForSeconds(TickRate);
        }
    }

    private IEnumerator SendClickBurst(Vector2 direction)
    {
        float elapsed = 0f;
        while (elapsed < ClickBurstDuration)
        {
            _joystickController?.Move(direction);
            yield return new WaitForSeconds(TickRate);
            elapsed += TickRate;
        }

        _joystickController?.Move(Vector2.zero);
        ClearActiveState();
        _sendCoroutine = null;
    }

    public void SetAllButtonsEnabled(bool enabled)
    {
        _upBtn?.SetEnabled(enabled);
        _downBtn?.SetEnabled(enabled);
        _leftBtn?.SetEnabled(enabled);
        _rightBtn?.SetEnabled(enabled);

        if (!enabled)
            StopSending();
    }
}
