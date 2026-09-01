#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VlcD3D11Rtsp.Editor
{
    public static class VlcEditorSelfTests
    {
        private const string ExpectedLibVlcSharpHash =
            "260EC9F6DCFD5DFC57372D3B1B1167A44D62F3A068BCB1D8EED541D4F529275B";

        [MenuItem("VLC RTSP/Run editor self-tests")]
        public static void Run()
        {
            string secretUrl = "rtsp://viewer:secret@camera.local/live?token=private";
            string sanitized = VlcLogSanitizer.Sanitize(
                "failed to open " + secretUrl + " at input");
            Assert(!sanitized.Contains("secret") && !sanitized.Contains("private"),
                "RTSP URL sanitizer leaked credentials or a query token.");
            Assert(sanitized.Contains("<redacted-rtsp-url>"),
                "RTSP URL sanitizer did not insert its redaction marker.");

            Assert(!VlcLogSanitizer.IsHardwareDecoderEvidence(
                    "d3d11va", "looking for hw decoder module matching any"),
                "Module discovery was incorrectly accepted as hardware evidence.");
            Assert(VlcLogSanitizer.IsHardwareDecoderEvidence(
                    "d3d11va",
                    "Using D3D11VA (Example GPU, vendor 1234, device 5678, revision 1)"),
                "A real VLC D3D11VA selection message was not recognized.");

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 Directory.GetCurrentDirectory();
            string managedDll = Path.Combine(
                projectRoot, "Assets", "Plugins", "Managed", "LibVLCSharp.dll");
            Assert(File.Exists(managedDll), "LibVLCSharp.dll is missing.");
            Assert(HashFile(managedDll) == ExpectedLibVlcSharpHash,
                "LibVLCSharp.dll does not match the pinned source artifact.");

            AssertSampleDisplay("Assets/VlcD3D11Rtsp/Demo/Demo.unity");
            AssertSampleDisplay("Assets/VlcD3D11Rtsp/Demo/Smoke.unity");

            Debug.Log("[VLC RTSP] Editor self-tests passed.");
        }

        public static void BatchRun()
        {
            Run();
        }

        private static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static void AssertSampleDisplay(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool closeWhenDone = !scene.IsValid() || !scene.isLoaded;
            if (closeWhenDone)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            try
            {
                VlcRtspPlayer player = FindInScene<VlcRtspPlayer>(scene);
                RawImage image = player == null ? null : player.GetComponent<RawImage>();
                AspectRatioFitter fitter = player == null
                    ? null
                    : player.GetComponent<AspectRatioFitter>();
                Camera camera = FindInScene<Camera>(scene);

                Assert(player != null, scenePath + " has no VlcRtspPlayer.");
                Assert(image != null, scenePath + " has no video RawImage.");
                Assert(fitter != null, scenePath + " has no video AspectRatioFitter.");
                Assert(image.color == Color.white,
                    scenePath + " video RawImage must be white; black hides the texture.");
                Assert(camera != null, scenePath + " has no background camera.");

                var serializedPlayer = new SerializedObject(player);
                Assert(serializedPlayer.FindProperty("targetImage").objectReferenceValue == image,
                    scenePath + " player targetImage is not serialized.");
                Assert(serializedPlayer.FindProperty("aspectRatioFitter").objectReferenceValue == fitter,
                    scenePath + " player aspectRatioFitter is not serialized.");
                Assert(serializedPlayer.FindProperty("flipHorizontally").boolValue,
                    scenePath + " horizontal flip must default to enabled.");
                Assert(serializedPlayer.FindProperty("flipVertically").boolValue,
                    scenePath + " vertical flip must default to enabled.");

                if (scenePath.EndsWith("/Demo.unity", StringComparison.OrdinalIgnoreCase))
                    AssertDemoLayout(scene);
            }
            finally
            {
                if (closeWhenDone) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void AssertDemoLayout(Scene scene)
        {
            InputField url = FindInScene<InputField>(scene);
            Dropdown dropdown = FindInScene<Dropdown>(scene);
            Button play = FindNamedInScene<Button>(scene, "Play");
            Button stop = FindNamedInScene<Button>(scene, "Stop");

            Assert(url != null && Approximately(url.GetComponent<RectTransform>().anchorMin,
                       new Vector2(0.01f, 0.53f)) &&
                   Approximately(url.GetComponent<RectTransform>().anchorMax,
                       new Vector2(0.47f, 0.94f)),
                "Demo URL field anchors are incorrect.");
            Assert(dropdown != null &&
                   Approximately(dropdown.GetComponent<RectTransform>().anchorMin,
                       new Vector2(0.48f, 0.53f)) &&
                   Approximately(dropdown.GetComponent<RectTransform>().anchorMax,
                       new Vector2(0.78f, 0.94f)),
                "Demo mode dropdown anchors are incorrect.");
            Assert(play != null &&
                   Approximately(play.GetComponent<RectTransform>().anchorMin,
                       new Vector2(0.79f, 0.53f)) &&
                   Approximately(play.GetComponent<RectTransform>().anchorMax,
                       new Vector2(0.89f, 0.94f)),
                "Demo Play button anchors are incorrect.");
            Assert(stop != null &&
                   Approximately(stop.GetComponent<RectTransform>().anchorMin,
                       new Vector2(0.90f, 0.53f)) &&
                   Approximately(stop.GetComponent<RectTransform>().anchorMax,
                       new Vector2(0.99f, 0.94f)),
                "Demo Stop button anchors are incorrect.");

            Assert(dropdown.captionText != null &&
                   dropdown.captionText.horizontalOverflow == HorizontalWrapMode.Overflow,
                "Demo dropdown caption must not wrap.");
            Assert(dropdown.itemText != null &&
                   dropdown.itemText.horizontalOverflow == HorizontalWrapMode.Overflow,
                "Demo dropdown items must not wrap.");
            Assert(dropdown.template != null &&
                   Mathf.Approximately(dropdown.template.sizeDelta.y, 90f) &&
                   Mathf.Approximately(dropdown.template.pivot.y, 1f),
                "Demo dropdown template must be a 90 px top-pivoted list.");
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null) return component;
            }

            return null;
        }

        private static T FindNamedInScene<T>(Scene scene, string name) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true))
            {
                if (component.gameObject.name == name) return component;
            }

            return null;
        }

        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                   Mathf.Approximately(left.y, right.y);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
