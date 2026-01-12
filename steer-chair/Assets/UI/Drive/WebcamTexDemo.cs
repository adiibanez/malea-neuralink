using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

#nullable enable

[RequireComponent(typeof(UIDocument))]
public class WebcamTexDemo : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private string? preferredCameraName = "See3CAM_CU31";
    [SerializeField] private int requestedWidth = 1280;
    [SerializeField] private int requestedHeight = 720;
    [SerializeField] private int requestedFPS = 30;

    [Header("Debug")]
    [SerializeField] private bool logCameraChanges = true;

    private WebCamTexture? _webCamTexture;
    private Image? _uiImage;
    private DropdownField? _cameraDropdown;
    private Button? _refreshBtn;
    private List<WebCamDevice> _availableDevices = new List<WebCamDevice>();
    private int _selectedIndex = -1;

    /// <summary>
    /// Currently active camera name.
    /// </summary>
    public string? ActiveCameraName => _webCamTexture?.deviceName;

    /// <summary>
    /// List of available camera names.
    /// </summary>
    public IReadOnlyList<string> AvailableCameras => _availableDevices.Select(d => d.name).ToList();

    void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        // Get UI elements
        _uiImage = root.Q<Image>(DriveUIElementNames.CameraImage);
        _cameraDropdown = root.Q<DropdownField>(DriveUIElementNames.CameraDropdown);
        _refreshBtn = root.Q<Button>(DriveUIElementNames.RefreshCamerasBtn);

        // Setup dropdown
        if (_cameraDropdown != null)
        {
            _cameraDropdown.RegisterValueChangedCallback(OnCameraSelected);
        }

        // Setup refresh button
        if (_refreshBtn != null)
        {
            _refreshBtn.clicked += RefreshCameraList;
        }

        // Initial camera list population
        RefreshCameraList();

        // Try to select preferred camera or first available
        SelectInitialCamera();
    }

    void OnDisable()
    {
        StopCamera();

        if (_cameraDropdown != null)
        {
            _cameraDropdown.UnregisterValueChangedCallback(OnCameraSelected);
        }

        if (_refreshBtn != null)
        {
            _refreshBtn.clicked -= RefreshCameraList;
        }
    }

    private void RefreshCameraList()
    {
        _availableDevices = WebCamTexture.devices.ToList();

        if (_cameraDropdown != null)
        {
            var cameraNames = _availableDevices.Select(d => d.name).ToList();

            if (cameraNames.Count == 0)
            {
                cameraNames.Add("No cameras found");
            }

            _cameraDropdown.choices = cameraNames;

            // Preserve selection if possible
            if (_selectedIndex >= 0 && _selectedIndex < cameraNames.Count)
            {
                _cameraDropdown.index = _selectedIndex;
            }
            else if (cameraNames.Count > 0)
            {
                _cameraDropdown.index = 0;
            }
        }

        if (logCameraChanges)
        {
            Debug.Log($"[WebcamTexDemo] Found {_availableDevices.Count} cameras: {string.Join(", ", _availableDevices.Select(d => d.name))}");
        }
    }

    private void SelectInitialCamera()
    {
        if (_availableDevices.Count == 0)
        {
            Debug.LogWarning("[WebcamTexDemo] No cameras available");
            return;
        }

        int indexToSelect = 0;

        // Try to find preferred camera
        if (!string.IsNullOrEmpty(preferredCameraName))
        {
            var preferredIndex = _availableDevices.FindIndex(d => d.name == preferredCameraName);
            if (preferredIndex >= 0)
            {
                indexToSelect = preferredIndex;
            }
        }

        // Select and start camera
        if (_cameraDropdown != null)
        {
            _cameraDropdown.index = indexToSelect;
        }

        StartCamera(_availableDevices[indexToSelect].name);
    }

    private void OnCameraSelected(ChangeEvent<string> evt)
    {
        if (_cameraDropdown == null) return;

        _selectedIndex = _cameraDropdown.index;

        if (_selectedIndex < 0 || _selectedIndex >= _availableDevices.Count)
            return;

        var selectedDevice = _availableDevices[_selectedIndex];
        StartCamera(selectedDevice.name);
    }

    private void StartCamera(string deviceName)
    {
        // Stop existing camera
        StopCamera();

        if (logCameraChanges)
        {
            Debug.Log($"[WebcamTexDemo] Starting camera: {deviceName}");
        }

        // Create and start new webcam texture
        _webCamTexture = new WebCamTexture(deviceName, requestedWidth, requestedHeight, requestedFPS);
        _webCamTexture.Play();

        // Assign to UI image
        if (_uiImage != null)
        {
            _uiImage.image = _webCamTexture;
        }

        // Save as preferred for next time
        preferredCameraName = deviceName;
    }

    private void StopCamera()
    {
        if (_webCamTexture != null)
        {
            if (_webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }
            _webCamTexture = null;
        }

        if (_uiImage != null)
        {
            _uiImage.image = null;
        }
    }

    /// <summary>
    /// Programmatically select a camera by name.
    /// </summary>
    public void SelectCamera(string cameraName)
    {
        var index = _availableDevices.FindIndex(d => d.name == cameraName);
        if (index >= 0)
        {
            _selectedIndex = index;
            if (_cameraDropdown != null)
            {
                _cameraDropdown.index = index;
            }
            StartCamera(cameraName);
        }
        else
        {
            Debug.LogWarning($"[WebcamTexDemo] Camera not found: {cameraName}");
        }
    }

    /// <summary>
    /// Programmatically select a camera by index.
    /// </summary>
    public void SelectCamera(int index)
    {
        if (index >= 0 && index < _availableDevices.Count)
        {
            SelectCamera(_availableDevices[index].name);
        }
    }
}
