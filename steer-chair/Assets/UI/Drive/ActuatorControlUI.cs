using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Provides +/- controls for the currently selected actuator.
/// Large, easy-to-use buttons designed for Eoin.
/// Only active in DriveChair scene.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ActuatorControlUI : MonoBehaviour
{
    private const string DRIVE_CHAIR_SCENE = "DriveChair";
    [Header("References")]
    [SerializeField] private ModeController modeController;
    [SerializeField] private WheelchairState wheelchairState;

    [Header("Styling")]
    [SerializeField] private Color increaseColor = new Color(0.30f, 0.69f, 0.31f);  // Green
    [SerializeField] private Color decreaseColor = new Color(0.96f, 0.26f, 0.21f);  // Red
    [SerializeField] private Color disabledColor = new Color(0.7f, 0.7f, 0.7f);

    private VisualElement _root;
    private VisualElement _controlPanel;
    private Label _actuatorLabel;
    private Button _increaseBtn;
    private Button _decreaseBtn;
    private Button _backToDriveBtn;
    private bool _isHolding = false;
    private float _holdStartTime;
    private string _holdAction;

    void OnEnable()
    {
        // Only initialize in DriveChair scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            enabled = false;
            return;
        }

        StartCoroutine(InitializeDelayed());
    }

    private IEnumerator InitializeDelayed()
    {
        yield return null;

        // Double-check we're in the right scene
        if (SceneManager.GetActiveScene().name != DRIVE_CHAIR_SCENE)
        {
            yield break;
        }

        if (modeController == null)
            modeController = FindFirstObjectByType<ModeController>();

        if (wheelchairState == null)
            wheelchairState = FindFirstObjectByType<WheelchairState>();

        _root = GetComponent<UIDocument>().rootVisualElement;

        if (wheelchairState != null)
        {
            wheelchairState.OnModeChanged += OnModeChanged;
            wheelchairState.OnActuatorSelected += OnActuatorSelected;
        }

        if (modeController != null)
        {
            modeController.OnActuatorSelected += OnActuatorConfigSelected;
        }

        CreateUI();
        UpdateVisibility();
    }

    void OnDisable()
    {
        if (wheelchairState != null)
        {
            wheelchairState.OnModeChanged -= OnModeChanged;
            wheelchairState.OnActuatorSelected -= OnActuatorSelected;
        }

        if (modeController != null)
        {
            modeController.OnActuatorSelected -= OnActuatorConfigSelected;
        }
    }

    private void CreateUI()
    {
        // Find the center area to place our controls
        var center = _root.Q<VisualElement>("Center");
        if (center == null)
        {
            Debug.LogError("[ActuatorControlUI] Center element not found");
            return;
        }

        // Create the control panel
        _controlPanel = new VisualElement { name = "ActuatorControlPanel" };
        _controlPanel.style.position = Position.Absolute;
        _controlPanel.style.bottom = 100;
        _controlPanel.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
        _controlPanel.style.translate = new StyleTranslate(new Translate(new Length(-50, LengthUnit.Percent), 0));
        _controlPanel.style.backgroundColor = new StyleColor(new Color(1, 1, 1, 0.95f));
        _controlPanel.style.borderTopLeftRadius = 16;
        _controlPanel.style.borderTopRightRadius = 16;
        _controlPanel.style.borderBottomLeftRadius = 16;
        _controlPanel.style.borderBottomRightRadius = 16;
        _controlPanel.style.paddingTop = 20;
        _controlPanel.style.paddingBottom = 20;
        _controlPanel.style.paddingLeft = 30;
        _controlPanel.style.paddingRight = 30;
        _controlPanel.style.display = DisplayStyle.None;

        // Actuator name label
        _actuatorLabel = new Label("Select an actuator");
        _actuatorLabel.style.fontSize = 18;
        _actuatorLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        _actuatorLabel.style.color = new StyleColor(Color.black);
        _actuatorLabel.style.marginBottom = 15;
        _actuatorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        _controlPanel.Add(_actuatorLabel);

        // Button row
        var buttonRow = new VisualElement();
        buttonRow.style.flexDirection = FlexDirection.Row;
        buttonRow.style.justifyContent = Justify.Center;
        buttonRow.style.alignItems = Align.Center;

        // Decrease button (large, easy to tap)
        _decreaseBtn = CreateControlButton("-", decreaseColor, OnDecreaseClicked);
        _decreaseBtn.RegisterCallback<PointerDownEvent>(e => StartHold("decrease"));
        _decreaseBtn.RegisterCallback<PointerUpEvent>(e => StopHold());
        _decreaseBtn.RegisterCallback<PointerLeaveEvent>(e => StopHold());
        buttonRow.Add(_decreaseBtn);

        // Spacer
        var spacer = new VisualElement();
        spacer.style.width = 40;
        buttonRow.Add(spacer);

        // Increase button
        _increaseBtn = CreateControlButton("+", increaseColor, OnIncreaseClicked);
        _increaseBtn.RegisterCallback<PointerDownEvent>(e => StartHold("increase"));
        _increaseBtn.RegisterCallback<PointerUpEvent>(e => StopHold());
        _increaseBtn.RegisterCallback<PointerLeaveEvent>(e => StopHold());
        buttonRow.Add(_increaseBtn);

        _controlPanel.Add(buttonRow);

        // Back to Drive button
        _backToDriveBtn = new Button(OnBackToDriveClicked);
        _backToDriveBtn.text = "Back to Drive Mode";
        _backToDriveBtn.style.marginTop = 20;
        _backToDriveBtn.style.height = 44;
        _backToDriveBtn.style.fontSize = 14;
        _backToDriveBtn.style.backgroundColor = new StyleColor(new Color(0.13f, 0.59f, 0.95f));
        _backToDriveBtn.style.color = new StyleColor(Color.white);
        _backToDriveBtn.style.borderTopLeftRadius = 8;
        _backToDriveBtn.style.borderTopRightRadius = 8;
        _backToDriveBtn.style.borderBottomLeftRadius = 8;
        _backToDriveBtn.style.borderBottomRightRadius = 8;
        _controlPanel.Add(_backToDriveBtn);

        center.Add(_controlPanel);
    }

    private Button CreateControlButton(string text, Color bgColor, System.Action onClick)
    {
        var btn = new Button(onClick);
        btn.text = text;
        btn.style.width = 80;
        btn.style.height = 80;
        btn.style.fontSize = 40;
        btn.style.unityFontStyleAndWeight = FontStyle.Bold;
        btn.style.backgroundColor = new StyleColor(bgColor);
        btn.style.color = new StyleColor(Color.white);
        btn.style.borderTopLeftRadius = 40;
        btn.style.borderTopRightRadius = 40;
        btn.style.borderBottomLeftRadius = 40;
        btn.style.borderBottomRightRadius = 40;
        btn.style.borderTopWidth = 0;
        btn.style.borderBottomWidth = 0;
        btn.style.borderLeftWidth = 0;
        btn.style.borderRightWidth = 0;
        return btn;
    }

    private void OnModeChanged(WheelchairState.WheelchairMode mode)
    {
        UpdateVisibility();
    }

    private void OnActuatorSelected(string actuatorId)
    {
        UpdateVisibility();
        UpdateActuatorLabel();
    }

    private void OnActuatorConfigSelected(SeatActuatorConfig actuator)
    {
        UpdateActuatorLabel();
    }

    private void UpdateVisibility()
    {
        if (_controlPanel == null || wheelchairState == null) return;

        bool show = wheelchairState.CurrentMode == WheelchairState.WheelchairMode.Seat
                    && !string.IsNullOrEmpty(wheelchairState.SelectedActuatorId);

        _controlPanel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void UpdateActuatorLabel()
    {
        if (_actuatorLabel == null || modeController == null) return;

        var actuator = modeController.GetSelectedActuator();
        if (actuator != null)
        {
            _actuatorLabel.text = actuator.label;
        }
        else
        {
            _actuatorLabel.text = "Select an actuator";
        }
    }

    private void OnIncreaseClicked()
    {
        modeController?.IncreaseSelectedActuator();
    }

    private void OnDecreaseClicked()
    {
        modeController?.DecreaseSelectedActuator();
    }

    private void OnBackToDriveClicked()
    {
        modeController?.SwitchToDriveMode();
    }

    private void StartHold(string action)
    {
        _isHolding = true;
        _holdStartTime = Time.time;
        _holdAction = action;
    }

    private void StopHold()
    {
        _isHolding = false;
        _holdAction = null;
    }

    void Update()
    {
        // Continuous actuation while holding (after initial 0.3s delay)
        if (_isHolding && Time.time - _holdStartTime > 0.5f)
        {
            if (Time.frameCount % 10 == 0) // Every 10 frames
            {
                if (_holdAction == "increase")
                    OnIncreaseClicked();
                else if (_holdAction == "decrease")
                    OnDecreaseClicked();
            }
        }
    }
}
