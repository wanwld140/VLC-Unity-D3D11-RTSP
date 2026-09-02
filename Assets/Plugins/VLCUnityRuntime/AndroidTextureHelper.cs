// Android subset of VideoLAN VLC for Unity TextureHelper.cs, f2bbedd5.
// LGPL-2.1-or-later; see LICENSES. Windows Editor deliberately has no native entry points.
#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace LibVLCSharp
{
    public static class AndroidTextureHelper
    {
        private const string Plugin = "libVLCUnityPlugin";

        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl,
            EntryPoint = "libvlc_unity_set_unity_texture_vulkan")]
        private static extern bool SetUnityTextureVulkan(IntPtr player, IntPtr texture);

        [DllImport(Plugin, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetRenderEventFunc();

        public static Texture2D CreateNativeTexture(MediaPlayer player, bool linear)
        {
            uint width = 0, height = 0;
            player.Size(0, ref width, ref height);
            if (width == 0 || height == 0) return null;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
            {
                var texture = new Texture2D(
                    (int)width, (int)height, TextureFormat.RGBA32, false, linear);
                texture.Apply(false, false);
                if (SetUnityTextureVulkan(player.NativeReference, texture.GetNativeTexturePtr())) return texture;
                UnityEngine.Object.Destroy(texture);
                Debug.LogError("[VLC RTSP] Android Vulkan texture initialization failed.");
                return null;
            }
            var pointer = player.GetTexture(width, height, out bool updated);
            return updated && pointer != IntPtr.Zero
                ? Texture2D.CreateExternalTexture((int)width, (int)height,
                    TextureFormat.RGBA32, false, linear, pointer)
                : null;
        }

        public static bool UpdateTexture(Texture2D texture, MediaPlayer player)
        {
            if (texture == null) return false;
            var pointer = player.GetTexture((uint)texture.width, (uint)texture.height, out bool updated);
            if (!updated || pointer == IntPtr.Zero) return false;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
                GL.IssuePluginEvent(GetRenderEventFunc(), 0);
            else
                texture.UpdateExternalTexture(pointer);
            return true;
        }
    }
}
#endif
