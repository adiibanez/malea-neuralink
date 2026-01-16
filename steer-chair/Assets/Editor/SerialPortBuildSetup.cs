using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Ensures proper build settings for serial port support in IL2CPP builds.
/// </summary>
public class SerialPortBuildSetup : IPreprocessBuildWithReport
{
    public int callbackOrder => -100; // Run early

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.StandaloneOSX ||
            report.summary.platform == BuildTarget.StandaloneWindows ||
            report.summary.platform == BuildTarget.StandaloneWindows64)
        {
            // Log current settings for debugging
            var group = BuildPipeline.GetBuildTargetGroup(report.summary.platform);
            var strippingLevel = PlayerSettings.GetManagedStrippingLevel(group);
            var apiLevel = PlayerSettings.GetApiCompatibilityLevel(group);

            Debug.Log($"[SerialPortBuildSetup] Platform: {report.summary.platform}");
            Debug.Log($"[SerialPortBuildSetup] API Compatibility: {apiLevel}");
            Debug.Log($"[SerialPortBuildSetup] Managed Stripping: {strippingLevel}");

            // Warn if stripping is too aggressive
            if (strippingLevel == ManagedStrippingLevel.High)
            {
                Debug.LogWarning("[SerialPortBuildSetup] High stripping level may break SerialPort. Consider using 'Minimal' or 'Low'.");
            }
        }
    }
}
