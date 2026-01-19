using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

#if ZXING_AVAILABLE
using ZXing;
#endif

/// <summary>
/// QR code scanner using webcam and ZXing.Net library.
///
/// Setup: In Unity Editor, go to Sensocto > Auth > Setup QR Scanner
/// This will automatically download and configure ZXing.Net.
/// </summary>
public class QRCodeScanner : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float scanInterval = 0.2f; // 5Hz scan rate
    [SerializeField] private int cameraWidth = 640;
    [SerializeField] private int cameraHeight = 480;

    [Header("Debug")]
    [SerializeField] private bool logScans = true;

    private WebCamTexture _webcam;
    private Coroutine _scanCoroutine;
    private VisualElement _displayElement;
    private bool _isScanning;
    private Color32[] _pixelBuffer;
    private byte[] _rawByteBuffer;
    private Texture2D _displayTexture;

#if ZXING_AVAILABLE
    private IBarcodeReader _reader;
#endif

    /// <summary>Fired when a QR code is successfully scanned.</summary>
    public event Action<string> OnQRCodeScanned;

    /// <summary>Fired when a scan error occurs.</summary>
    public event Action<string> OnScanError;

    /// <summary>Whether scanner is currently active.</summary>
    public bool IsScanning => _isScanning;

    /// <summary>Whether ZXing library is available.</summary>
    public static bool IsLibraryAvailable
    {
        get
        {
#if ZXING_AVAILABLE
            return true;
#else
            return false;
#endif
        }
    }

    void Awake()
    {
#if ZXING_AVAILABLE
        _reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = new[] { BarcodeFormat.QR_CODE }
            }
        };
#endif
    }

    void OnDestroy()
    {
        StopScanning();
        CleanupCamera();
    }

    /// <summary>
    /// Start scanning for QR codes.
    /// </summary>
    /// <param name="displayElement">Optional UI element to show camera feed.</param>
    public void StartScanning(VisualElement displayElement = null)
    {
        if (_isScanning)
        {
            Debug.LogWarning("[QRCodeScanner] Already scanning");
            return;
        }

#if !ZXING_AVAILABLE
        Debug.LogError("[QRCodeScanner] ZXing not installed. Use menu: Sensocto > Auth > Setup QR Scanner");
        OnScanError?.Invoke("QR scanner not installed. Use Sensocto > Auth > Setup QR Scanner in Unity Editor.");
        return;
#else
        _displayElement = displayElement;
        _isScanning = true;
        StartCoroutine(InitializeAndScan());
#endif
    }

    /// <summary>
    /// Stop scanning.
    /// </summary>
    public void StopScanning()
    {
        _isScanning = false;

        if (_scanCoroutine != null)
        {
            StopCoroutine(_scanCoroutine);
            _scanCoroutine = null;
        }

        if (_webcam != null && _webcam.isPlaying)
        {
            _webcam.Stop();
        }

        if (_displayElement != null)
        {
            _displayElement.style.backgroundImage = null;
        }

        if (logScans)
            Debug.Log("[QRCodeScanner] Stopped");
    }

#if ZXING_AVAILABLE
    private IEnumerator InitializeAndScan()
    {
        // Request camera permission
        yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            OnScanError?.Invoke("Camera permission denied");
            _isScanning = false;
            yield break;
        }

        // Check for cameras
        if (WebCamTexture.devices.Length == 0)
        {
            OnScanError?.Invoke("No camera found");
            _isScanning = false;
            yield break;
        }

        // Start camera
        var device = WebCamTexture.devices[0];
        _webcam = new WebCamTexture(device.name, cameraWidth, cameraHeight);
        _webcam.Play();

        // Wait for camera to initialize
        float timeout = 5f;
        while (!_webcam.didUpdateThisFrame && timeout > 0)
        {
            timeout -= 0.1f;
            yield return new WaitForSeconds(0.1f);
        }

        if (!_webcam.didUpdateThisFrame)
        {
            OnScanError?.Invoke("Camera failed to start");
            _isScanning = false;
            yield break;
        }

        // Allocate pixel buffer and byte buffer for ZXing
        int pixelCount = _webcam.width * _webcam.height;
        _pixelBuffer = new Color32[pixelCount];
        _rawByteBuffer = new byte[pixelCount * 4];

        // Create texture for UI display
        _displayTexture = new Texture2D(_webcam.width, _webcam.height, TextureFormat.RGBA32, false);

        if (logScans)
            Debug.Log($"[QRCodeScanner] Camera started: {device.name} ({_webcam.width}x{_webcam.height})");

        // Start scan loop
        _scanCoroutine = StartCoroutine(ScanLoop());
    }

    private IEnumerator ScanLoop()
    {
        while (_isScanning && _webcam != null)
        {
            if (_webcam.didUpdateThisFrame)
            {
                // Get pixels for both display and decode
                _webcam.GetPixels32(_pixelBuffer);

                // Update display texture
                if (_displayElement != null && _displayTexture != null)
                {
                    _displayTexture.SetPixels32(_pixelBuffer);
                    _displayTexture.Apply();
                    _displayElement.style.backgroundImage = Background.FromTexture2D(_displayTexture);
                }

                // Decode QR code
                try
                {
                    // Convert Color32[] to raw RGBA bytes for ZXing
                    for (int i = 0; i < _pixelBuffer.Length; i++)
                    {
                        int offset = i * 4;
                        _rawByteBuffer[offset] = _pixelBuffer[i].r;
                        _rawByteBuffer[offset + 1] = _pixelBuffer[i].g;
                        _rawByteBuffer[offset + 2] = _pixelBuffer[i].b;
                        _rawByteBuffer[offset + 3] = _pixelBuffer[i].a;
                    }

                    var result = _reader.Decode(_rawByteBuffer, _webcam.width, _webcam.height, RGBLuminanceSource.BitmapFormat.RGBA32);

                    if (result != null)
                    {
                        HandleResult(result.Text);
                    }
                }
                catch (Exception ex)
                {
                    if (logScans)
                        Debug.LogWarning($"[QRCodeScanner] Decode error: {ex.Message}");
                }
            }

            yield return new WaitForSeconds(scanInterval);
        }
    }

    private void HandleResult(string text)
    {
        if (logScans)
            Debug.Log($"[QRCodeScanner] Scanned: {text}");

        // Accept sensocto:// URLs, https:// URLs, or raw tokens
        OnQRCodeScanned?.Invoke(text);
    }
#endif

    private void CleanupCamera()
    {
        if (_webcam != null)
        {
            if (_webcam.isPlaying)
                _webcam.Stop();
            Destroy(_webcam);
            _webcam = null;
        }
        if (_displayTexture != null)
        {
            Destroy(_displayTexture);
            _displayTexture = null;
        }
        _pixelBuffer = null;
        _rawByteBuffer = null;
    }
}
