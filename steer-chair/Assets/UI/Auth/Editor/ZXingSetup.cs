#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using System.Linq;

namespace Auth.Editor
{
    /// <summary>
    /// One-click setup for ZXing.Net library.
    /// Downloads the Unity-compatible DLL from GitHub releases.
    /// </summary>
    public static class ZXingSetup
    {
        private const string ZXING_VERSION = "0.16.11.0";
        private const string DOWNLOAD_URL = "https://github.com/micjahn/ZXing.Net/releases/download/v" + ZXING_VERSION + "/ZXing.Net." + ZXING_VERSION + ".zip";
        private const string PLUGIN_PATH = "Assets/Plugins/ZXing";
        private const string DLL_NAME = "zxing.dll";
        private const string DEFINE_SYMBOL = "ZXING_AVAILABLE";

        [MenuItem("Sensocto/Auth/Setup QR Scanner (Download ZXing)")]
        public static void SetupZXing()
        {
            if (IsZXingInstalled())
            {
                EditorUtility.DisplayDialog("ZXing Already Installed",
                    "ZXing.Net is already installed and ready to use.\n\n" +
                    "Location: " + Path.Combine(PLUGIN_PATH, DLL_NAME),
                    "OK");
                return;
            }

            if (EditorUtility.DisplayDialog("Download ZXing.Net",
                "This will download ZXing.Net (" + ZXING_VERSION + ") for QR code scanning.\n\n" +
                "The library will be installed to:\n" + PLUGIN_PATH + "\n\n" +
                "Download size: ~500KB",
                "Download", "Cancel"))
            {
                DownloadAndInstall();
            }
        }

        [MenuItem("Sensocto/Auth/Check QR Scanner Status")]
        public static void CheckStatus()
        {
            bool installed = IsZXingInstalled();
            bool defined = HasDefineSymbol();

            string message = installed
                ? "ZXing.Net is installed and ready!\n\nLocation: " + Path.Combine(PLUGIN_PATH, DLL_NAME)
                : "ZXing.Net is NOT installed.\n\nUse 'Sensocto > Auth > Setup QR Scanner' to install.";

            message += "\n\nDefine symbol: " + (defined ? "ZXING_AVAILABLE (set)" : "Not set");

            EditorUtility.DisplayDialog("QR Scanner Status", message, "OK");
        }

        private static void DownloadAndInstall()
        {
            EditorUtility.DisplayProgressBar("Downloading ZXing.Net", "Starting download...", 0);

            try
            {
                // Create plugin directory
                if (!Directory.Exists(PLUGIN_PATH))
                {
                    Directory.CreateDirectory(PLUGIN_PATH);
                }

                // Download using UnityWebRequest
                var tempZipPath = Path.Combine(Application.temporaryCachePath, "zxing.zip");

                EditorUtility.DisplayProgressBar("Downloading ZXing.Net", "Downloading from GitHub...", 0.2f);

                using (var www = UnityWebRequest.Get(DOWNLOAD_URL))
                {
                    var operation = www.SendWebRequest();

                    while (!operation.isDone)
                    {
                        EditorUtility.DisplayProgressBar("Downloading ZXing.Net",
                            $"Downloading... {(www.downloadProgress * 100):F0}%",
                            0.2f + www.downloadProgress * 0.5f);
                        System.Threading.Thread.Sleep(100);
                    }

                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        throw new System.Exception($"Download failed: {www.error}");
                    }

                    File.WriteAllBytes(tempZipPath, www.downloadHandler.data);
                }

                EditorUtility.DisplayProgressBar("Downloading ZXing.Net", "Extracting...", 0.8f);

                // Extract the Unity DLL from the zip
                ExtractUnityDll(tempZipPath);

                // Cleanup
                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);

                EditorUtility.DisplayProgressBar("Downloading ZXing.Net", "Configuring...", 0.9f);

                // Add define symbol
                AddDefineSymbol();

                // Refresh asset database
                AssetDatabase.Refresh();

                EditorUtility.ClearProgressBar();

                EditorUtility.DisplayDialog("ZXing.Net Installed",
                    "ZXing.Net has been installed successfully!\n\n" +
                    "QR code scanning is now enabled.",
                    "OK");

