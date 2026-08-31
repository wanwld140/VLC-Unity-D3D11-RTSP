#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Runtime.InteropServices;
using LibVLCSharp;
using UnityEngine;
using UnityEngine.Rendering;

namespace VlcD3D11Rtsp
{
    /// <summary>
    /// Managed contract for the repository's VLCUnityPlugin D3D11 bridge.
    /// The native plug-in owns LibVLC 4 output callbacks and exposes the
    /// resulting ID3D11Texture2D to Unity as an external texture.
    /// </summary>
    internal static class VlcNativeBridge
    {
        private const string PluginName = "VLCUnityPlugin";
        private const int ExpectedApiVersion = 1;
        private const int UnityRendererD3D11 = 2;
        private const int RendererCleanupEvent = 3;

        private static bool initialized;
        private static bool cleanupPending;
        private static IntPtr renderEvent;

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_bridge_api_version")]
        private static extern int BridgeApiVersionNative();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_set_next_media_player_rendering_mode")]
        private static extern void SetNextMediaPlayerRenderingModeNative(int mode);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_has_native_renderer")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool HasNativeRendererNative(IntPtr mediaPlayer);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_get_unity_renderer_type")]
        private static extern int GetUnityRendererTypeNative();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_set_color_space")]
        private static extern void SetColorSpaceNative(int colorSpace);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_has_retired_renderers")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool HasRetiredRenderersNative();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "GetRenderEventFunc")]
        private static extern IntPtr GetRenderEventFuncNative();

        internal static int ApiVersion => initialized ? ExpectedApiVersion : 0;

        internal static bool TryInitialize(out string error)
        {
            error = null;
            if (initialized) return true;

            try
            {
                int version = BridgeApiVersionNative();
                if (version != ExpectedApiVersion)
                {
                    error = "VLCUnityPlugin API mismatch. Expected " +
                            ExpectedApiVersion + ", got " + version + ".";
                    return false;
                }

                SetColorSpaceNative(
                    QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Linear ? 1 : 0);
                renderEvent = GetRenderEventFuncNative();
                initialized = true;
                return true;
            }
            catch (Exception exception) when (
                exception is DllNotFoundException ||
                exception is EntryPointNotFoundException ||
                exception is BadImageFormatException)
            {
                error = "VLCUnityPlugin is missing, incompatible, or built for the wrong architecture (" +
                        exception.GetType().Name + ").";
                return false;
            }
        }

        /// <summary>
        /// Must run on the same thread immediately before new MediaPlayer(libVlc).
        /// The native choice is thread-local and consumed by that constructor.
        /// </summary>
        internal static void PrepareNextMediaPlayer(VlcDecodeMode mode)
        {
            SetNextMediaPlayerRenderingModeNative(mode == VlcDecodeMode.Cpu ? 0 : 1);
        }

        internal static bool HasNativeRenderer(MediaPlayer player)
        {
            return player != null &&
                   HasNativeRendererNative(player.NativeReference);
        }

        internal static bool IsD3D11Renderer()
        {
            return SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 &&
                   GetUnityRendererTypeNative() == UnityRendererD3D11;
        }

        internal static string RendererDescription()
        {
            int renderer = GetUnityRendererTypeNative();
            return renderer == UnityRendererD3D11
                ? "Direct3D 11"
                : "Unity renderer id " + renderer;
        }

        internal static void QueueRendererCleanup()
        {
            if (!initialized) return;
            cleanupPending = true;
            IssueCleanupEvent();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Application.onBeforeRender -= PumpRendererCleanup;
            initialized = false;
            cleanupPending = false;
            renderEvent = IntPtr.Zero;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCleanupPump()
        {
            Application.onBeforeRender -= PumpRendererCleanup;
            Application.onBeforeRender += PumpRendererCleanup;
        }

        private static void PumpRendererCleanup()
        {
            if (!cleanupPending || !initialized) return;

            try
            {
                if (!HasRetiredRenderersNative())
                {
                    cleanupPending = false;
                    return;
                }

                IssueCleanupEvent();
            }
            catch (DllNotFoundException)
            {
                cleanupPending = false;
            }
            catch (EntryPointNotFoundException)
            {
                cleanupPending = false;
            }
        }

        private static void IssueCleanupEvent()
        {
            if (renderEvent != IntPtr.Zero)
                GL.IssuePluginEvent(renderEvent, RendererCleanupEvent);
        }
    }
}
#endif
