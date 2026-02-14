using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

#nullable enable

[RequireComponent(typeof(UIDocument))]
public class PowerSaveController : MonoBehaviour
{
    private Button? _cameraToggleBtn;
    private Button? _fpsToggleBtn;
    private WebcamTexDemo? _webcam;

    private bool _cameraPaused;
    private bool _lowFps;

    private const int NormalFps = 30;
    private const int LowFps = 5;

    void OnEnable()
    {
        _webcam = GetComponent<WebcamTexDemo>();
        StartCoroutine(InitUI());
    }

    private IEnumerator InitUI()
    {
        yield return null;

        var root = GetComponent<UIDocument>().rootVisualElement;

        _cameraToggleBtn = root.Q<Button>(DriveUIElementNames.CameraToggleBtn);
        _fpsToggleBtn = root.Q<Button>(DriveUIElementNames.FpsToggleBtn);

        if (_cameraToggleBtn != null)
            _cameraToggleBtn.clicked += OnCameraToggle;

        if (_fpsToggleBtn != null)
            _fpsToggleBtn.clicked += OnFpsToggle;

        // Sync initial state
        _cameraPaused = false;
        _lowFps = false;
        UpdateButtonLabels();
    }

    void OnDisable()
    {
        if (_cameraToggleBtn != null)
            _cameraToggleBtn.clicked -= OnCameraToggle;

        if (_fpsToggleBtn != null)
            _fpsToggleBtn.clicked -= OnFpsToggle;
    }

    private void OnCameraToggle()
    {
        _cameraPaused = !_cameraPaused;

        if (_webcam != null)
        {
            if (_cameraPaused)
                _webcam.PauseCamera();
            else
                _webcam.ResumeCamera();
        }

        UpdateButtonLabels();
    }

    private void OnFpsToggle()
    {
        _lowFps = !_lowFps;
        Application.targetFrameRate = _lowFps ? LowFps : NormalFps;
        UpdateButtonLabels();
    }

    private void UpdateButtonLabels()
    {
        if (_cameraToggleBtn != null)
        {
            _cameraToggleBtn.text = _cameraPaused ? "Cam On" : "Cam Off";
            _cameraToggleBtn.EnableInClassList("powersave-active", _cameraPaused);
        }

        if (_fpsToggleBtn != null)
        {
            _fpsToggleBtn.text = _lowFps ? "Normal FPS" : "Low FPS";
            _fpsToggleBtn.EnableInClassList("powersave-active", _lowFps);
        }
    }
}
