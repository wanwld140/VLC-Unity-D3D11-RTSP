#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VlcD3D11Rtsp
{
    /// <summary>
    /// Resolves the pinned private LibVLC 4 runtime. System-installed VLC is
    /// intentionally ignored so VLC 3 and VLC 4 can never be mixed.
    /// </summary>
    internal static class VlcWindowsRuntime
    {
        private const string RuntimeFolderName = "VLCUnityWindows";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetDllDirectoryW(string pathName);

        internal static string RuntimeBasePath
        {
            get
            {
#if UNITY_EDITOR_WIN
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                return Path.Combine(projectRoot ?? Application.dataPath,
                    "External", RuntimeFolderName);
#else
                return Path.Combine(Application.dataPath, RuntimeFolderName);
#endif
            }
        }

        internal static bool TryPrepare(out string runtimeBasePath, out string error)
        {
            runtimeBasePath = RuntimeBasePath;
            error = null;

            string nativeDirectory = Path.Combine(runtimeBasePath, "Plugins");
            string pluginDirectory = Path.Combine(nativeDirectory, "plugins");
            string libVlcPath = Path.Combine(nativeDirectory, "libvlc.dll");
            string libVlcCorePath = Path.Combine(nativeDirectory, "libvlccore.dll");

            if (!File.Exists(libVlcPath) || !File.Exists(libVlcCorePath) ||
                !Directory.Exists(pluginDirectory))
            {
                error = "Pinned LibVLC 4 runtime is missing. Run scripts/setup-dependencies.ps1.";
                return false;
            }

            if (!SetDllDirectoryW(nativeDirectory))
            {
                error = "Unable to configure the private LibVLC DLL directory. Win32 error " +
                        Marshal.GetLastWin32Error() + ".";
                return false;
            }

            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", pluginDirectory);
            return true;
        }
    }
}
#endif
