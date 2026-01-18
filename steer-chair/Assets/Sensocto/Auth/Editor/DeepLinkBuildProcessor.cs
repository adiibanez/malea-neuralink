#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;
using System.IO;

using Sensocto.SDK;

namespace Sensocto.Auth.Editor
{
    /// <summary>
    /// Build processor that configures URL schemes for deep linking.
    /// Automatically adds the 'sensocto://' URL scheme to platform builds.
    /// </summary>
    public class DeepLinkBuildProcessor : IPreprocessBuildWithReport
    {
        public const string URL_SCHEME = "sensocto";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log($"[DeepLinkBuildProcessor] Configuring URL scheme '{URL_SCHEME}' for {report.summary.platform}");
        }

#if UNITY_IOS
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild_iOS(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;

            // For iOS, modify Info.plist to add URL scheme
            var plistPath = Path.Combine(path, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning("[DeepLinkBuildProcessor] Info.plist not found at: " + plistPath);
                return;
            }

            // Read plist
            var plist = new UnityEditor.iOS.Xcode.PlistDocument();
            plist.ReadFromFile(plistPath);

            // Add URL scheme
            var urlTypes = plist.root.CreateArray("CFBundleURLTypes");
            var urlTypeDict = urlTypes.AddDict();
            urlTypeDict.SetString("CFBundleURLName", PlayerSettings.applicationIdentifier);
            var urlSchemes = urlTypeDict.CreateArray("CFBundleURLSchemes");
            urlSchemes.AddString(URL_SCHEME);

            // Save plist
            plist.WriteToFile(plistPath);
            Debug.Log($"[DeepLinkBuildProcessor] Added URL scheme to iOS Info.plist");
        }
#endif

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild_macOS(BuildTarget target, string path)
        {
            if (target != BuildTarget.StandaloneOSX) return;

            // For macOS, modify Info.plist inside the .app bundle
            var appPath = path;
            if (!path.EndsWith(".app"))
            {
                appPath = path + ".app";
            }

            var plistPath = Path.Combine(appPath, "Contents", "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning("[DeepLinkBuildProcessor] Info.plist not found at: " + plistPath);
                return;
            }

            // Read and modify plist
            var plistContent = File.ReadAllText(plistPath);

            // Check if URL scheme already exists
            if (plistContent.Contains("CFBundleURLTypes"))
            {
                Debug.Log("[DeepLinkBuildProcessor] CFBundleURLTypes already exists in Info.plist");
                return;
            }

            // Add URL scheme before closing </dict></plist>
            var urlSchemeEntry = @"
	<key>CFBundleURLTypes</key>
	<array>
		<dict>
			<key>CFBundleURLName</key>
			<string>" + PlayerSettings.applicationIdentifier + @"</string>
			<key>CFBundleURLSchemes</key>
			<array>
				<string>" + URL_SCHEME + @"</string>
			</array>
		</dict>
	</array>";

            // Insert before the closing </dict>
            var insertIndex = plistContent.LastIndexOf("</dict>");
            if (insertIndex > 0)
            {
                plistContent = plistContent.Insert(insertIndex, urlSchemeEntry + "\n");
                File.WriteAllText(plistPath, plistContent);
                Debug.Log($"[DeepLinkBuildProcessor] Added URL scheme to macOS Info.plist");
            }
        }

        /// <summary>
        /// Menu item to manually configure URL scheme in player settings.
        /// </summary>
        [MenuItem("Sensocto/Configure Deep Link URL Scheme")]
        public static void ConfigureUrlScheme()
        {
            Debug.Log($"[DeepLinkBuildProcessor] URL Scheme: {URL_SCHEME}://");
            Debug.Log($"[DeepLinkBuildProcessor] Example link: {URL_SCHEME}://auth?token=YOUR_TOKEN&user=UserName");

            EditorUtility.DisplayDialog(
                "Deep Link Configuration",
                $"URL Scheme: {URL_SCHEME}://\n\n" +
                $"Example deep link:\n{URL_SCHEME}://auth?token=YOUR_TOKEN&user=UserName\n\n" +
                "The URL scheme will be automatically added to builds.\n\n" +
                "For Editor testing, use the DeepLinkHandler component's 'Test Deep Link' context menu.",
                "OK"
            );
        }

        /// <summary>
        /// Menu item to test deep link in editor.
        /// </summary>
        [MenuItem("Sensocto/Test Deep Link (Editor)")]
        public static void TestDeepLinkInEditor()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Test Deep Link",
                    "Enter Play mode first, then use this menu item to simulate a deep link.",
                    "OK"
                );
                return;
            }

            var testUrl = EditorInputDialog.Show(
                "Test Deep Link",
                "Enter deep link URL:",
                $"{URL_SCHEME}://auth?token=test-token&user=TestUser"
            );

            if (!string.IsNullOrEmpty(testUrl))
            {
                // Simulate deep link activation
                var data = Sensocto.SDK.DeepLinkHandler.ParseDeepLink(testUrl);
                if (data != null && !string.IsNullOrEmpty(data.Token))
                {
                    AuthManager.SetCredentials(data.Token, data.UserName, data.UserId, data.ServerUrl);
                    Debug.Log($"[Test] Deep link processed: user={data.UserName}, token={(data.Token.Length > 10 ? data.Token.Substring(0, 10) + "..." : data.Token)}");
                }
            }
        }
    }

    /// <summary>
    /// Simple input dialog for editor.
    /// </summary>
    public class EditorInputDialog : EditorWindow
    {
        private string _input;
        private string _message;
        private bool _shouldClose;
        private static string _result;

        public static string Show(string title, string message, string defaultValue = "")
        {
            _result = null;
            var window = GetWindow<EditorInputDialog>(true, title, true);
            window._message = message;
            window._input = defaultValue;
            window.minSize = new Vector2(400, 120);
            window.maxSize = new Vector2(400, 120);
            window.ShowModalUtility();
            return _result;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(_message);
            EditorGUILayout.Space(5);
            _input = EditorGUILayout.TextField(_input);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Cancel", GUILayout.Width(80)))
            {
                _result = null;
                Close();
            }

            if (GUILayout.Button("OK", GUILayout.Width(80)))
            {
                _result = _input;
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
