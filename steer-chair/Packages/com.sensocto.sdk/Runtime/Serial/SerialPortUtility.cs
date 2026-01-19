using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using System.IO.Ports;
#endif

namespace Sensocto.SDK
{
    /// <summary>
    /// Utility class for auto-detecting serial ports across platforms.
    /// Supports Windows, macOS, and Linux.
    /// </summary>
    public static class SerialPortUtility
    {
        /// <summary>
        /// Attempts to find an available serial port.
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
#if UNITY_EDITOR
            string[] ports = SerialPort.GetPortNames();

            foreach (string port in ports)
            {
                if (port.StartsWith("COM"))
                {
                    if (TryOpenPort(port))
                        return port;
                }
            }
#endif
            return "COM3"; // Fallback default
        }

        private static string GetMacOSPort()
        {
            Debug.Log($"[SerialPortUtility] Searching for macOS serial ports... (isEditor={Application.isEditor})");

#if UNITY_EDITOR
            // First, try .NET SerialPort.GetPortNames() - works better in Editor
            try
            {
                string[] systemPorts = SerialPort.GetPortNames();
                Debug.Log($"[SerialPortUtility] SerialPort.GetPortNames() returned {systemPorts.Length} ports: {string.Join(", ", systemPorts)}");

                foreach (string port in systemPorts)
                {
                    if (port.Contains("usbmodem") || port.Contains("usbserial") || port.Contains("wchusbserial"))
                    {
                        Debug.Log($"[SerialPortUtility] Found USB serial via GetPortNames: {port}");
                        return port;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SerialPortUtility] SerialPort.GetPortNames() failed: {e.Message}");
            }
#endif

            // Check if we have access to /dev directory (sandbox check)
            try
            {
                bool canAccessDev = Directory.Exists("/dev");
                Debug.Log($"[SerialPortUtility] Can access /dev: {canAccessDev}");

                if (!canAccessDev)
                {
                    Debug.LogError("[SerialPortUtility] Cannot access /dev - app may be sandboxed. See console for fix instructions.");
                    LogSandboxFixInstructions();
                    return "";
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[SerialPortUtility] Exception checking /dev access: {e.Message}");
                LogSandboxFixInstructions();
                return "";
            }

            // Fall back to directory scanning
            string[] patterns = new[]
            {
                "/dev/cu.usbmodem*",
                "/dev/cu.usbserial*",
                "/dev/cu.wchusbserial*",
                "/dev/cu.SLAB_USBtoUART",
            };

            foreach (string pattern in patterns)
            {
                string[] matchingPorts = GlobPorts(pattern);
                Debug.Log($"[SerialPortUtility] Pattern '{pattern}' found {matchingPorts.Length} ports: {string.Join(", ", matchingPorts)}");

                if (matchingPorts.Length > 0)
                {
                    string selectedPort = matchingPorts[0];
                    Debug.Log($"[SerialPortUtility] Selected port: {selectedPort}");
                    return selectedPort;
                }
            }

            Debug.LogWarning("[SerialPortUtility] No usbmodem/usbserial device found in /dev/");
            return "";
        }

        private static void LogSandboxFixInstructions()
        {
            Debug.LogError(@"[SerialPortUtility] ===== SANDBOX FIX INSTRUCTIONS =====
To allow serial port access in macOS builds, you need to disable App Sandbox:

Option 1 - Disable sandbox via terminal after build:
  codesign --remove-signature ""YourApp.app""

Option 2 - Add entitlements file with:
  <key>com.apple.security.app-sandbox</key>
  <false/>

Option 3 - Use a post-build script to auto-codesign
=================================================");
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
                string searchPattern = filePattern.Replace("*", "");

                string[] files = Directory.GetFiles(directory);
                var matches = new List<string>();

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
#if UNITY_EDITOR
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
#else
            // In builds, we can't use SerialPort, so just check if file exists
            return File.Exists(portName);
#endif
        }

        /// <summary>
        /// Lists all available serial ports on the system.
        /// </summary>
        public static string[] ListAllPorts()
        {
            var allPorts = new List<string>();

#if UNITY_EDITOR
            // .NET provided ports (works best on Windows)
            try
            {
                allPorts.AddRange(SerialPort.GetPortNames());
            }
            catch { }
#endif

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
}
