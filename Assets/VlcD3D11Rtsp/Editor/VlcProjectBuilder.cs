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
        private const string AndroidDemoScenePath =
            "Assets/VlcD3D11Rtsp/Demo/AndroidDemo.unity";
        private const string AndroidSmokeScenePath =
            "Assets/VlcD3D11Rtsp/Demo/AndroidSmoke.unity";

        [MenuItem("VLC RTSP/Generate sample scenes")]
        public static void GenerateScenes()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DemoScenePath) ?? "Assets");
            ConfigurePluginImporters();
            BuildDemoScene(DemoScenePath, true);
            BuildSmokeScene(SmokeScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(DemoScenePath, true),
                new EditorBuildSettingsScene(SmokeScenePath, false),
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VLC RTSP] Generated Demo.unity and Smoke.unity.");
        }

        [MenuItem("VLC RTSP/Generate Android sample scenes")]
        public static void GenerateAndroidScenes()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AndroidDemoScenePath) ?? "Assets");
            ConfigurePluginImporters();
            BuildDemoScene(AndroidDemoScenePath, false);
            BuildSmokeScene(AndroidSmokeScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[VLC RTSP] Generated AndroidDemo.unity and AndroidSmoke.unity.");
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

        [MenuItem("VLC RTSP/Build Android ARM64 demo APK")]
        public static void BuildAndroidDemo()
        {
            PrepareAndroidBuild();
            GenerateAndroidScenes();
            BuildAndroid(
                AndroidDemoScenePath,
                Path.Combine(ProjectRoot, "Build", "Android", "VlcRtspAndroidDemo.apk"));
        }

        [MenuItem("VLC RTSP/Build Android ARM64 smoke APK")]
        public static void BuildAndroidSmoke()
        {
            PrepareAndroidBuild();
            GenerateAndroidScenes();
            BuildAndroid(
                AndroidSmokeScenePath,
                Path.Combine(ProjectRoot, "Build", "Android", "VlcRtspAndroidSmoke.apk"));
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

        public static void BatchBuildAndroidDemo()
        {
            BuildAndroidDemo();
        }

        public static void BatchBuildAndroidSmoke()
        {
            BuildAndroidSmoke();
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

        private static void PrepareAndroidBuild()
        {
            if (!BuildPipeline.IsBuildTargetSupported(
                    BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException(
                    "Unity Android Build Support is not installed for this Editor.");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android))
                throw new InvalidOperationException("Unable to activate the Android target.");

            PlayerSettings.companyName = "VlcD3D11Rtsp";
            PlayerSettings.productName = "VlcRtspAndroidDemo";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(
                BuildTargetGroup.Android, "com.vlcd3d11rtsp.demo");
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            // The user-tested source project serializes renderer 11 (OpenGL ES 3)
            // as 0b000000. Vulkan is renderer 21 and failed texture creation on
            // the Android 16 / Adreno 830 acceptance device.
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
        }

        private static void BuildAndroid(string scenePath, string apkPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath) ?? ProjectRoot);
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { scenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.StrictMode,
            };
            UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException(
                    "Android build failed: " + report.summary.result + ".");
        }

        private static void BuildDemoScene(string scenePath, bool includeHikvision)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateBackgroundCamera();
            CreateEventSystem();
            Canvas canvas = CreateCanvas();

            GameObject video = CreateUiObject("Video", canvas.transform);
            RectTransform videoRect = video.GetComponent<RectTransform>();
            SetAnchors(videoRect, new Vector2(0.02f, 0.18f), new Vector2(0.98f, 0.98f));
            RawImage image = video.AddComponent<RawImage>();
            // RawImage.color multiplies every video pixel. Black hides valid frames.
            image.color = Color.white;
            AspectRatioFitter fitter = video.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            VlcRtspPlayer player = video.AddComponent<VlcRtspPlayer>();
            AssignObjectReference(player, "targetImage", image);
            AssignObjectReference(player, "aspectRatioFitter", fitter);
            HikvisionRtspPlayer hikvisionPlayer = null;
            if (includeHikvision)
            {
                hikvisionPlayer = video.AddComponent<HikvisionRtspPlayer>();
                AssignObjectReference(hikvisionPlayer, "targetImage", image);
                AssignObjectReference(hikvisionPlayer, "aspectRatioFitter", fitter);
            }

            GameObject controls = CreateUiObject("Controls", canvas.transform);
            RectTransform controlsRect = controls.GetComponent<RectTransform>();
            SetAnchors(controlsRect, new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.16f));
            Image controlsBackground = controls.AddComponent<Image>();
            controlsBackground.color = new Color(0.08f, 0.09f, 0.11f, 0.98f);

            InputField urlField = CreateInputField(controls.transform);
            SetAnchors(urlField.GetComponent<RectTransform>(),
                new Vector2(0.01f, 0.53f), new Vector2(0.47f, 0.94f));
            urlField.text = VlcRtspPlayer.DefaultTestUrl;

            Dropdown mode = CreateDropdown(controls.transform, includeHikvision);
            SetAnchors(mode.GetComponent<RectTransform>(),
                new Vector2(0.48f, 0.53f), new Vector2(0.78f, 0.94f));

            Button play = CreateButton("Play", controls.transform, "PLAY / APPLY");
            SetAnchors(play.GetComponent<RectTransform>(),
                new Vector2(0.79f, 0.53f), new Vector2(0.89f, 0.94f));
            Button stop = CreateButton("Stop", controls.transform, "STOP");
            SetAnchors(stop.GetComponent<RectTransform>(),
                new Vector2(0.90f, 0.53f), new Vector2(0.99f, 0.94f));

            Text status = CreateText("Status", controls.transform, 15, TextAnchor.MiddleLeft);
            SetAnchors(status.rectTransform,
                new Vector2(0.01f, 0.05f), new Vector2(0.99f, 0.47f));
            status.color = new Color(0.75f, 0.9f, 1f);
            status.text = "Idle";

            VlcDemoController controller = controls.AddComponent<VlcDemoController>();
            AssignObjectReference(controller, "player", player);
            if (includeHikvision)
                AssignObjectReference(controller, "hikvisionPlayer", hikvisionPlayer);
            AssignObjectReference(controller, "urlInput", urlField);
            AssignObjectReference(controller, "modeDropdown", mode);
            AssignObjectReference(controller, "statusText", status);
            UnityEventTools.AddPersistentListener(play.onClick, controller.ApplyAndPlay);
            UnityEventTools.AddPersistentListener(stop.onClick, controller.Stop);

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        private static void BuildSmokeScene(string scenePath)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateBackgroundCamera();
            Canvas canvas = CreateCanvas();
            GameObject video = CreateUiObject("Video", canvas.transform);
            SetAnchors(video.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            RawImage image = video.AddComponent<RawImage>();
            image.color = Color.white;
            AspectRatioFitter fitter = video.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            VlcRtspPlayer player = video.AddComponent<VlcRtspPlayer>();
            AssignObjectReference(player, "targetImage", image);
            AssignObjectReference(player, "aspectRatioFitter", fitter);

            VlcSmokeTestController smoke = new GameObject("SmokeTest")
                .AddComponent<VlcSmokeTestController>();
            AssignObjectReference(smoke, "player", player);
            EditorSceneManager.SaveScene(scene, scenePath);
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

        private static void CreateBackgroundCamera()
        {
            // Screen-space UI does not require a camera, but an empty scene makes the
            // Unity Game view draw "No cameras rendering" behind the video. A clear-only
            // camera keeps the sample presentation clean without rendering scene objects.
            Camera camera = new GameObject("Background Camera", typeof(Camera))
                .GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            camera.depth = -100f;
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

        private static Dropdown CreateDropdown(Transform parent, bool includeHikvision)
        {
            GameObject root = CreateUiObject("Decode mode", parent);
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.22f, 1f);
            Dropdown dropdown = root.AddComponent<Dropdown>();
            Text label = CreateText("Label", root.transform, 15, TextAnchor.MiddleLeft);
            SetAnchors(label.rectTransform, new Vector2(0.05f, 0f), new Vector2(0.9f, 1f));
            label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            dropdown.captionText = label;

            GameObject template = CreateUiObject("Template", root.transform);
            RectTransform templateRect = template.GetComponent<RectTransform>();
            // Use the standard fixed-height dropdown template. Unity flips it upward
            // automatically near the bottom; four 30 px backend rows need 120 px.
            SetAnchors(templateRect, new Vector2(0f, 0f), new Vector2(1f, 0f));
            templateRect.pivot = new Vector2(0.5f, 1f);
            float dropdownHeight = includeHikvision ? 120f : 90f;
            templateRect.sizeDelta = new Vector2(0f, dropdownHeight);
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
            content.GetComponent<RectTransform>().sizeDelta =
                new Vector2(0f, dropdownHeight);
            scroll.content = content.GetComponent<RectTransform>();

            Toggle item = CreateUiObject("Item", content.transform).AddComponent<Toggle>();
            SetAnchors(item.GetComponent<RectTransform>(), new Vector2(0f, 1f), Vector2.one);
            item.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 30f);
            Text itemLabel = CreateText("Item Label", item.transform, 14, TextAnchor.MiddleLeft);
            SetAnchors(itemLabel.rectTransform, new Vector2(0.05f, 0f), Vector2.one);
            itemLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            item.targetGraphic = itemLabel;
            dropdown.template = template.GetComponent<RectTransform>();
            dropdown.itemText = itemLabel;
            // Serialize all four backends into Demo.unity as well as rebuilding them in
            // VlcDemoController.Awake, so the scene remains understandable in Edit mode.
            var options = includeHikvision
                ? new System.Collections.Generic.List<string>
                {
                    "Auto (GPU -> CPU fallback)",
                    "CPU callbacks",
                    "GPU / D3D11 native texture",
                    "Hikvision SDK / PlayM4",
                }
                : new System.Collections.Generic.List<string>
                {
                    "Auto (native texture -> CPU fallback)",
                    "CPU callbacks",
                    "GPU request / Android native texture",
                };
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(0);
            dropdown.RefreshShownValue();
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
#if UNITY_2022_2_OR_NEWER
            // Unity 2022.2 removed Arial.ttf from the built-in resource API.
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
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
            ConfigureManagedLibVlcSharpPlugin();
            ConfigureAndroidPlugin(
                "Assets/Plugins/Android/VLCUnity/vlc-android-java.aar", null);
            ConfigureAndroidPlugin(
                "Assets/Plugins/Android/VLCUnity/arm64-v8a/libvlc.so", "ARM64");
            ConfigureAndroidPlugin(
                "Assets/Plugins/Android/VLCUnity/arm64-v8a/libVLCUnityPlugin.so",
                "ARM64");
        }

        private static void ConfigureManagedLibVlcSharpPlugin()
        {
            const string assetPath = "Assets/Plugins/Managed/LibVLCSharp.dll";
            PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
                throw new InvalidOperationException("Plug-in importer is missing: " + assetPath);

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("CPU", "AnyCPU");
            importer.SetEditorData("OS", "Windows");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "AnyCPU");
            importer.SetPlatformData(BuildTarget.Android, "CPU", "AnyCPU");
            importer.isPreloaded = false;
            importer.SaveAndReimport();
        }

        private static void ConfigureAndroidPlugin(string assetPath, string cpu)
        {
            PluginImporter importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
                throw new InvalidOperationException("Plug-in importer is missing: " + assetPath);

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(false);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
            if (!string.IsNullOrEmpty(cpu))
                importer.SetPlatformData(BuildTarget.Android, "CPU", cpu);
            importer.SaveAndReimport();
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