                Debug.Log("[ZXingSetup] ZXing.Net installed successfully to " + PLUGIN_PATH);
            }
            catch (System.Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Installation Failed",
                    "Failed to install ZXing.Net:\n\n" + ex.Message + "\n\n" +
                    "You can manually download from:\n" + DOWNLOAD_URL,
                    "OK");
                Debug.LogError("[ZXingSetup] Installation failed: " + ex.Message);
            }
        }

        private static void ExtractUnityDll(string zipPath)
        {
            // Use System.IO.Compression if available, otherwise shell out
            #if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            // Use unzip command on macOS/Linux
            var extractDir = Path.Combine(Application.temporaryCachePath, "zxing_extract");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "unzip";
            process.StartInfo.Arguments = $"-o \"{zipPath}\" -d \"{extractDir}\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new System.Exception("Failed to extract zip file");
            }

            // Find the Unity DLL - search for various paths and names
            string sourceDll = null;
            var searchPaths = new[] {
                Path.Combine(extractDir, "lib", "unity", "zxing.dll"),
                Path.Combine(extractDir, "lib", "unity", "ZXing.dll"),
                Path.Combine(extractDir, "lib", "net20", "zxing.dll"),
                Path.Combine(extractDir, "lib", "net20", "ZXing.dll"),
                Path.Combine(extractDir, "lib", "netstandard2.0", "zxing.dll"),
                Path.Combine(extractDir, "lib", "netstandard2.0", "ZXing.dll"),
                Path.Combine(extractDir, "lib", "net40", "zxing.dll"),
                Path.Combine(extractDir, "lib", "net40", "ZXing.dll"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    sourceDll = path;
                    Debug.Log($"[ZXingSetup] Found DLL at: {path}");
                    break;
                }
            }

            // If not found, search recursively for any zxing dll
            if (sourceDll == null)
            {
                Debug.Log($"[ZXingSetup] Searching recursively in: {extractDir}");
                var allFiles = Directory.GetFiles(extractDir, "*.dll", SearchOption.AllDirectories);
                Debug.Log($"[ZXingSetup] Found {allFiles.Length} DLL files");

                // Look for zxing.dll (case insensitive)
                var zxingFiles = allFiles.Where(f =>
                    Path.GetFileName(f).Equals("zxing.dll", System.StringComparison.OrdinalIgnoreCase)).ToArray();

                Debug.Log($"[ZXingSetup] Found {zxingFiles.Length} ZXing DLL files");
                foreach (var f in zxingFiles)
                    Debug.Log($"[ZXingSetup]   - {f}");

                // Prefer unity, net20, or netstandard2.0 version
                sourceDll = zxingFiles.FirstOrDefault(f => f.Contains("unity"))
                           ?? zxingFiles.FirstOrDefault(f => f.Contains("net20"))
                           ?? zxingFiles.FirstOrDefault(f => f.Contains("netstandard2.0"))
                           ?? zxingFiles.FirstOrDefault();
            }

            if (sourceDll == null)
            {
                // List what we found for debugging
                var allDlls = Directory.GetFiles(extractDir, "*.dll", SearchOption.AllDirectories);
                Debug.LogError($"[ZXingSetup] Could not find zxing.dll. Found these DLLs:");
                foreach (var dll in allDlls.Take(20))
                    Debug.LogError($"  - {dll}");
                throw new System.Exception("Could not find zxing.dll in the downloaded package. Check console for details.");
            }

            // Copy to Plugins folder
            var destPath = Path.Combine(PLUGIN_PATH, DLL_NAME);
            File.Copy(sourceDll, destPath, true);

            // Cleanup extract dir
            Directory.Delete(extractDir, true);

            #else
            // Windows - use PowerShell
            var extractDir = Path.Combine(Application.temporaryCachePath, "zxing_extract");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);

            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "powershell";
            process.StartInfo.Arguments = $"-Command \"Expand-Archive -Path '{zipPath}' -DestinationPath '{extractDir}' -Force\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit();

            // Find the Unity DLL - search for various paths and names
            string sourceDll = null;
            var searchPaths = new[] {
                Path.Combine(extractDir, "lib", "unity", "zxing.dll"),
                Path.Combine(extractDir, "lib", "unity", "ZXing.dll"),
                Path.Combine(extractDir, "lib", "net20", "zxing.dll"),
                Path.Combine(extractDir, "lib", "net20", "ZXing.dll"),
                Path.Combine(extractDir, "lib", "netstandard2.0", "zxing.dll"),
                Path.Combine(extractDir, "lib", "netstandard2.0", "ZXing.dll"),
                Path.Combine(extractDir, "lib", "net40", "zxing.dll"),
                Path.Combine(extractDir, "lib", "net40", "ZXing.dll"),
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    sourceDll = path;
                    Debug.Log($"[ZXingSetup] Found DLL at: {path}");
                    break;
                }
            }

            // If not found, search recursively for any zxing dll
            if (sourceDll == null)
            {
                Debug.Log($"[ZXingSetup] Searching recursively in: {extractDir}");
                var allFiles = Directory.GetFiles(extractDir, "*.dll", SearchOption.AllDirectories);
                Debug.Log($"[ZXingSetup] Found {allFiles.Length} DLL files");

                // Look for zxing.dll (case insensitive)
                var zxingFiles = allFiles.Where(f =>
                    Path.GetFileName(f).Equals("zxing.dll", System.StringComparison.OrdinalIgnoreCase)).ToArray();

                Debug.Log($"[ZXingSetup] Found {zxingFiles.Length} ZXing DLL files");
                foreach (var f in zxingFiles)
                    Debug.Log($"[ZXingSetup]   - {f}");

                // Prefer unity, net20, or netstandard2.0 version
                sourceDll = zxingFiles.FirstOrDefault(f => f.Contains("unity"))
                           ?? zxingFiles.FirstOrDefault(f => f.Contains("net20"))
                           ?? zxingFiles.FirstOrDefault(f => f.Contains("netstandard2.0"))
                           ?? zxingFiles.FirstOrDefault();
            }

            if (sourceDll == null)
            {
                // List what we found for debugging
                var allDlls = Directory.GetFiles(extractDir, "*.dll", SearchOption.AllDirectories);
                Debug.LogError($"[ZXingSetup] Could not find zxing.dll. Found these DLLs:");
                foreach (var dll in allDlls.Take(20))
                    Debug.LogError($"  - {dll}");
                throw new System.Exception("Could not find zxing.dll in the downloaded package. Check console for details.");
            }

            // Copy to Plugins folder
            var destPath = Path.Combine(PLUGIN_PATH, DLL_NAME);
            File.Copy(sourceDll, destPath, true);

            // Cleanup extract dir
            Directory.Delete(extractDir, true);
            #endif
        }

        public static bool IsZXingInstalled()
        {
            var paths = new[]
            {
                Path.Combine(PLUGIN_PATH, DLL_NAME),
                Path.Combine(PLUGIN_PATH, "ZXing.dll"),
                "Assets/Plugins/zxing.dll",
                "Assets/Plugins/ZXing.dll"
            };

            return paths.Any(File.Exists);
        }

        private static bool HasDefineSymbol()
        {
            var buildTarget = EditorUserBuildSettings.selectedBuildTargetGroup;
            var defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTarget);
            return defines.Contains(DEFINE_SYMBOL);
        }

        private static void AddDefineSymbol()
        {
            var buildTarget = EditorUserBuildSettings.selectedBuildTargetGroup;
            var currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(buildTarget);
            var definesList = currentDefines.Split(';').Where(d => !string.IsNullOrWhiteSpace(d)).ToList();

            if (!definesList.Contains(DEFINE_SYMBOL))
            {
                definesList.Add(DEFINE_SYMBOL);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(buildTarget, string.Join(";", definesList));
                Debug.Log($"[ZXingSetup] Added {DEFINE_SYMBOL} define symbol");
            }
        }

        // Auto-check on editor load
        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            // Delay to let Unity finish loading
            EditorApplication.delayCall += () =>
            {
                if (IsZXingInstalled() && !HasDefineSymbol())
                {
                    AddDefineSymbol();
                }
            };
        }
    }
}
#endif
