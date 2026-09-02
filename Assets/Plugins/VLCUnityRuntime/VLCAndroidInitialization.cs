// Android subset of VideoLAN VLC for Unity OnLoad.cs, f2bbedd5, LGPL-2.1-or-later.
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LibVLCSharp
{
    public static class VLCAndroidInitialization
    {
        private const string Plugin = "libVLCUnityPlugin";
        private static bool initialized;

        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_set_color_space")]
        private static extern void SetColorSpace(int colorSpace);

        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetRenderEventFunc();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BeforeSceneLoad() => EnsureInitialized();

        public static bool EnsureInitialized()
        {
            if (initialized) return true;
            try
            {
                SetColorSpace(QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Linear ? 1 : 0);
                GL.IssuePluginEvent(GetRenderEventFunc(), 1);
                initialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[VLC RTSP] Android native plug-in initialization failed: " +
                               ex.GetType().Name + ".");
                return false;
            }
        }
    }
}
#endif
