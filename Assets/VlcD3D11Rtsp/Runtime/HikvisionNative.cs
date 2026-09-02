#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace VlcD3D11Rtsp
{
    /// <summary>
    /// 海康 HCNetSDK V6.1.9.48 / PlayCtrl V7.4 的最小 P/Invoke 表面。
    /// 结构体字段按本版本 HCNetSDK.h 定义，不使用随包旧 C# 示例中已经漂移的尾部字段。
    /// </summary>
    internal static class HikvisionNative
    {
        internal const uint NetDvrSysHead = 1;
        internal const uint NetDvrStreamData = 2;
        internal const uint StreamRealTime = 0;
        internal const int FrameTypeYv12 = 3;

        internal const int InitCfgSdkPath = 2;
        internal const int InitCfgLibCryptoPath = 3;
        internal const int InitCfgLibSslPath = 4;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate void RealDataCallback(
            int realHandle,
            uint dataType,
            IntPtr buffer,
            uint bufferSize,
            IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        internal delegate void DecodeCallback(
            int port,
            IntPtr buffer,
            int bufferSize,
            ref FrameInfo frameInfo,
            int reserved1,
            int reserved2);

        [StructLayout(LayoutKind.Sequential)]
        internal struct FrameInfo
        {
            internal int Width;
            internal int Height;
            internal int Timestamp;
            internal int Type;
            internal int FrameRate;
            internal uint FrameNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct UserLoginInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 129)]
            internal byte[] DeviceAddress;

            internal byte UseTransport;
            internal ushort Port;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            internal byte[] UserName;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            internal byte[] Password;

            // 同步登录不使用回调。IntPtr 能明确保持 x64 原生函数指针的宽度。
            internal IntPtr LoginResultCallback;
            internal IntPtr UserData;
            internal int UseAsyncLogin;
            internal byte ProxyType;
            internal byte UseUtcTime;
            internal byte LoginMode;
            internal byte Https;
            internal int ProxyId;
            internal byte VerifyMode;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 119)]
            internal byte[] Reserved;

            internal static UserLoginInfo Create(
                string address, ushort port, string username, string password)
            {
                return new UserLoginInfo
                {
                    DeviceAddress = ToFixedAnsi(address, 129),
                    Port = port,
                    UserName = ToFixedAnsi(username, 64),
                    Password = ToFixedAnsi(password, 64),
                    LoginResultCallback = IntPtr.Zero,
                    UserData = IntPtr.Zero,
                    UseAsyncLogin = 0,
                    Reserved = new byte[119],
                };
            }

            internal static void ClearCredentials(ref UserLoginInfo loginInfo)
            {
                // The managed strings cannot be zeroed, but the fixed native-marshalling
                // buffers can be cleared immediately after NET_DVR_Login_V40 returns.
                if (loginInfo.UserName != null)
                    Array.Clear(loginInfo.UserName, 0, loginInfo.UserName.Length);
                if (loginInfo.Password != null)
                    Array.Clear(loginInfo.Password, 0, loginInfo.Password.Length);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PreviewInfo
        {
            internal int Channel;
            internal uint StreamType;
            internal uint LinkMode;
            internal IntPtr PlayWindow;
            internal uint Blocked;
            internal uint PassbackRecord;
            internal byte PreviewMode;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            internal byte[] StreamId;

            internal byte ProtocolType;
            internal byte Reserved1;
            internal byte VideoCodingType;
            internal uint DisplayBufferCount;
            internal byte NpqMode;
            internal byte ReceiveMetadata;
            internal byte DataType;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 213)]
            internal byte[] Reserved;

            internal static PreviewInfo Create(int channel, uint streamType, uint linkMode)
            {
                return new PreviewInfo
                {
                    Channel = channel,
                    StreamType = streamType,
                    LinkMode = linkMode,
                    PlayWindow = IntPtr.Zero,
                    Blocked = 0,
                    PassbackRecord = 0,
                    StreamId = new byte[32],
                    DisplayBufferCount = 1,
                    Reserved = new byte[213],
                };
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LocalSdkPath
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            internal byte[] Path;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            internal byte[] Reserved;
        }

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool NET_DVR_SetSDKInitCfg(int configType, IntPtr input);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool NET_DVR_Init();

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool NET_DVR_Cleanup();

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool NET_DVR_SetConnectTime(uint waitMilliseconds, uint attempts);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool NET_DVR_SetReconnect(
            uint intervalMilliseconds,
            [MarshalAs(UnmanagedType.Bool)] bool enabled);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint NET_DVR_GetSDKVersion();

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint NET_DVR_GetLastError();

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int NET_DVR_Login_V40(
            ref UserLoginInfo loginInfo,
            IntPtr deviceInfo);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool NET_DVR_Logout(int userId);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern int NET_DVR_RealPlay_V40(
            int userId,
            ref PreviewInfo previewInfo,
            RealDataCallback callback,
            IntPtr userData);

        [DllImport("HCNetSDK.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool NET_DVR_StopRealPlay(int realHandle);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_GetPort(ref int port);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_FreePort(int port);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_SetStreamOpenMode(int port, uint mode);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_OpenStream(
            int port, IntPtr fileHeader, uint headerSize, uint sourceBufferSize);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_InputData(int port, IntPtr buffer, uint bufferSize);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_CloseStream(int port);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_SetDisplayBuf(int port, uint frameCount);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_SetDecCallBackEx(
            int port, DecodeCallback callback, IntPtr destination, int destinationSize);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_Play(int port, IntPtr window);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PlayM4_Stop(int port);

        [DllImport("PlayCtrl.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern uint PlayM4_GetLastError(int port);

        internal static bool SetSdkRoot(string runtimeRoot, out uint error)
        {
            var path = new LocalSdkPath
            {
                Path = ToFixedAnsi(runtimeRoot, 256),
                Reserved = new byte[128],
            };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(LocalSdkPath)));
            try
            {
                Marshal.StructureToPtr(path, memory, false);
                bool success = NET_DVR_SetSDKInitCfg(InitCfgSdkPath, memory);
                error = success ? 0 : NET_DVR_GetLastError();
                return success;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        internal static bool SetSdkFile(int configType, string path, out uint error)
        {
            IntPtr memory = Marshal.StringToHGlobalAnsi(path);
            try
            {
                bool success = NET_DVR_SetSDKInitCfg(configType, memory);
                error = success ? 0 : NET_DVR_GetLastError();
                return success;
            }
            finally
            {
                Marshal.FreeHGlobal(memory);
            }
        }

        private static byte[] ToFixedAnsi(string value, int capacity)
        {
            byte[] result = new byte[capacity];
            if (string.IsNullOrEmpty(value)) return result;

            byte[] encoded = Encoding.Default.GetBytes(value);
            int length = Math.Min(encoded.Length, capacity - 1);
            Buffer.BlockCopy(encoded, 0, result, 0, length);
            return result;
        }
    }

    /// <summary>
    /// 只加载用户本地安装的海康运行库。使用绝对路径 LoadLibraryEx，避免修改进程级
    /// DLL 搜索目录后影响同进程中的 LibVLC 私有运行库。
    /// </summary>
    internal static class HikvisionSdkRuntime
    {
        private const uint LoadWithAlteredSearchPath = 0x00000008;
        private static readonly object Gate = new object();
        private static readonly List<IntPtr> ModuleHandles = new List<IntPtr>();
        private static int referenceCount;
        private static string loadedRoot = string.Empty;
        private static string preparedRoot = string.Empty;
        private static string version = "not loaded";

        private static readonly string[] RequiredFiles =
        {
            "HCNetSDK.dll",
            "HCCore.dll",
            "PlayCtrl.dll",
            "libcrypto-1_1-x64.dll",
            "libssl-1_1-x64.dll",
            "hlog.dll",
            "hpr.dll",
        };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(
            string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);

        internal static string Version
        {
            get
            {
                lock (Gate) return version;
            }
        }

        internal static bool TryAcquire(string runtimeRoot, out string error)
        {
            lock (Gate)
            {
                error = null;
                try
                {
                    string fullRoot = Path.GetFullPath(runtimeRoot);
                    if (referenceCount > 0)
                    {
                        if (!string.Equals(fullRoot, loadedRoot,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            error = "Hikvision SDK is already initialized from another path.";
                            return false;
                        }

                        referenceCount++;
                        return true;
                    }

                    if (!ValidateAndLoad(fullRoot, out error)) return false;
                    if (!ConfigurePaths(fullRoot, out error)) return false;
                    if (!HikvisionNative.NET_DVR_Init())
                    {
                        error = "NET_DVR_Init failed with SDK error " +
                                HikvisionNative.NET_DVR_GetLastError() + ".";
                        return false;
                    }

                    // 官方 SDK 的自动重连继续保留；播放器自己的帧停滞检测负责兜底重建会话。
                    HikvisionNative.NET_DVR_SetConnectTime(3000, 1);
                    HikvisionNative.NET_DVR_SetReconnect(10000, true);
                    loadedRoot = fullRoot;
                    version = NormalizeFileVersion(Path.Combine(fullRoot, "HCNetSDK.dll"));
                    referenceCount = 1;
                    return true;
                }
                catch (Exception exception)
                {
                    error = "Unable to initialize Hikvision SDK: " +
                            exception.GetType().Name + ".";
                    return false;
                }
            }
        }

        internal static bool Release(out uint cleanupError)
        {
            lock (Gate)
            {
                cleanupError = 0;
                if (referenceCount <= 0) return true;
                referenceCount--;
                if (referenceCount != 0) return true;

                bool cleaned = HikvisionNative.NET_DVR_Cleanup();
                if (!cleaned) cleanupError = HikvisionNative.NET_DVR_GetLastError();
                loadedRoot = string.Empty;
                version = "not loaded";
                // 模块句柄在进程结束前保留，防止原生回调代码被提前卸载。
                return cleaned;
            }
        }

        private static bool ValidateAndLoad(string root, out string error)
        {
            error = null;
            foreach (string file in RequiredFiles)
            {
                string path = Path.Combine(root, file);
                if (!File.Exists(path))
                {
                    error = "Hikvision runtime is incomplete: missing " + file +
                            ". Run scripts/setup-hikvision.ps1.";
                    return false;
                }
            }

            if (!Directory.Exists(Path.Combine(root, "HCNetSDKCom")))
            {
                error = "Hikvision runtime is missing HCNetSDKCom. " +
                        "Run scripts/setup-hikvision.ps1.";
                return false;
            }

            // LoadLibraryEx 的模块句柄会保留到进程结束；后续 Init/Cleanup 周期复用它们。
            if (ModuleHandles.Count > 0)
            {
                if (!string.Equals(root, preparedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    error = "Hikvision native modules were already loaded from " +
                            "another runtime directory. Restart Unity or the player " +
                            "before switching Hikvision SDK directories.";
                    return false;
                }
                return true;
            }

            // 先加载主 SDK，再加载 PlayCtrl；只有全部成功才提交句柄。
            // 这样第二个 DLL 失败时不会把半初始化状态留给下一次重试。
            var loadedThisAttempt = new List<IntPtr>();
            foreach (string file in new[] { "HCNetSDK.dll", "PlayCtrl.dll" })
            {
                IntPtr handle = LoadLibraryExW(
                    Path.Combine(root, file), IntPtr.Zero, LoadWithAlteredSearchPath);
                if (handle == IntPtr.Zero)
                {
                    int win32Error = Marshal.GetLastWin32Error();
                    for (int i = loadedThisAttempt.Count - 1; i >= 0; i--)
                        FreeLibrary(loadedThisAttempt[i]);
                    error = "Unable to load " + file + ". Win32 error " +
                            win32Error + ".";
                    return false;
                }
                loadedThisAttempt.Add(handle);
            }

            ModuleHandles.AddRange(loadedThisAttempt);
            preparedRoot = root;

            return true;
        }

        private static bool ConfigurePaths(string root, out string error)
        {
            uint sdkError;
            if (!HikvisionNative.SetSdkRoot(root, out sdkError))
            {
                error = "NET_DVR_SetSDKInitCfg(SDK_PATH) failed with SDK error " +
                        sdkError + ".";
                return false;
            }

            if (!HikvisionNative.SetSdkFile(
                    HikvisionNative.InitCfgLibCryptoPath,
                    Path.Combine(root, "libcrypto-1_1-x64.dll"), out sdkError))
            {
                error = "NET_DVR_SetSDKInitCfg(LIBCRYPTO) failed with SDK error " +
                        sdkError + ".";
                return false;
            }

            if (!HikvisionNative.SetSdkFile(
                    HikvisionNative.InitCfgLibSslPath,
                    Path.Combine(root, "libssl-1_1-x64.dll"), out sdkError))
            {
                error = "NET_DVR_SetSDKInitCfg(LIBSSL) failed with SDK error " +
                        sdkError + ".";
                return false;
            }

            error = null;
            return true;
        }

        private static string NormalizeFileVersion(string dllPath)
        {
            string fileVersion = System.Diagnostics.FileVersionInfo
                .GetVersionInfo(dllPath).FileVersion;
            return string.IsNullOrWhiteSpace(fileVersion)
                ? "unknown"
                : fileVersion.Replace(", ", ".").Replace(",", ".");
        }
    }
}
#endif
