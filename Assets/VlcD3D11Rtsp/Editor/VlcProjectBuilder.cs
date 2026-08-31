#if UNITY_EDITOR_WIN
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace VlcD3D11Rtsp.Editor
{
    public static class VlcProjectBuilder
    {
        private const string DemoScenePath = "Assets/VlcD3D11Rtsp/Demo/Demo.unity";
        private const string SmokeScenePath = "Assets/VlcD3D11Rtsp/Demo/Smoke.unity";

        [MenuItem("VLC RTSP/Generate sample scenes")]
        public static void GenerateScenes()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DemoScenePath) ?? "Assets");
            ConfigurePluginImporters();
            BuildDemoScene();
            BuildSmokeScene();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(DemoScenePath, true),
                new EditorBuildSettingsScene(SmokeScenePath, false),
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VLC RTSP] Generated Demo.unity and Smoke.unity.");
        }

        [MenuItem("VLC RTSP/Build Windows demo")]
        public static void BuildWindowsDemo()
        {
            GenerateScenes();
            BuildWindows(
                DemoScenePath,
                Path.Combine(ProjectRoot, "Build", "Windows", "VlcD3D11RtspDemo.exe"));
        }

        [MenuItem("VLC RTSP/Build Windows smoke player")]
        public static void BuildWindowsSmoke()
        {
            GenerateScenes();
            BuildWindows(
                SmokeScenePath,
                Path.Combine(ProjectRoot, "Build", "Smoke", "VlcD3D11RtspSmoke.exe"));
        }

        public static void BatchGenerateScenes()
        {
            GenerateScenes();
        }

        public static void BatchBuildWindowsDemo()
        {
            BuildWindowsDemo();
        }

        public static void BatchBuildWindowsSmoke()
        {
            BuildWindowsSmoke();
        }

        private static void BuildWindows(string scenePath, string executablePath)
        {
            PlayerSettings.companyName = "VlcD3D11Rtsp";
            PlayerSettings.productName = Path.GetFileNameWithoutExtension(executablePath);
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 });

            Directory.CreateDirectory(Path.GetDirectoryName(executablePath) ?? ProjectRoot);
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = executablePath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.StrictMode,
            };
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException(
                    "Windows build failed: " + report.summary.result + ".");
        }

        private static void BuildDemoScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateEventSystem();
            Canvas canvas = CreateCanvas();

            GameObject video = CreateUiObject("Video", canvas.transform);
            RectTransform videoRect = video.GetComponent<RectTransform>();
            SetAnchors(videoRect, new Vector2(0.02f, 0.18f), new Vector2(0.98f, 0.98f));
            RawImage image = video.AddComponent<RawImage>();
            image.color = Color.black;
            AspectRatioFitter fitter = video.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            VlcRtspPlayer player = video.AddComponent<VlcRtspPlayer>();

            GameObject controls = CreateUiObject("Controls", canvas.transform);
            RectTransform controlsRect = controls.GetComponent<RectTransform>();
            SetAnchors(controlsRect, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.16f));
            Image controlsBackground = controls.AddComponent<Image>();
            controlsBackground.color = new Color(0.08f, 0.09f, 0.11f, 0.98f);

            InputField urlField = CreateInputField(controls.transform);
            SetAnchors(urlField.GetComponent<RectTransform>(),
                new Vector2(0.01f, 0.53f), new Vector2(0.55f, 0.94f));
            urlField.text = "rtsp://127.0.0.1:8554/live";

            Dropdown mode = CreateDropdown(controls.transform);
            SetAnchors(mode.GetComponent<RectTransform>(),
                new Vector2(0.56f, 0.53f), new Vector2(0.74f, 0.94f));

            Button play = CreateButton("Play", controls.transform, "PLAY / APPLY");
            SetAnchors(play.GetComponent<RectTransform>(),
                new Vector2(0.75f, 0.53f), new Vector2(0.87f, 0.94f));
            Button stop = CreateButton("Stop", controls.transform, "STOP");
            SetAnchors(stop.GetComponent<RectTransform>(),
                new Vector2(0.88f, 0.53f), new Vector2(0.99f, 0.94f));

            Text status = CreateText("Status", controls.transform, 15, TextAnchor.MiddleLeft);
            SetAnchors(status.rectTransform,
                new Vector2(0.01f, 0.05f), new Vector2(0.99f, 0.47f));
            status.color = new Color(0.75f, 0.9f, 1f);
            status.text = "Idle";

            VlcDemoController controller = controls.AddComponent<VlcDemoController>();
            AssignObjectReference(controller, "player", player);
            AssignObjectReference(controller, "urlInput", urlField);
            AssignObjectReference(controller, "modeDropdown", mode);
            AssignObjectReference(controller, "statusText", status);
            UnityEventTools.AddPersistentListener(play.onClick, controller.ApplyAndPlay);
            UnityEventTools.AddPersistentListener(stop.onClick, controller.Stop);

            EditorSceneManager.SaveScene(scene, DemoScenePath);
        }

        private static void BuildSmokeScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Canvas canvas = CreateCanvas();
            GameObject video = CreateUiObject("Video", canvas.transform);
            SetAnchors(video.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            video.AddComponent<RawImage>().color = Color.black;
            video.AddComponent<AspectRatioFitter>().aspectMode =
                AspectRatioFitter.AspectMode.FitInParent;
            VlcRtspPlayer player = video.AddComponent<VlcRtspPlayer>();

            VlcSmokeTestController smoke = new GameObject("SmokeTest")
                .AddComponent<VlcSmokeTestController>();
            AssignObjectReference(smoke, "player", player);
            EditorSceneManager.SaveScene(scene, SmokeScenePath);
        }

        private static Canvas CreateCanvas()
        {
            GameObject gameObject = new GameObject(
                "Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = gameObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            return canvas;
        }

        private static void CreateEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static InputField CreateInputField(Transform parent)
        {
            GameObject root = CreateUiObject("RTSP URL", parent);
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.22f, 1f);
            InputField input = root.AddComponent<InputField>();

            Text text = CreateText("Text", root.transform, 16, TextAnchor.MiddleLeft);
            SetAnchors(text.rectTransform, new Vector2(0.02f, 0f), new Vector2(0.98f, 1f));
            text.color = Color.white;
            input.textComponent = text;

            Text placeholder = CreateText(
                "Placeholder", root.transform, 16, TextAnchor.MiddleLeft);
            SetAnchors(placeholder.rectTransform,
                new Vector2(0.02f, 0f), new Vector2(0.98f, 1f));
            placeholder.color = new Color(1f, 1f, 1f, 0.38f);
            placeholder.text = "rtsp://camera-or-server/path";
            input.placeholder = placeholder;
            return input;
        }

        private static Dropdown CreateDropdown(Transform parent)
        {
            GameObject root = CreateUiObject("Decode mode", parent);
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.22f, 1f);
            Dropdown dropdown = root.AddComponent<Dropdown>();
            Text label = CreateText("Label", root.transform, 15, TextAnchor.MiddleLeft);
            SetAnchors(label.rectTransform, new Vector2(0.05f, 0f), new Vector2(0.9f, 1f));
            label.color = Color.white;
            dropdown.captionText = label;

            GameObject template = CreateUiObject("Template", root.transform);
            SetAnchors(template.GetComponent<RectTransform>(), new Vector2(0f, -3f), new Vector2(1f, 0f));
            template.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 120f);
            template.SetActive(false);
            Image templateImage = template.AddComponent<Image>();
            templateImage.color = new Color(0.12f, 0.13f, 0.16f, 1f);
            ScrollRect scroll = template.AddComponent<ScrollRect>();

            GameObject viewport = CreateUiObject("Viewport", template.transform);
            SetAnchors(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            viewport.AddComponent<Image>().color = Color.white;
            scroll.viewport = viewport.GetComponent<RectTransform>();

            GameObject content = CreateUiObject("Content", viewport.transform);
            SetAnchors(content.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one);
            content.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 90f);
            scroll.content = content.GetComponent<RectTransform>();

            Toggle item = CreateUiObject("Item", content.transform).AddComponent<Toggle>();
            SetAnchors(item.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one);
            item.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 30f);
            Text itemLabel = CreateText("Item Label", item.transform, 14, TextAnchor.MiddleLeft);
            SetAnchors(itemLabel.rectTransform, new Vector2(0.05f, 0f), Vector2.one);
            item.targetGraphic = itemLabel;
            dropdown.template = template.GetComponent<RectTransform>();
            dropdown.itemText = itemLabel;
            return dropdown;
        }

        private static Button CreateButton(string name, Transform parent, string label)
        {
            GameObject root = CreateUiObject(name, parent);
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.08f, 0.44f, 0.73f, 1f);
            Button button = root.AddComponent<Button>();
            button.targetGraphic = image;
            Text text = CreateText("Label", root.transform, 14, TextAnchor.MiddleCenter);
            SetAnchors(text.rectTransform, Vector2.zero, Vector2.one);
            text.text = label;
            text.color = Color.white;
            return button;
        }

        private static Text CreateText(
            string name, Transform parent, int size, TextAnchor alignment)
        {
            GameObject root = CreateUiObject(name, parent);
            Text text = root.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void SetAnchors(RectTransform transform, Vector2 min, Vector2 max)
        {
            transform.anchorMin = min;
            transform.anchorMax = max;
            transform.offsetMin = Vector2.zero;
            transform.offsetMax = Vector2.zero;
        }

        private static void AssignObjectReference(
            UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePluginImporters()
        {
            ConfigureWindowsPlugin(
                "Assets/Plugins/x86_64/VLCUnityPlugin.dll", true, "x86_64");
            ConfigureWindowsPlugin(
                "Assets/Plugins/Managed/LibVLCSharp.dll", false, "AnyCPU");
        }

        private static void ConfigureWindowsPlugin(
            string assetPath, bool preloaded, string cpu)
        {
            PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
                throw new InvalidOperationException("Plug-in importer is missing: " + assetPath);

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("CPU", cpu);
            importer.SetEditorData("OS", "Windows");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", cpu);
            importer.isPreloaded = preloaded;
            importer.SaveAndReimport();
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ??
                                             Directory.GetCurrentDirectory();
    }
}
#endif
