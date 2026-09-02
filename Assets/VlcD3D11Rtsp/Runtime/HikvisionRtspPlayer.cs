#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace VlcD3D11Rtsp
{
    public enum HikvisionLinkMode
    {
        Tcp = 0,
        Udp = 1,
        Rtp = 3,
        RtpRtsp = 4,
        RtspHttp = 5,
        RtspHttps = 7,
    }

    /// <summary>
    /// Windows 海康官方 HCNetSDK + PlayM4 实时预览组件。
    /// HCNetSDK 负责登录和取流，PlayM4 输出 YV12；Unity 主线程只上传最新一帧，
    /// 不在海康回调线程调用任何 Unity API，也不建立无界帧队列。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class HikvisionRtspPlayer : MonoBehaviour
    {
        public const string DefaultEndpoint =
            "hikvision://admin@192.168.1.64:8000/1?stream=0";

        private const string AddressVariable = "HIKVISION_DEVICE_ADDRESS";
        private const string PortVariable = "HIKVISION_PORT";
        private const string UsernameVariable = "HIKVISION_USERNAME";
        private const string PasswordVariable = "HIKVISION_PASSWORD";
        private const string ChannelVariable = "HIKVISION_CHANNEL";
        private const string StreamVariable = "HIKVISION_STREAM_TYPE";

        [Header("Hikvision device")]
        [SerializeField] private string deviceAddress = "192.168.1.64";
        [SerializeField, Range(1, 65535)] private int devicePort = 8000;
        [SerializeField] private string username = "admin";
        // 密码只允许运行时通过 API 或环境变量注入，避免写进场景、Prefab 或 Git。
        [NonSerialized] private string password = string.Empty;
        [SerializeField, Min(1)] private int channel = 1;
        [Tooltip("0=main stream, 1=sub stream, 2=third stream.")]
        [SerializeField, Range(0, 10)] private int streamType;
        [SerializeField] private HikvisionLinkMode linkMode = HikvisionLinkMode.Tcp;

        [Header("Recovery")]
        [SerializeField] private bool reconnectOnFailure = true;
        [SerializeField, Min(1f)] private float firstFrameTimeoutSeconds = 15f;
        [SerializeField, Min(1f)] private float frameStallTimeoutSeconds = 15f;
        [SerializeField, Min(0.1f)] private float initialReconnectDelaySeconds = 1f;
        [SerializeField, Min(1f)] private float maximumReconnectDelaySeconds = 15f;
        [SerializeField] private bool runInBackground = true;

        [Header("Display")]
        [SerializeField] private RawImage targetImage;
        [SerializeField] private AspectRatioFitter aspectRatioFitter;
        [SerializeField] private bool manageAspectRatio = true;
        [SerializeField] private bool flipHorizontally;
        [SerializeField] private bool flipVertically;
        [SerializeField] private bool linearTexture = true;

        [Header("PlayM4")]
        [SerializeField, Min(1048576)] private int sourceBufferBytes = 4 * 1024 * 1024;
        [SerializeField, Range(1, 50)] private int displayBufferFrames = 1;

        private sealed class ConnectionSnapshot
        {
            internal string RuntimeRoot;
            internal string Address;
            internal ushort Port;
            internal string Username;
            internal string Password;
            internal int Channel;
            internal uint StreamType;
            internal uint LinkMode;
        }

        private sealed class LoginResult
        {
            internal ConnectionSnapshot Configuration;
            internal bool RuntimeAcquired;
            internal int UserId = -1;
            internal uint ErrorCode;
            internal string Error = string.Empty;
        }

        /// <summary>
        /// 每次预览各自持有原生回调委托和代次。Stop 会先禁用该会话并等待已进入的
        /// 回调退出，再释放 PlayM4 端口，避免旧回调在重连后误操作新端口。
        /// </summary>
        private sealed class CallbackSession
        {
            private readonly HikvisionRtspPlayer owner;

            internal CallbackSession(HikvisionRtspPlayer owner, int generation)
            {
                this.owner = owner;
                Generation = generation;
                RealData = HandleRealData;
                Decode = HandleDecodedFrame;
            }

            internal int Generation { get; }
            internal HikvisionNative.RealDataCallback RealData { get; }
            internal HikvisionNative.DecodeCallback Decode { get; }

            private void HandleRealData(
                int handle, uint dataType, IntPtr buffer, uint bufferSize, IntPtr userData)
            {
                owner.OnRealData(this, handle, dataType, buffer, bufferSize);
            }

            private void HandleDecodedFrame(
                int port,
                IntPtr buffer,
                int bufferSize,
                ref HikvisionNative.FrameInfo frame,
                int reserved1,
                int reserved2)
            {
                owner.OnDecodedFrame(this, port, buffer, bufferSize, ref frame);
            }
        }

        private readonly object decoderGate = new object();
        private readonly object callbackGate = new object();
        private readonly object frameGate = new object();
        private readonly ConcurrentQueue<string> nativeFailures =
            new ConcurrentQueue<string>();

        private CallbackSession activeCallbackSession;
        private int inFlightCallbacks;
        private bool callbacksAllowed;
        private Task<LoginResult> loginTask;
        private int loginTaskGeneration;
        private ConnectionSnapshot queuedLoginConfiguration;
        private int queuedLoginGeneration;
        private int generation;
        private int userId = -1;
        private int realHandle = -1;
        private int playPort = -1;
        private int invalidFrameReported;
        private bool sdkLeaseHeld;
        private bool playbackRequested;
        private bool applicationPaused;
        private bool focusLost;
        private bool destroying;
        private float scheduledRestartAt = float.PositiveInfinity;
        private float openingStartedAt;
        private float lastFrameAt;
        private int reconnectAttempt;

        // 两个数组在回调线程和主线程间交换：回调只覆盖“待显示”的最新帧。
        private byte[] pendingFrame;
        private byte[] uploadFrame;
        private int pendingWidth;
        private int pendingHeight;
        private int pendingFrameRate;
        private long pendingSerial;
        private long uploadedSerial;

        private Texture2D yTexture;
        private Texture2D uTexture;
        private Texture2D vTexture;
        private Material yv12Material;
        private Material originalMaterial;
        private int textureWidth;
        private int textureHeight;
        private long renderedFrameCount;
        private long inputDropCount;
        private bool hasFirstFrame;
        private string status = "Idle";
        private string lastError = string.Empty;

        public event Action FirstFrameReady;
        public event Action<string> PlaybackFailed;

        public string DeviceAddress
        {
            get => deviceAddress;
            set => deviceAddress = value ?? string.Empty;
        }

        public int DevicePort
        {
            get => devicePort;
            set => devicePort = Mathf.Clamp(value, 1, 65535);
        }

        public string Username
        {
            get => username;
            set => username = value ?? string.Empty;
        }

        public string Password
        {
            set => password = value ?? string.Empty;
        }

        public int Channel
        {
            get => channel;
            set => channel = Mathf.Max(1, value);
        }

        public int StreamType
        {
            get => streamType;
            set => streamType = Mathf.Clamp(value, 0, 10);
        }

        public string Endpoint => BuildEndpoint();
        public string Status => status;
        public string LastError => lastError;
        public bool HasFirstFrame => hasFirstFrame;
        public long RenderedFrameCount => renderedFrameCount;
        public Texture VideoTexture => yTexture;

        public string DiagnosticsSummary =>
            "backend=HikvisionSdk" +
            ", sdk=" + HikvisionSdkRuntime.Version +
            ", channel=" + channel +
            ", stream=" + streamType +
            ", login=" + (userId >= 0) +
            ", preview=" + (realHandle >= 0) +
            ", playPort=" + Volatile.Read(ref playPort) +
            ", frames=" + renderedFrameCount +
            ", inputDrops=" + Interlocked.Read(ref inputDropCount) +
            ", firstFrame=" + hasFirstFrame;

        /// <summary>供 Editor 自测核对当前官方 Win64 头文件的托管 ABI。</summary>
        public static bool ValidateNativeAbi(out string error)
        {
            Type login = typeof(HikvisionNative.UserLoginInfo);
            Type preview = typeof(HikvisionNative.PreviewInfo);
            Type frame = typeof(HikvisionNative.FrameInfo);
            bool valid = Marshal.SizeOf(login) == 416 &&
                         Marshal.OffsetOf(login, "LoginResultCallback").ToInt32() == 264 &&
                         Marshal.OffsetOf(login, "UseAsyncLogin").ToInt32() == 280 &&
                         Marshal.SizeOf(preview) == 288 &&
                         Marshal.OffsetOf(preview, "PlayWindow").ToInt32() == 16 &&
                         Marshal.OffsetOf(preview, "Blocked").ToInt32() == 24 &&
                         Marshal.OffsetOf(preview, "Reserved").ToInt32() == 75 &&
                         Marshal.SizeOf(frame) == 24;
            error = valid
                ? null
                : "Hikvision managed ABI does not match V6.1.9.48 Win64 headers.";
            return valid;
        }

        /// <summary>加载并初始化一次本地 SDK，不登录设备；用于换机后的依赖诊断。</summary>
        public static bool TryValidateInstalledRuntime(out string version, out string error)
        {
            version = "not loaded";
            if (!HikvisionSdkRuntime.TryAcquire(RuntimeRoot, out error)) return false;
            version = HikvisionSdkRuntime.Version;
            uint cleanupError;
            if (!HikvisionSdkRuntime.Release(out cleanupError))
            {
                error = "NET_DVR_Cleanup failed with SDK error " + cleanupError + ".";
                return false;
            }
            error = null;
            return true;
        }

        private void Awake()
        {
            if (targetImage == null) targetImage = GetComponent<RawImage>();
            if (aspectRatioFitter == null)
                aspectRatioFitter = GetComponent<AspectRatioFitter>();
            if (targetImage != null) originalMaterial = targetImage.material;

            if (runInBackground) Application.runInBackground = true;
            ApplyUvOrientation();
        }

        private void Update()
        {
            CompleteLoginIfReady();

            string nativeFailure;
            while (nativeFailures.TryDequeue(out nativeFailure))
                HandleFailure(nativeFailure);

            UploadLatestFrame();

            if (!float.IsPositiveInfinity(scheduledRestartAt) &&
                Time.realtimeSinceStartup >= scheduledRestartAt &&
                playbackRequested && !applicationPaused && !focusLost)
            {
                scheduledRestartAt = float.PositiveInfinity;
                StartLogin();
            }

            if (!playbackRequested || realHandle < 0 ||
                !float.IsPositiveInfinity(scheduledRestartAt)) return;

            float now = Time.realtimeSinceStartup;
            if (!hasFirstFrame && now - openingStartedAt >= firstFrameTimeoutSeconds)
                HandleFailure("Timed out waiting for the first Hikvision decoded frame.");
            else if (hasFirstFrame && now - lastFrameAt >= frameStallTimeoutSeconds)
                HandleFailure("Hikvision decoded frame stream stalled.");
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (!playbackRequested) return;

            if (paused)
            {
                status = "Suspended";
                InvalidateLoginRequests();
                StopActiveSession();
            }
            else if (!focusLost)
            {
                scheduledRestartAt = Time.realtimeSinceStartup + 0.35f;
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            // Windows 普通失焦不是挂起；后台运行开启时保持海康会话不动。
            if (runInBackground)
            {
                focusLost = false;
                return;
            }

            focusLost = !focused;
            if (!playbackRequested || applicationPaused) return;
            if (!focused)
            {
                InvalidateLoginRequests();
                StopActiveSession();
            }
            else
            {
                scheduledRestartAt = Time.realtimeSinceStartup + 0.35f;
            }
        }

        private void OnDestroy()
        {
            destroying = true;
            playbackRequested = false;
            InvalidateLoginRequests();
            StopActiveSession();
            DetachLoginForDestroy();
            password = string.Empty;
            DestroyDisplayResources();
        }

        /// <summary>使用当前 Inspector/API/环境变量配置异步登录并开始海康预览。</summary>
        public void Play()
        {
            playbackRequested = true;
            reconnectAttempt = 0;
            lastError = string.Empty;
            scheduledRestartAt = float.PositiveInfinity;
            StartLogin();
        }

        public void Stop()
        {
            playbackRequested = false;
            scheduledRestartAt = float.PositiveInfinity;
            reconnectAttempt = 0;
            InvalidateLoginRequests();
            StopActiveSession();
            status = "Stopped";
        }

        public void RestartPreferred()
        {
            if (!playbackRequested) playbackRequested = true;
            scheduledRestartAt = float.PositiveInfinity;
            StartLogin();
        }

        /// <summary>
        /// 解析不含密码的 Demo 端点：hikvision://user@host:8000/channel?stream=0。
        /// 密码只从 Password 属性或 HIKVISION_PASSWORD 获取，不序列化到场景。
        /// </summary>
        public bool TryConfigureEndpoint(string value, out string error)
        {
            error = null;
            Uri endpoint;
            if (!Uri.TryCreate(value, UriKind.Absolute, out endpoint) ||
                !endpoint.Scheme.Equals("hikvision", StringComparison.OrdinalIgnoreCase))
            {
                error = "Enter hikvision://user@device:8000/channel?stream=0.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                error = "Hikvision endpoint has no device address.";
                return false;
            }

            if (!string.IsNullOrEmpty(endpoint.UserInfo))
            {
                string decodedUser = Uri.UnescapeDataString(endpoint.UserInfo);
                if (decodedUser.Contains(":"))
                {
                    error = "Do not place the Hikvision password in the endpoint URL.";
                    return false;
                }
                username = decodedUser;
            }

            int parsedChannel;
            if (!int.TryParse(endpoint.AbsolutePath.Trim('/'), out parsedChannel) ||
                parsedChannel < 1)
            {
                error = "Hikvision endpoint channel must be a positive number.";
                return false;
            }

            int parsedStream = streamType;
            string query = endpoint.Query.TrimStart('?');
            foreach (string pair in query.Split(new[] { '&' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length == 2 &&
                    parts[0].Equals("stream", StringComparison.OrdinalIgnoreCase) &&
                    (!int.TryParse(parts[1], out parsedStream) ||
                     parsedStream < 0 || parsedStream > 10))
                {
                    error = "Hikvision stream must be between 0 and 10.";
                    return false;
                }
            }

            deviceAddress = endpoint.Host;
            devicePort = endpoint.IsDefaultPort ? 8000 : endpoint.Port;
            channel = parsedChannel;
            streamType = parsedStream;
            return true;
        }

        private void StartLogin()
        {
            int requestGeneration = ++generation;
            StopActiveSession();
            ClearFrameState();

            ConnectionSnapshot config;
            string validationError;
            if (!TryCaptureConfiguration(out config, out validationError))
            {
                HandleFailure(validationError);
                return;
            }

            ClearConfigurationSecret(queuedLoginConfiguration);
            queuedLoginConfiguration = config;
            queuedLoginGeneration = requestGeneration;
            if (loginTask != null)
            {
                // NET_DVR_Login_V40 是同步原生调用，不能安全强杀。只保留最新请求，
                // 等当前调用完成并清理后再启动，保证同一组件永远只有一个登录任务。
                status = "Waiting for previous Hikvision login";
                return;
            }

            BeginQueuedLogin();
        }

        private void CompleteLoginIfReady()
        {
            Task<LoginResult> task = loginTask;
            if (task == null || !task.IsCompleted) return;
            int completedGeneration = loginTaskGeneration;
            loginTask = null;

            if (task.IsFaulted)
            {
                // Observe the exception, but do not surface native paths or credentials.
                AggregateException ignored = task.Exception;
                if (completedGeneration == generation && playbackRequested)
                    HandleFailure("Hikvision login worker failed.");
                else
                    BeginQueuedLogin();
                return;
            }

            LoginResult result = task.Result;
            if (destroying || !playbackRequested || completedGeneration != generation)
            {
                RecordCleanupWarning(ReleaseLoginResult(result));
                BeginQueuedLogin();
                return;
            }

            if (result.UserId < 0)
            {
                string reason = string.IsNullOrEmpty(result.Error)
                    ? "NET_DVR_Login_V40 failed with SDK error " + result.ErrorCode + "."
                    : result.Error;
                HandleFailure(reason);
                return;
            }

            userId = result.UserId;
            sdkLeaseHeld = result.RuntimeAcquired;
            result.UserId = -1;
            result.RuntimeAcquired = false;
            StartPreview(result.Configuration);
        }

        private void BeginQueuedLogin()
        {
            if (loginTask != null || queuedLoginConfiguration == null || destroying ||
                !playbackRequested || applicationPaused || focusLost) return;

            ConnectionSnapshot config = queuedLoginConfiguration;
            loginTaskGeneration = queuedLoginGeneration;
            queuedLoginConfiguration = null;
            status = "Logging into Hikvision device";
            loginTask = Task.Run(() => Login(config));
        }

        private LoginResult Login(ConnectionSnapshot config)
        {
            var result = new LoginResult { Configuration = config };
            try
            {
                string runtimeError;
                if (!HikvisionSdkRuntime.TryAcquire(config.RuntimeRoot, out runtimeError))
                {
                    result.Error = runtimeError;
                    return result;
                }

                result.RuntimeAcquired = true;
                HikvisionNative.UserLoginInfo login =
                    HikvisionNative.UserLoginInfo.Create(
                        config.Address, config.Port, config.Username, config.Password);

                // 当前组件不读取设备信息，用足量输出缓冲避免复制旧版嵌套结构造成 ABI 错位。
                IntPtr deviceInfo = Marshal.AllocHGlobal(1024);
                try
                {
                    result.UserId = HikvisionNative.NET_DVR_Login_V40(ref login, deviceInfo);
                    if (result.UserId < 0)
                        result.ErrorCode = HikvisionNative.NET_DVR_GetLastError();
                }
                finally
                {
                    HikvisionNative.UserLoginInfo.ClearCredentials(ref login);
                    Marshal.FreeHGlobal(deviceInfo);
                }

                if (result.UserId < 0)
                {
                    uint cleanupError;
                    if (!HikvisionSdkRuntime.Release(out cleanupError) &&
                        string.IsNullOrEmpty(result.Error))
                    {
                        result.Error = "NET_DVR_Cleanup failed with SDK error " +
                                       cleanupError + ".";
                    }
                    result.RuntimeAcquired = false;
                }
            }
            catch (Exception exception)
            {
                result.Error = "Hikvision login failed: " + exception.GetType().Name + ".";
                if (result.RuntimeAcquired)
                {
                    uint cleanupError;
                    HikvisionSdkRuntime.Release(out cleanupError);
                    result.RuntimeAcquired = false;
                }
                result.UserId = -1;
            }
            finally
            {
                // Release the task's reference to the plaintext credential on every path,
                // including SDK-load and initialization failures before Login_V40.
                config.Password = string.Empty;
            }

            return result;
        }

        private void StartPreview(ConnectionSnapshot config)
        {
            var session = new CallbackSession(this, generation);
            lock (callbackGate)
            {
                activeCallbackSession = session;
                callbacksAllowed = true;
            }
            HikvisionNative.PreviewInfo preview = HikvisionNative.PreviewInfo.Create(
                config.Channel, config.StreamType, config.LinkMode);
            int handle = HikvisionNative.NET_DVR_RealPlay_V40(
                userId, ref preview, session.RealData, IntPtr.Zero);
            if (handle < 0)
            {
                uint sdkError = HikvisionNative.NET_DVR_GetLastError();
                HandleFailure("NET_DVR_RealPlay_V40 failed with SDK error " + sdkError + ".");
                return;
            }

            realHandle = handle;
            openingStartedAt = Time.realtimeSinceStartup;
            lastFrameAt = openingStartedAt;
            status = "Opening Hikvision native stream";
        }

        private void OnRealData(
            CallbackSession session,
            int handle,
            uint dataType,
            IntPtr buffer,
            uint bufferSize)
        {
            if (!TryEnterCallback(session)) return;
            try
            {
                if (buffer == IntPtr.Zero || bufferSize == 0) return;

                if (dataType == HikvisionNative.NetDvrSysHead)
                {
                    InitializeDecoder(session, buffer, bufferSize);
                    return;
                }

                int port = Volatile.Read(ref playPort);
                if (port < 0) return;
                if (!HikvisionNative.PlayM4_InputData(port, buffer, bufferSize))
                    Interlocked.Increment(ref inputDropCount);
            }
            catch (Exception exception)
            {
                nativeFailures.Enqueue(
                    "Hikvision real-data callback failed: " +
                    exception.GetType().Name + ".");
            }
            finally
            {
                ExitCallback();
            }
        }

        private void InitializeDecoder(
            CallbackSession session, IntPtr header, uint headerSize)
        {
            lock (decoderGate)
            {
                if (!IsCurrentCallbackSession(session) || playPort >= 0) return;

                int port = -1;
                if (!HikvisionNative.PlayM4_GetPort(ref port))
                {
                    nativeFailures.Enqueue("PlayM4_GetPort failed.");
                    return;
                }

                if (!HikvisionNative.PlayM4_SetStreamOpenMode(
                        port, HikvisionNative.StreamRealTime) ||
                    !HikvisionNative.PlayM4_OpenStream(
                        port, header, headerSize, (uint)sourceBufferBytes) ||
                    !HikvisionNative.PlayM4_SetDecCallBackEx(
                        port, session.Decode, IntPtr.Zero, 0))
                {
                    uint error = HikvisionNative.PlayM4_GetLastError(port);
                    ReleaseDecoderPort(port);
                    nativeFailures.Enqueue(
                        "Unable to initialize PlayM4 decoder. Error " + error + ".");
                    return;
                }

                HikvisionNative.PlayM4_SetDisplayBuf(port, (uint)displayBufferFrames);
                Volatile.Write(ref playPort, port);
                if (!HikvisionNative.PlayM4_Play(port, IntPtr.Zero))
                {
                    uint error = HikvisionNative.PlayM4_GetLastError(port);
                    Volatile.Write(ref playPort, -1);
                    ReleaseDecoderPort(port);
                    nativeFailures.Enqueue("PlayM4_Play failed. Error " + error + ".");
                }
            }
        }

        private void OnDecodedFrame(
            CallbackSession session,
            int port,
            IntPtr buffer,
            int bufferSize,
            ref HikvisionNative.FrameInfo frame)
        {
            if (!TryEnterCallback(session)) return;
            try
            {
                if (frame.Type != HikvisionNative.FrameTypeYv12 ||
                    buffer == IntPtr.Zero) return;

                int width = frame.Width;
                int height = frame.Height;
                long expectedLong = (long)width * height * 3 / 2;
                if (width <= 0 || height <= 0 || (width & 1) != 0 ||
                    (height & 1) != 0 || expectedLong > int.MaxValue ||
                    frameSizeMismatch(bufferSize, (int)expectedLong))
                {
                    if (Interlocked.Exchange(ref invalidFrameReported, 1) == 0)
                        nativeFailures.Enqueue(
                            "PlayM4 returned a non-compact or invalid YV12 frame. " +
                            "This callback API exposes no stride, so padded frames are rejected.");
                    return;
                }

                int expected = (int)expectedLong;
                lock (frameGate)
                {
                    if (pendingFrame == null || pendingFrame.Length != expected)
                        pendingFrame = new byte[expected];
                    Marshal.Copy(buffer, pendingFrame, 0, expected);
                    pendingWidth = width;
                    pendingHeight = height;
                    pendingFrameRate = frame.FrameRate;
                    pendingSerial++;
                }
            }
            catch (Exception exception)
            {
                nativeFailures.Enqueue(
                    "Hikvision decode callback failed: " +
                    exception.GetType().Name + ".");
            }
            finally
            {
                ExitCallback();
            }
        }

        private static bool frameSizeMismatch(int actual, int expected)
        {
            return actual != expected;
        }

        private void UploadLatestFrame()
        {
            byte[] frame;
            int width;
            int height;
            int frameRate;
            long serial;
            lock (frameGate)
            {
                if (pendingSerial == uploadedSerial || pendingFrame == null) return;
                byte[] previousUpload = uploadFrame;
                uploadFrame = pendingFrame;
                pendingFrame = previousUpload;
                frame = uploadFrame;
                width = pendingWidth;
                height = pendingHeight;
                frameRate = pendingFrameRate;
                serial = pendingSerial;
            }

            if (!EnsureDisplayResources(width, height)) return;

            int ySize = width * height;
            int chromaSize = ySize / 4;
            if (frame.Length < ySize + chromaSize * 2)
            {
                HandleFailure("Hikvision YV12 upload buffer is shorter than expected.");
                return;
            }

            try
            {
                yTexture.SetPixelData<byte>(frame, 0, 0);
                vTexture.SetPixelData<byte>(frame, 0, ySize);
                uTexture.SetPixelData<byte>(frame, 0, ySize + chromaSize);
                yTexture.Apply(false, false);
                vTexture.Apply(false, false);
                uTexture.Apply(false, false);
            }
            catch (Exception exception)
            {
                HandleFailure(
                    "Unable to upload Hikvision YV12 frame: " +
                    exception.GetType().Name + ".");
                return;
            }

            uploadedSerial = serial;
            renderedFrameCount++;
            lastFrameAt = Time.realtimeSinceStartup;
            status = "Playing Hikvision native stream" +
                     (frameRate > 0 ? " (" + frameRate + " fps source)" : string.Empty);
            if (!hasFirstFrame)
            {
                hasFirstFrame = true;
                reconnectAttempt = 0;
                lastError = string.Empty;
                RaiseFirstFrameReady();
            }
        }

        private bool EnsureDisplayResources(int width, int height)
        {
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.R8))
            {
                HandleFailure("This Direct3D device does not support R8 YUV plane textures.");
                return false;
            }

            if (yv12Material == null)
            {
                Shader shader = Resources.Load<Shader>("HikvisionYv12");
                if (shader == null)
                {
                    HandleFailure("HikvisionYv12 shader is missing from Resources.");
                    return false;
                }
                yv12Material = new Material(shader) { hideFlags = HideFlags.DontSave };
            }

            if (textureWidth != width || textureHeight != height || yTexture == null)
            {
                DestroyPlaneTextures();
                yTexture = CreatePlaneTexture("Hikvision Y", width, height);
                vTexture = CreatePlaneTexture("Hikvision V", width / 2, height / 2);
                uTexture = CreatePlaneTexture("Hikvision U", width / 2, height / 2);
                textureWidth = width;
                textureHeight = height;
                yv12Material.SetTexture("_VTex", vTexture);
                yv12Material.SetTexture("_UTex", uTexture);
            }

            if (targetImage != null)
            {
                targetImage.material = yv12Material;
                targetImage.texture = yTexture;
                targetImage.color = Color.white;
            }
            if (manageAspectRatio && aspectRatioFitter != null)
                aspectRatioFitter.aspectRatio = (float)width / height;
            ApplyUvOrientation();
            return true;
        }

        private Texture2D CreatePlaneTexture(string name, int width, int height)
        {
            var texture = new Texture2D(
                width, height, TextureFormat.R8, false, linearTexture)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            return texture;
        }

        private void HandleFailure(string reason)
        {
            if (string.IsNullOrEmpty(reason)) reason = "Unknown Hikvision playback error.";
            lastError = reason;
            RaisePlaybackFailed(reason);
            InvalidateLoginRequests();
            StopActiveSession();

            if (!playbackRequested || !reconnectOnFailure || destroying)
            {
                status = "Hikvision playback failed";
                return;
            }

            float delay = Mathf.Min(
                initialReconnectDelaySeconds * Mathf.Pow(2f, reconnectAttempt),
                maximumReconnectDelaySeconds);
            reconnectAttempt++;
            scheduledRestartAt = Time.realtimeSinceStartup + delay;
            status = "Hikvision reconnect in " + delay.ToString("0.0") + "s";
        }

        private void InvalidateLoginRequests()
        {
            generation++;
            ClearConfigurationSecret(queuedLoginConfiguration);
            queuedLoginConfiguration = null;
        }

        private void DetachLoginForDestroy()
        {
            Task<LoginResult> task = loginTask;
            loginTask = null;
            ClearConfigurationSecret(queuedLoginConfiguration);
            queuedLoginConfiguration = null;
            if (task != null)
                task.ContinueWith(CleanupDetachedLogin, TaskScheduler.Default);
        }

        private static void CleanupDetachedLogin(Task<LoginResult> task)
        {
            if (task.Status == TaskStatus.RanToCompletion)
                ReleaseLoginResult(task.Result);
            else if (task.IsFaulted)
            {
                AggregateException ignored = task.Exception;
            }
        }

        private static string ReleaseLoginResult(LoginResult result)
        {
            if (result == null) return null;
            ClearConfigurationSecret(result.Configuration);
            string warning = null;
            if (result.UserId >= 0)
            {
                if (!HikvisionNative.NET_DVR_Logout(result.UserId))
                    warning = "NET_DVR_Logout failed with SDK error " +
                              HikvisionNative.NET_DVR_GetLastError() + ".";
                result.UserId = -1;
            }
            if (result.RuntimeAcquired)
            {
                uint cleanupError;
                if (!HikvisionSdkRuntime.Release(out cleanupError))
                    warning = AppendWarning(warning,
                        "NET_DVR_Cleanup failed with SDK error " + cleanupError + ".");
                result.RuntimeAcquired = false;
            }
            return warning;
        }

        private void StopActiveSession()
        {
            CallbackSession stoppedSession;
            lock (callbackGate)
            {
                callbacksAllowed = false;
                stoppedSession = activeCallbackSession;
            }

            string warning = null;
            int handle = Interlocked.Exchange(ref realHandle, -1);
            if (handle >= 0 && !HikvisionNative.NET_DVR_StopRealPlay(handle))
                warning = "NET_DVR_StopRealPlay failed with SDK error " +
                          HikvisionNative.NET_DVR_GetLastError() + ".";

            // StopRealPlay prevents new data callbacks. Already-entered callbacks are allowed
            // to finish before PlayM4 resources are released.
            WaitForCallbacksToDrain();

            lock (decoderGate)
            {
                int port = Interlocked.Exchange(ref playPort, -1);
                if (port >= 0)
                    warning = AppendWarning(warning, ReleaseDecoderPort(port));
            }

            lock (callbackGate)
            {
                if (ReferenceEquals(activeCallbackSession, stoppedSession))
                    activeCallbackSession = null;
            }

            int login = Interlocked.Exchange(ref userId, -1);
            if (login >= 0 && !HikvisionNative.NET_DVR_Logout(login))
                warning = AppendWarning(warning,
                    "NET_DVR_Logout failed with SDK error " +
                    HikvisionNative.NET_DVR_GetLastError() + ".");
            if (sdkLeaseHeld)
            {
                sdkLeaseHeld = false;
                uint cleanupError;
                if (!HikvisionSdkRuntime.Release(out cleanupError))
                    warning = AppendWarning(warning,
                        "NET_DVR_Cleanup failed with SDK error " + cleanupError + ".");
            }

            RecordCleanupWarning(warning);

            if (targetImage != null)
            {
                if (targetImage.texture == yTexture) targetImage.texture = null;
                if (targetImage.material == yv12Material)
                    targetImage.material = originalMaterial;
            }
        }

        private static string ReleaseDecoderPort(int port)
        {
            string warning = null;
            if (!HikvisionNative.PlayM4_Stop(port))
                warning = "PlayM4_Stop failed with error " +
                          HikvisionNative.PlayM4_GetLastError(port) + ".";
            if (!HikvisionNative.PlayM4_CloseStream(port))
                warning = AppendWarning(warning,
                    "PlayM4_CloseStream failed with error " +
                    HikvisionNative.PlayM4_GetLastError(port) + ".");
            if (!HikvisionNative.PlayM4_FreePort(port))
                warning = AppendWarning(warning,
                    "PlayM4_FreePort failed with error " +
                    HikvisionNative.PlayM4_GetLastError(port) + ".");
            return warning;
        }

        private bool TryEnterCallback(CallbackSession session)
        {
            lock (callbackGate)
            {
                if (!callbacksAllowed ||
                    !ReferenceEquals(activeCallbackSession, session) ||
                    session.Generation != generation) return false;
                inFlightCallbacks++;
                return true;
            }
        }

        private bool IsCurrentCallbackSession(CallbackSession session)
        {
            lock (callbackGate)
                return callbacksAllowed &&
                       ReferenceEquals(activeCallbackSession, session) &&
                       session.Generation == generation;
        }

        private void ExitCallback()
        {
            lock (callbackGate)
            {
                inFlightCallbacks--;
                if (inFlightCallbacks == 0) Monitor.PulseAll(callbackGate);
            }
        }

        private void WaitForCallbacksToDrain()
        {
            lock (callbackGate)
            {
                while (inFlightCallbacks > 0) Monitor.Wait(callbackGate);
            }
        }

        private void RecordCleanupWarning(string warning)
        {
            if (string.IsNullOrEmpty(warning)) return;
            if (string.IsNullOrEmpty(lastError)) lastError = warning;
            if (!destroying) Debug.LogWarning("[HikvisionRtspPlayer] " + warning, this);
        }

        private static string AppendWarning(string existing, string addition)
        {
            if (string.IsNullOrEmpty(addition)) return existing;
            return string.IsNullOrEmpty(existing) ? addition : existing + " " + addition;
        }

        private void RaiseFirstFrameReady()
        {
            Action handlers = FirstFrameReady;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private void RaisePlaybackFailed(string reason)
        {
            Action<string> handlers = PlaybackFailed;
            if (handlers == null) return;
            foreach (Action<string> handler in handlers.GetInvocationList())
            {
                try { handler(reason); }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private static void ClearConfigurationSecret(ConnectionSnapshot config)
        {
            if (config != null) config.Password = string.Empty;
        }

        private bool TryCaptureConfiguration(
            out ConnectionSnapshot config, out string error)
        {
            string address = EnvironmentValue(AddressVariable, deviceAddress).Trim();
            string user = EnvironmentValue(UsernameVariable, username);
            string secret = EnvironmentValue(PasswordVariable, password);
            int port = EnvironmentInt(PortVariable, devicePort);
            int selectedChannel = EnvironmentInt(ChannelVariable, channel);
            int selectedStream = EnvironmentInt(StreamVariable, streamType);

            if (string.IsNullOrWhiteSpace(address))
            {
                config = null;
                error = "Hikvision device address is empty.";
                return false;
            }
            if (port < 1 || port > 65535 || selectedChannel < 1 ||
                selectedStream < 0 || selectedStream > 10)
            {
                config = null;
                error = "Hikvision port/channel/stream configuration is invalid.";
                return false;
            }
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(secret))
            {
                config = null;
                error = "Set Hikvision username/password through the runtime API or " +
                        UsernameVariable + "/" + PasswordVariable + ".";
                return false;
            }

            config = new ConnectionSnapshot
            {
                RuntimeRoot = RuntimeRoot,
                Address = address,
                Port = (ushort)port,
                Username = user,
                Password = secret,
                Channel = selectedChannel,
                StreamType = (uint)selectedStream,
                LinkMode = (uint)linkMode,
            };
            error = null;
            return true;
        }

        private void ClearFrameState()
        {
            lock (frameGate)
            {
                pendingSerial = 0;
                uploadedSerial = 0;
                pendingFrame = null;
                uploadFrame = null;
            }
            hasFirstFrame = false;
            renderedFrameCount = 0;
            Interlocked.Exchange(ref inputDropCount, 0);
            Interlocked.Exchange(ref invalidFrameReported, 0);
        }

        private void ApplyUvOrientation()
        {
            if (targetImage == null) return;
            targetImage.uvRect = new Rect(
                flipHorizontally ? 1f : 0f,
                flipVertically ? 1f : 0f,
                flipHorizontally ? -1f : 1f,
                flipVertically ? -1f : 1f);
        }

        private string BuildEndpoint()
        {
            string user = string.IsNullOrEmpty(username)
                ? string.Empty
                : Uri.EscapeDataString(username) + "@";
            return "hikvision://" + user + deviceAddress + ":" + devicePort + "/" +
                   channel + "?stream=" + streamType;
        }

        private static string EnvironmentValue(string name, string fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? fallback ?? string.Empty : value;
        }

        private static int EnvironmentInt(string name, int fallback)
        {
            int value;
            return int.TryParse(Environment.GetEnvironmentVariable(name), out value)
                ? value
                : fallback;
        }

        private static string RuntimeRoot
        {
            get
            {
#if UNITY_EDITOR_WIN
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                return Path.Combine(projectRoot ?? Application.dataPath,
                    "External", "HikvisionWindows");
#else
                return Path.Combine(Application.dataPath, "HikvisionWindows");
#endif
            }
        }

        private void DestroyDisplayResources()
        {
            if (targetImage != null)
            {
                if (targetImage.texture == yTexture) targetImage.texture = null;
                if (targetImage.material == yv12Material)
                    targetImage.material = originalMaterial;
            }
            DestroyPlaneTextures();
            if (yv12Material != null) Destroy(yv12Material);
            yv12Material = null;
        }

        private void DestroyPlaneTextures()
        {
            if (yTexture != null) Destroy(yTexture);
            if (uTexture != null) Destroy(uTexture);
            if (vTexture != null) Destroy(vTexture);
            yTexture = null;
            uTexture = null;
            vTexture = null;
            textureWidth = 0;
            textureHeight = 0;
        }
    }
}
#endif
