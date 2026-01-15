using System;
using System.IO;
using System.IO.Ports;
using UnityEngine;

/// <summary>
/// Utility class for auto-detecting serial ports across platforms.
/// </summary>
public static class SerialPortUtility
{
    /// <summary>
    /// Attempts to find an available serial port for the joystick controller.
    /// </summary>
    /// <returns>The detected port name, or a platform-specific default.</returns>
    public static string GetSerialPort()
    {
        switch (Application.platform)
        {
            case RuntimePlatform.WindowsPlayer:
            case RuntimePlatform.WindowsEditor:
                return GetWindowsPort();
                
            case RuntimePlatform.OSXPlayer:
            case RuntimePlatform.OSXEditor:
                return GetMacOSPort();
                
            case RuntimePlatform.LinuxPlayer:
            case RuntimePlatform.LinuxEditor:
                return GetLinuxPort();
                
            default:
                Debug.LogWarning($"[SerialPortUtility] Unsupported platform: {Application.platform}");
                return "";
        }
    }
    
    private static string GetWindowsPort()
    {
        string[] ports = SerialPort.GetPortNames();
        
        foreach (string port in ports)
        {
            if (port.StartsWith("COM"))
            {
                if (TryOpenPort(port))
                    return port;
            }
        }
        
        return "COM3"; // Fallback default
    }
    
    private static string GetMacOSPort()
    {
        string[] patterns = new[]
        {
            "/dev/cu.usbmodem*",
            // "/dev/cu.SLAB_USBtoUART",
            // "/dev/cu.usbserial*",
            // "/dev/cu.wchusbserial*",
        };

        Debug.Log("[SerialPortUtility] Searching for macOS serial ports...");

        foreach (string pattern in patterns)
        {
            string[] matchingPorts = GlobPorts(pattern);
            Debug.Log($"[SerialPortUtility] Pattern '{pattern}' found {matchingPorts.Length} ports: {string.Join(", ", matchingPorts)}");

            foreach (string port in matchingPorts)
            {
                Debug.Log($"[SerialPortUtility] Trying port: {port}");
                if (TryOpenPort(port))
                {
                    Debug.Log($"[SerialPortUtility] Successfully opened port: {port}");
                    return port;
                }
                else
                {
                    Debug.Log($"[SerialPortUtility] Failed to open port: {port}");
                }
            }
        }

        Debug.LogWarning("[SerialPortUtility] No working port found, using fallback");
        return "/dev/cu.usbmodem11101"; // Fallback
    }
    
    private static string GetLinuxPort()
    {
        string[] patterns = new[]
        {
            "/dev/ttyUSB*",
            "/dev/ttyACM*",
        };
        
        foreach (string pattern in patterns)
        {
            string[] matchingPorts = GlobPorts(pattern);
            foreach (string port in matchingPorts)
            {
                if (TryOpenPort(port))
                    return port;
            }
        }
        
        return "/dev/ttyACM0"; // Fallback
    }
    
    /// <summary>
    /// Simple glob implementation for Unix-like paths.
    /// </summary>
    private static string[] GlobPorts(string pattern)
    {
        try
        {
            string directory = Path.GetDirectoryName(pattern);
            string filePattern = Path.GetFileName(pattern);
            
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();
            
            // Convert glob pattern to search pattern
            // Unity/Mono doesn't have full glob support, so we do simple wildcard
            string searchPattern = filePattern.Replace("*", "");
            
            string[] files = Directory.GetFiles(directory);
            var matches = new System.Collections.Generic.List<string>();
            
            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                // Simple prefix match for patterns like "usbmodem*"
                if (fileName.StartsWith(searchPattern) || 
                    (filePattern.StartsWith("*") && fileName.Contains(searchPattern)))
                {
                    matches.Add(file);
                }
            }
            
            // Sort for consistent ordering
            matches.Sort();
            return matches.ToArray();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SerialPortUtility] Error globbing {pattern}: {e.Message}");
            return Array.Empty<string>();
        }
    }
    
    /// <summary>
    /// Attempts to open a serial port to verify it's available.
    /// </summary>
    private static bool TryOpenPort(string portName)
    {
        try
        {
            using (var port = new SerialPort(portName, 115200) { ReadTimeout = 1000 })
            {
                port.Open();
                port.Close();
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.Log($"[SerialPortUtility] TryOpenPort({portName}) failed: {e.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Lists all available serial ports on the system.
    /// </summary>
    public static string[] ListAllPorts()
    {
        var allPorts = new System.Collections.Generic.List<string>();
        
        // .NET provided ports (works best on Windows)
        try
        {
            allPorts.AddRange(SerialPort.GetPortNames());
        }
        catch { }
        
        // On macOS/Linux, also check /dev directly
        if (Application.platform == RuntimePlatform.OSXPlayer ||
            Application.platform == RuntimePlatform.OSXEditor)
        {
            allPorts.AddRange(GlobPorts("/dev/cu.*"));
            allPorts.AddRange(GlobPorts("/dev/tty.usb*"));
        }
        else if (Application.platform == RuntimePlatform.LinuxPlayer ||
                 Application.platform == RuntimePlatform.LinuxEditor)
        {
            allPorts.AddRange(GlobPorts("/dev/ttyUSB*"));
            allPorts.AddRange(GlobPorts("/dev/ttyACM*"));
        }
        
        return allPorts.ToArray();
    }
}