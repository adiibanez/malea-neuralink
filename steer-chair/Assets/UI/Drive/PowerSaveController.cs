using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
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

    private const string LowFpsPrefKey = "PowerSave_LowFps";
    private const int SimulatorFps = 60;
    private const int DriveFps = 30;
    private const int LowFps = 5;

    private bool IsSimulator => SceneManager.GetActiveScene().name == "Simulator";

    void OnEnable()
    {
        _webcam = GetComponent<WebcamTexDemo>();

        // Simulator always runs full speed, no persistence needed
        if (IsSimulator)
        {
            _lowFps = false;
            Application.targetFrameRate = SimulatorFps;
        }
        else
        {
            // Restore persisted setting; default to low FPS for driving
            _lowFps = PlayerPrefs.GetInt(LowFpsPrefKey, 1) == 1;
            Application.targetFrameRate = _lowFps ? LowFps : DriveFps;
        }

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

        _cameraPaused = false;
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

        if (IsSimulator)
        {
            Application.targetFrameRate = SimulatorFps;
        }
        else
        {
            Application.targetFrameRate = _lowFps ? LowFps : DriveFps;
            PlayerPrefs.SetInt(LowFpsPrefKey, _lowFps ? 1 : 0);
            PlayerPrefs.Save();
        }

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
