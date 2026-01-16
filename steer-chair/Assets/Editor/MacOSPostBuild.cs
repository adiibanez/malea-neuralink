using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using System.Diagnostics;
using System.IO;

/// <summary>
/// Post-build processor that removes code signature from macOS builds to disable App Sandbox.
/// This allows the app to access serial ports in /dev/.
/// </summary>
public class MacOSPostBuild : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneOSX)
            return;

        string appPath = report.summary.outputPath;

        // Handle both .app bundles and direct executable paths
        if (!appPath.EndsWith(".app"))
        {
            // Unity might output the executable path, find the .app bundle
            string dir = Path.GetDirectoryName(appPath);
            string[] apps = Directory.GetFiles(dir, "*.app", SearchOption.TopDirectoryOnly);
            if (apps.Length == 0)
            {
                apps = Directory.GetDirectories(dir, "*.app", SearchOption.TopDirectoryOnly);
            }
            if (apps.Length > 0)
            {
                appPath = apps[0];
            }
        }

        if (!Directory.Exists(appPath) && !File.Exists(appPath))
        {
            UnityEngine.Debug.LogWarning($"[MacOSPostBuild] Could not find app bundle at: {appPath}");
            return;
        }

        UnityEngine.Debug.Log($"[MacOSPostBuild] Removing code signature from: {appPath}");

        try
        {
            // Clear quarantine attributes first
            var xattrProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/xattr",
                    Arguments = $"-cr \"{appPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            xattrProcess.Start();
            xattrProcess.WaitForExit();
            UnityEngine.Debug.Log("[MacOSPostBuild] Cleared extended attributes");

            // Use ad-hoc signing (allows app to run while being unsigned for distribution)
            // This is better than removing signature completely which breaks app launch
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/codesign",
                    Arguments = $"--force --deep --sign - \"{appPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                UnityEngine.Debug.Log("[MacOSPostBuild] Successfully ad-hoc signed app - serial port access enabled!");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[MacOSPostBuild] codesign returned {process.ExitCode}: {error}");
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[MacOSPostBuild] Failed to sign app: {e.Message}");
        }
    }
}
