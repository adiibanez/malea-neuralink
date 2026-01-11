using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using System;
using Sensocto;

#nullable enable

[RequireComponent(typeof(UIDocument))]
public class DriveEvents : MonoBehaviour
{
    private static readonly List<string> _driveOperationUIElements = new List<string>
    {
        DriveUIElementNames.ReadyBtn,
        DriveUIElementNames.StartBtn,
        DriveUIElementNames.StopBtn,
        DriveUIElementNames.FasterBtn,
        DriveUIElementNames.SlowerBtn,
        DriveUIElementNames.MouseJoystick,
    };

    public enum DriveState
    {
        Stopped,
        Ready,
        Driving
    }

    public enum InputSource
    {
        MouseJoystick,
        Sensocto,
        Both
    }

    // This should be improved and encapsulated
    public const float ReadyTimeout = 3f;
    public const float DriveTimeout = 1.5f;
    private DriveState _currentDriveState = DriveState.Stopped;
    private float _lastStateChangedAt;

    [Header("Input Source")]
    [SerializeField] private InputSource inputSource = InputSource.Both;
    [SerializeField] private SensoctoSensorProvider? sensoctoProvider;

    private Dictionary<string, Button> _fullButtonList;
    private List<Button> _driveOperationButtons;
    private HoverActivateButton _stopBtn;
    private MouseJoystick _mouseJoystick;

    private IMoveReceiver[] _joystickTargets;

    public void OnEnable()
    {
        _currentDriveState = DriveState.Stopped;
        _lastStateChangedAt = Time.time;

        _joystickTargets = GetComponents<MonoBehaviour>()
            .Select(m => m as IMoveReceiver)
            .Where(m => m != null)
            .Cast<IMoveReceiver>()
            .ToArray();

        // Subscribe to Sensocto input if available
        if (sensoctoProvider != null && (inputSource == InputSource.Sensocto || inputSource == InputSource.Both))
        {
            sensoctoProvider.OnMovementReceived.AddListener(OnSensoctoMovement);
        }

        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        // Get All Buttons
        _fullButtonList ??= root
            .Query<Button>()
            .ToList()
            .ToDictionary(b => b.name);

        // Get JoystickView
        _mouseJoystick = root.Q<MouseJoystick>(DriveUIElementNames.MouseJoystick);
        _mouseJoystick.RegisterCallback<JoystickMoveEvent>(OnJoystickMoved);

        // Classify Buttons and setup Callbacks
        _driveOperationButtons = _fullButtonList
            .Where(b => !_driveOperationUIElements.Contains(b.Key))
            .Select(b => b.Value)
            .ToList();

        foreach (var b in _fullButtonList.Values)
        {
            switch (b.name)
            {
                case DriveUIElementNames.ReadyBtn:
                    b.RegisterCallback<ClickEvent>(OnDriveReady);
                    break;
                case DriveUIElementNames.StartBtn:
                    b.RegisterCallback<ClickEvent>(OnDriveStart);
                    break;
                case DriveUIElementNames.StopBtn:
                    _stopBtn ??= new HoverActivateButton(b);
                    b.RegisterCallback<ClickEvent>(OnDriveStop);
                    break;
                case DriveUIElementNames.QuitBtn:
                    b.RegisterCallback<ClickEvent>(OnQuit);
                    break;
                default:
                    b.RegisterCallback<ClickEvent>(OnButtonClicked);
                    break;
            }
        }
    }

    public void OnDisable()
    {
        OnDriveStop(null);
        _mouseJoystick.UnregisterCallback<JoystickMoveEvent>(OnJoystickMoved);

        // Unsubscribe from Sensocto input
        if (sensoctoProvider != null)
        {
            sensoctoProvider.OnMovementReceived.RemoveListener(OnSensoctoMovement);
        }

        foreach (var b in _fullButtonList.Values)
        {
            switch (b.name)
            {
                case DriveUIElementNames.ReadyBtn:
                    b.UnregisterCallback<ClickEvent>(OnDriveReady);
                    break;
                case DriveUIElementNames.StartBtn:
                    b.UnregisterCallback<ClickEvent>(OnDriveStart);
                    break;
                case DriveUIElementNames.StopBtn:
                    b.UnregisterCallback<ClickEvent>(OnDriveStop);
                    break;
                case DriveUIElementNames.QuitBtn:
                    b.UnregisterCallback<ClickEvent>(OnQuit);
                    break;
                default:
                    b.UnregisterCallback<ClickEvent>(OnButtonClicked);
                    break;
            }
        }
    }

