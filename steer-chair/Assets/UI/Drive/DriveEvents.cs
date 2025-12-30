using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(UIDocument))]
public class DriveEvents : MonoBehaviour
{
    private Dictionary<string, Button> _buttonList;

    private HoverActivateButton _stopBtn;

    private MouseJoystick _mouseJoystick;

    public void OnEnable()
    {
        VisualElement root = GetComponent<UIDocument>().rootVisualElement;

        // Listen To Buttons
        _buttonList ??= root
            .Query<Button>()
            .ToList()
            .ToDictionary(b => b.name);

        foreach (var b in _buttonList.Values)
        {
            switch (b.name)
            {
                case "ReadyBtn":
                b.RegisterCallback<ClickEvent>(OnDriveReady);
                break;
                case "StartBtn":
                b.RegisterCallback<ClickEvent>(OnDriveStart);
                break;
                case "StopBtn":
                _stopBtn ??= new HoverActivateButton(b);
                b.RegisterCallback<ClickEvent>(OnDriveStop);
                break;
                
                case "QuitBtn":
                b.RegisterCallback<ClickEvent>(OnQuit);
                break;
            }
        }

        // Get JoystickView
        _mouseJoystick = root.Q<MouseJoystick>("MouseJoystick");
        _mouseJoystick.RegisterCallback<JoystickMoveEvent>(OnJoystickMoved);
    }

    public void OnDisable()
    {
        foreach (var b in _buttonList.Values)
        {
            switch (b.name)
            {
                case "ReadyBtn":
                b.UnregisterCallback<ClickEvent>(OnDriveReady);
                break;
                case "StartBtn":
                b.UnregisterCallback<ClickEvent>(OnDriveStart);
                break;
                case "StopBtn":
                b.UnregisterCallback<ClickEvent>(OnDriveStop);
                break;
                case "QuitBtn":
                b.UnregisterCallback<ClickEvent>(OnQuit);
                break;
            }
        }

        _mouseJoystick.UnregisterCallback<JoystickMoveEvent>(OnJoystickMoved);
    }

    private void OnJoystickMoved(JoystickMoveEvent evnt)
    {
        Debug.Log("OnJoystickMoved" + evnt.Direction.ToString());
    }

    private void OnDriveReady(ClickEvent evnt)
    {
        _buttonList["ReadyBtn"].SetEnabled(false);
        _buttonList["StartBtn"].SetEnabled(true);
    }

    private void OnDriveStart(ClickEvent evnt)
    {
        _buttonList["StartBtn"].SetEnabled(false);
        _buttonList["StopBtn"].SetEnabled(true);

        _mouseJoystick.SetEnabled(true);
    }

    private void OnDriveStop(ClickEvent evnt)
    {
        _buttonList["ReadyBtn"].SetEnabled(true);
        _buttonList["StartBtn"].SetEnabled(false);
        _buttonList["StopBtn"].SetEnabled(false);

        _mouseJoystick.SetEnabled(false);
    }

    private void OnQuit(ClickEvent evnt)
    {
        SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
    }

    private void OnButtonClicked(ClickEvent evnt)
    {
        Debug.Log("OnButtonClicked" + evnt.ToString());
    }
}