    public void OnApplicationFocus(bool hasFocus)
    {
        OnDriveStop(null);
    }

    public void Update()
    {
        switch (_currentDriveState)
        {
            case DriveState.Stopped:
                // Passive, we wait for instructions
                break;
            case DriveState.Ready:
                // Timed Cancellation
                if(_lastStateChangedAt + ReadyTimeout < Time.time)
                {
                    OnDriveStop(null);
                    return;
                }
                break;
            case DriveState.Driving:
                // Any mouse click Terminates the DriveMode
                if (Mouse.current.leftButton.wasPressedThisFrame
                    || Mouse.current.rightButton.wasPressedThisFrame
                    || Mouse.current.middleButton.wasPressedThisFrame)
                {
                    OnDriveStop(null);
                    return;
                }
                // Timed Cancellation
                if(_lastStateChangedAt + DriveTimeout < Time.time)
                {
                    OnDriveStop(null);
                    return;
                }
                break;
        }
        if (_currentDriveState == DriveState.Driving &&
            (Mouse.current.leftButton.wasPressedThisFrame
            || Mouse.current.rightButton.wasPressedThisFrame
            || Mouse.current.middleButton.wasPressedThisFrame))
        {
            OnDriveStop(null);
        }
    }

    private void OnDriveReady(ClickEvent evnt)
    {
        // Enable Start Button
        _fullButtonList[DriveUIElementNames.ReadyBtn].SetEnabled(false);
        _fullButtonList[DriveUIElementNames.StopBtn].SetEnabled(true);
        _fullButtonList[DriveUIElementNames.StartBtn].SetEnabled(true);

        // Disable all Non Drive UI Buttons
        foreach (var b in _driveOperationButtons)
        {
            b.SetEnabled(false);
        }

        _currentDriveState = DriveState.Ready;
        _lastStateChangedAt = Time.time;
    }

    private void OnDriveStart(ClickEvent evnt)
    {
        // Set Enabled State for Stop and Mouse Joystick
        _fullButtonList[DriveUIElementNames.StartBtn].SetEnabled(false);
        _fullButtonList[DriveUIElementNames.StopBtn].SetEnabled(true);
        _mouseJoystick.SetEnabled(true);

        _currentDriveState = DriveState.Driving;
        _lastStateChangedAt = Time.time;
    }

    private void OnDriveStop(ClickEvent? evnt)
    {
        // Set Disabled State for Drive UI Elements except Ready
        _fullButtonList[DriveUIElementNames.ReadyBtn].SetEnabled(true);
        _fullButtonList[DriveUIElementNames.StartBtn].SetEnabled(false);
        _fullButtonList[DriveUIElementNames.StopBtn].SetEnabled(false);
        _mouseJoystick.SetEnabled(false);
        _mouseJoystick.MarkDirtyRepaint();

        // Enable all Non Drive UI Buttons
        foreach (var b in _driveOperationButtons)
        {
            b.SetEnabled(true);
        }

        _currentDriveState = DriveState.Stopped;
        _lastStateChangedAt = Time.time;

        foreach(var t in _joystickTargets)
        {
            t.Move(Vector2.zero);
        }
    }

    private void OnJoystickMoved(JoystickMoveEvent evnt)
    {
        // Skip mouse joystick input if only using Sensocto
        if (inputSource == InputSource.Sensocto)
            return;

        Debug.Log("OnJoystickMoved" + evnt.Direction.ToString());
        BroadcastMovement(evnt.Direction);
    }

    private void OnSensoctoMovement(Vector2 direction)
    {
        // Skip Sensocto input if only using MouseJoystick
        if (inputSource == InputSource.MouseJoystick)
            return;

        Debug.Log("OnSensoctoMovement" + direction.ToString());
        BroadcastMovement(direction);
    }

    private void BroadcastMovement(Vector2 direction)
    {
        _lastStateChangedAt = Time.time;

        foreach(var t in _joystickTargets)
        {
            t.Move(direction);
        }
    }

    private void OnQuit(ClickEvent evnt)
    {
        OnDriveStop(null);
        SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
    }

    private void OnButtonClicked(ClickEvent evnt)
    {
        Debug.Log("OnButtonClicked" + evnt.ToString());
    }
}
