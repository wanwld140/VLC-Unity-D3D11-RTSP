#if UNITY_ANDROID && !UNITY_EDITOR
#define VLCUNITY_ANDROID
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
#define VLCUNITY_WINDOWS
#endif

#if VLCUNITY_ANDROID || VLCUNITY_WINDOWS
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp;
using UnityEngine;
using UnityEngine.UI;

namespace VlcD3D11Rtsp
{
    /// <summary>
    /// Windows x64 and Android ARM64 RTSP player with an explicit CPU, GPU,
    /// or automatic video path. GPU mode exposes LibVLC 4's native output
    /// texture directly to Unity through D3D11 or the Android render bridge.
    /// CPU mode uses LibVLC video callbacks and uploads completed RV32 frames.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class VlcRtspPlayer : MonoBehaviour
    {
        public const string DefaultTestUrl =
            "rtsp://stream.strba.sk:1935/strba/VYHLAD_JAZERO.stream";

        private enum PlaybackSignalKind
        {
            EncounteredError,
            EndReached,
        }

        private struct PlaybackSignal
        {
            internal int Session;
            internal PlaybackSignalKind Kind;
        }

        private static bool coreInitialized;

        [Header("Stream")]
        [SerializeField] private string url = DefaultTestUrl;
        [SerializeField] private bool playOnEnable;
        [SerializeField] private bool forceRtspTcp = true;
        [SerializeField, Range(0, 60000)] private int networkCachingMs = 500;
        [SerializeField] private bool disableAudio = true;
        [Tooltip("Prepares LibVLC when the scene starts so the first Play click does not load the runtime.")]
        [SerializeField] private bool warmUpRuntimeOnAwake = true;

        [Header("Decode / Output")]
        [SerializeField] private VlcDecodeMode decodeMode = VlcDecodeMode.Auto;
        [SerializeField] private bool autoFallbackToCpu = true;
        [SerializeField] private bool linearTexture = true;

        [Header("Recovery")]
        [SerializeField] private bool reconnectOnResumeOrFocus = true;
        [SerializeField, Min(0f)] private float resumeDelaySeconds = 0.35f;
        [SerializeField, Min(1f)] private float firstFrameTimeoutSeconds = 12f;
        [SerializeField, Min(1f)] private float frameStallTimeoutSeconds = 10f;
        [SerializeField, Min(0.1f)] private float initialReconnectDelaySeconds = 1f;
        [SerializeField, Min(1f)] private float maximumReconnectDelaySeconds = 15f;
        [Tooltip("Maximum time to wait for LibVLC's asynchronous Stopped event before cleanup continues.")]
        [SerializeField, Min(0.25f)] private float stopTransitionTimeoutSeconds = 2f;

        [Header("Display")]
        [SerializeField] private RawImage targetImage;
        [SerializeField] private AspectRatioFitter aspectRatioFitter;
        [SerializeField] private bool manageAspectRatio = true;
        [Tooltip("Mirrors the decoded texture left-to-right. The bundled VLC D3D11 output requires this.")]
        [SerializeField] private bool flipHorizontally = true;
        [Tooltip("Flips the decoded texture top-to-bottom. The bundled VLC outputs require this.")]
        [SerializeField] private bool flipVertically = true;
        [SerializeField] private bool runInBackground = true;

        private readonly ConcurrentQueue<PlaybackSignal> nativeSignals =
            new ConcurrentQueue<PlaybackSignal>();
        private readonly ConcurrentQueue<string> diagnosticMessages =
            new ConcurrentQueue<string>();

        private LibVLC libVlc;
        private MediaPlayer player;
        private Media media;
        private VlcCpuVideoBuffer cpuBuffer;
        private Texture2D videoTexture;
#if VLCUNITY_ANDROID
        private RenderTexture androidOutputTexture;
#endif
        private IntPtr externalTexturePointer;
        private Coroutine transitionCoroutine;
        private bool transitionRequested;
        private bool pendingStartAfterRelease;
        private VlcDecodeMode pendingAttemptMode;
        private string pendingCompletionStatus = string.Empty;
        private string activeRequestUrl = string.Empty;
        private VlcDecodeMode activeRequestMode = VlcDecodeMode.Auto;
        private bool playbackRequested;
        private long renderedFrameCount;
        private VlcDecodeMode currentAttemptMode;
        private VlcActiveVideoPath activeVideoPath;
        private int session;
        private int reconnectAttempt;
        private int hardwareConfirmedFlag;
        private float openingStartedAt;
        private float lastFrameAt;
        private float scheduledStartAt = float.PositiveInfinity;
        private bool hasFirstFrame;
        private bool autoFallbackUsed;
        private bool applicationPaused;
        private bool focusLost;
        private bool shuttingDown;
        private string status = "Idle";
        private string fallbackReason = string.Empty;
        private string lastError = string.Empty;
        private string lastDecoderDiagnostic = string.Empty;
        private string hardwareDecodeEvidence = string.Empty;

        public event Action FirstFrameReady;
        public event Action<string> PlaybackFailed;

        public string Url
        {
            get => url;
            set => url = value ?? string.Empty;
        }

        public VlcDecodeMode DecodeMode
        {
            get => decodeMode;
            set => decodeMode = value;
        }

        public VlcDecodeMode CurrentAttemptMode => currentAttemptMode;
        public VlcActiveVideoPath ActiveVideoPath => activeVideoPath;
        public bool HasFirstFrame => hasFirstFrame;
        public bool HardwareDecodeRequested => currentAttemptMode == VlcDecodeMode.Gpu;
        public bool HardwareDecodeConfirmed => HardwareDecodeRequested &&
                                               Volatile.Read(ref hardwareConfirmedFlag) == 1;
        public string HardwareDecodeEvidence => hardwareDecodeEvidence;
        public string FallbackReason => fallbackReason;
        public string LastError => lastError;
        public string Status => status;
        public Texture VideoTexture
        {
            get
            {
#if VLCUNITY_ANDROID
                return androidOutputTexture != null ? androidOutputTexture : videoTexture;
#else
                return videoTexture;
#endif
            }
        }
        public bool IsTransitioning => transitionCoroutine != null;
        public long RenderedFrameCount => renderedFrameCount;

        public string DiagnosticsSummary
        {
            get
            {
                string cpu = cpuBuffer == null ? "n/a" : cpuBuffer.Diagnostics;
                return "requested=" + decodeMode +
                       ", attempt=" + currentAttemptMode +
                       ", active=" + activeVideoPath +
                       ", transition=" + IsTransitioning +
                       ", frames=" + renderedFrameCount +
                       ", firstFrame=" + hasFirstFrame +
                       ", hardwareConfirmed=" + HardwareDecodeConfirmed +
                       ", hardwareEvidence=" + EmptyAsNone(hardwareDecodeEvidence) +
                       ", fallback=" + EmptyAsNone(fallbackReason) +
                       ", decoder=" + EmptyAsNone(lastDecoderDiagnostic) +
                       ", cpuCallbacks=" + cpu;
            }
        }

        private void Awake()
        {
            // Older generated sample scenes left these serialized references empty.
            // Resolve sibling UI components so decoded frames are still visible.
            if (targetImage == null) targetImage = GetComponent<RawImage>();
            if (aspectRatioFitter == null)
                aspectRatioFitter = GetComponent<AspectRatioFitter>();

            if (targetImage == null)
                Debug.LogWarning(
                    "[VLC RTSP] No RawImage target is assigned; decoded frames will not be visible.",
                    this);

            if (runInBackground) Application.runInBackground = true;
            ApplyUvOrientation();

            // The first Core.Initialize/new LibVLC call may load native DLLs and scan plug-ins.
            // Do it while the scene starts, rather than inside the user's first button click.
            if (warmUpRuntimeOnAwake) WarmUpRuntime();
        }

        private void OnEnable()
        {
            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            scheduledStartAt = float.PositiveInfinity;
            CancelTransition();
            ReleasePlayerImmediate();
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            CancelTransition();
            ReleasePlayerImmediate();

            if (libVlc != null)
            {
                libVlc.Log -= OnLibVlcLog;
                libVlc.Dispose();
                libVlc = null;
            }
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
        }

        private void Update()
        {
            DrainNativeSignals();
            DrainDiagnostics();

            if (!float.IsPositiveInfinity(scheduledStartAt) &&
                Time.realtimeSinceStartup >= scheduledStartAt &&
                !applicationPaused && !focusLost && isActiveAndEnabled)
            {
                scheduledStartAt = float.PositiveInfinity;
                QueuePlaybackTransition(
                    DetermineRetryMode(),
                    "Reconnect queued");
            }

            // A transition owns the player while it is stopping/disposing/starting.
            // Avoid texture callbacks against a player whose native renderer is retiring.
            if (transitionCoroutine != null) return;
            if (player == null) return;

            if (currentAttemptMode == VlcDecodeMode.Cpu)
                UpdateCpuFrame();
            else
                UpdateGpuFrame();

            float now = Time.realtimeSinceStartup;
            if (!hasFirstFrame && now - openingStartedAt >= firstFrameTimeoutSeconds)
            {
                HandlePlaybackFailure("Timed out waiting for the first decoded frame.");
            }
            else if (hasFirstFrame && now - lastFrameAt >= frameStallTimeoutSeconds)
            {
                HandlePlaybackFailure("The decoded video frame stream stalled.");
            }
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            if (!reconnectOnResumeOrFocus) return;

            if (paused)
            {
                status = "Suspended";
                CancelTransition();
                ReleasePlayerImmediate();
            }
            else if (!focusLost)
            {
                ScheduleResumeRestart();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
#if VLCUNITY_ANDROID
            // Android can report backgrounding/screen-off as focus loss before Pause.
            // Retire the old socket and renderer so resume always creates a new session.
            focusLost = !focused;
            if (!reconnectOnResumeOrFocus || applicationPaused) return;

            if (!focused)
            {
                status = "Focus lost";
                CancelTransition();
                ReleasePlayerImmediate();
            }
            else
            {
                ScheduleResumeRestart();
            }
#else
            // Windows window focus is not an application suspend. Keeping the player alive
            // avoids a full RTSP reconnect whenever the operator clicks another window.
            if (runInBackground)
            {
                focusLost = false;
                return;
            }

            focusLost = !focused;
            if (!reconnectOnResumeOrFocus || applicationPaused) return;

            if (!focused)
            {
                status = "Focus lost";
                CancelTransition();
                ReleasePlayerImmediate();
            }
            else
            {
                ScheduleResumeRestart();
            }
#endif
        }

        /// <summary>Starts a new session using the currently requested mode.</summary>
        public void Play()
        {
            RequestPlayback(false);
        }

        private void RequestPlayback(bool forceRestart)
        {
            string normalizedUrl = (url ?? string.Empty).Trim();
            bool sameRequest = playbackRequested &&
                               string.Equals(activeRequestUrl, normalizedUrl,
                                   StringComparison.Ordinal) &&
                               activeRequestMode == decodeMode;
            bool sessionIsOpeningOrPlaying = player != null || transitionCoroutine != null;
            if (!forceRestart && sameRequest && sessionIsOpeningOrPlaying &&
                string.IsNullOrEmpty(lastError))
            {
                // PLAY / APPLY is intentionally idempotent. Repeated clicks must not tear
                // down a healthy RTSP session and rebuild the native renderer.
                status = hasFirstFrame
                    ? "Already playing; settings unchanged"
                    : "Already opening; settings unchanged";
                return;
            }

            url = normalizedUrl;
            activeRequestUrl = normalizedUrl;
            activeRequestMode = decodeMode;
            playbackRequested = true;
            autoFallbackUsed = false;
            fallbackReason = string.Empty;
            lastError = string.Empty;
            reconnectAttempt = 0;
            Interlocked.Exchange(ref hardwareConfirmedFlag, 0);
            hardwareDecodeEvidence = string.Empty;
            scheduledStartAt = float.PositiveInfinity;
            QueuePlaybackTransition(
                DetermineInitialMode(),
                forceRestart ? "Restart queued" : "Play queued");
        }

        /// <summary>Stops playback and cancels automatic reconnect.</summary>
        public void Stop()
        {
            playbackRequested = false;
            scheduledStartAt = float.PositiveInfinity;
            reconnectAttempt = 0;
            QueueStopTransition("Stop queued", "Stopped");
        }

        /// <summary>Rebuilds the LibVLC media session and retries the preferred path.</summary>
        public void RestartPreferred()
        {
            RequestPlayback(true);
        }

        /// <summary>UI-friendly mode setter: 0 Auto, 1 CPU, 2 GPU.</summary>
        public void SetDecodeMode(int mode)
        {
            if (mode < 0 || mode > 2)
                throw new ArgumentOutOfRangeException(nameof(mode));
            decodeMode = (VlcDecodeMode)mode;
        }

        private VlcDecodeMode DetermineInitialMode()
        {
            return activeRequestMode == VlcDecodeMode.Auto
                ? VlcDecodeMode.Gpu
                : activeRequestMode;
        }

        private VlcDecodeMode DetermineRetryMode()
        {
            if (activeRequestMode == VlcDecodeMode.Auto)
                return autoFallbackUsed ? VlcDecodeMode.Cpu : VlcDecodeMode.Gpu;
            return activeRequestMode;
        }

        private void QueuePlaybackTransition(VlcDecodeMode attemptMode, string queuedStatus)
        {
            pendingStartAfterRelease = true;
            pendingAttemptMode = attemptMode;
            pendingCompletionStatus = string.Empty;
            transitionRequested = true;
            status = queuedStatus;
            EnsureTransitionCoroutine();
        }

        private void QueueStopTransition(string queuedStatus, string completionStatus)
        {
            pendingStartAfterRelease = false;
            pendingCompletionStatus = completionStatus;
            transitionRequested = true;
            status = queuedStatus;
            EnsureTransitionCoroutine();
        }

        private void EnsureTransitionCoroutine()
        {
            if (transitionCoroutine == null && isActiveAndEnabled && !shuttingDown)
                transitionCoroutine = StartCoroutine(ProcessTransitions());
        }

        /// <summary>
        /// Serializes stop/dispose/open work. A newer request replaces the pending one,
        /// but the current native player is always retired before another is created.
        /// </summary>
        private IEnumerator ProcessTransitions()
        {
            // StartCoroutine advances an iterator immediately until its first yield. Make
            // the button callback frame enqueue-only even when runtime warm-up is disabled.
            yield return null;

            try
            {
                while (transitionRequested && isActiveAndEnabled && !shuttingDown)
                {
                    bool shouldStart = pendingStartAfterRelease;
                    VlcDecodeMode attemptMode = pendingAttemptMode;
                    string completionStatus = pendingCompletionStatus;
                    transitionRequested = false;

                    yield return ReleasePlayerDeferred();

                    // PLAY may have been clicked again with different settings while the old
                    // native player was stopping. Only the newest queued request may start.
                    if (transitionRequested) continue;

                    if (!shouldStart)
                    {
                        status = string.IsNullOrEmpty(completionStatus)
                            ? "Stopped"
                            : completionStatus;
                        continue;
                    }

                    yield return StartAttemptDeferred(attemptMode);
                }
            }
            finally
            {
                // Never leave a stale Coroutine handle behind if native cleanup throws.
                transitionCoroutine = null;
            }
        }

        private IEnumerator StartAttemptDeferred(VlcDecodeMode attemptMode)
        {
            if (shuttingDown || !isActiveAndEnabled || applicationPaused || focusLost)
                yield break;

            currentAttemptMode = attemptMode;
            activeVideoPath = VlcActiveVideoPath.None;
            hasFirstFrame = false;
            renderedFrameCount = 0;
            externalTexturePointer = IntPtr.Zero;
            lastDecoderDiagnostic = string.Empty;
            Interlocked.Exchange(ref hardwareConfirmedFlag, 0);
            hardwareDecodeEvidence = string.Empty;

            Uri streamUri;
            string validationError;
            if (!TryValidateRtspUrl(activeRequestUrl, out streamUri, out validationError))
            {
                FailWithoutRetry(validationError);
                yield break;
            }

            status = "Preparing " + attemptMode + " path";
            string initializationError;
            if (!TryInitializeLibVlc(out initializationError))
            {
                FailWithoutRetry(initializationError);
                yield break;
            }

            // Keep runtime initialization, MediaPlayer construction, and Play out of one
            // frame. This removes the compound spike seen by the UI button click.
            yield return null;
            if (transitionRequested) yield break;

            int newSession = ++session;
            try
            {
#if VLCUNITY_WINDOWS
                VlcNativeBridge.PrepareNextMediaPlayer(attemptMode);
#endif
                player = new MediaPlayer(libVlc);
            }
            catch (Exception exception)
            {
                FailAttemptStart(attemptMode, exception);
                yield break;
            }

            yield return null;
            if (transitionRequested) yield break;

            try
            {
                ConfigureMediaPlayer(attemptMode, newSession);
            }
            catch (Exception exception)
            {
                FailAttemptStart(attemptMode, exception);
                yield break;
            }

            yield return null;
            if (transitionRequested) yield break;

            try
            {
                media = new Media(streamUri);
                openingStartedAt = Time.realtimeSinceStartup;
                lastFrameAt = openingStartedAt;
                status = "Opening " + attemptMode + " path";

                if (!player.Play(media))
                    throw new InvalidOperationException("LibVLC rejected the playback request.");
            }
            catch (Exception exception)
            {
                FailAttemptStart(attemptMode, exception);
            }
        }

        private void ConfigureMediaPlayer(VlcDecodeMode attemptMode, int newSession)
        {
            if (attemptMode == VlcDecodeMode.Gpu)
            {
#if VLCUNITY_WINDOWS
                if (!VlcNativeBridge.IsD3D11Renderer())
                    throw new InvalidOperationException(
                        "GPU mode requires Unity to run with Direct3D 11. Active renderer: " +
                        VlcNativeBridge.RendererDescription() + ".");
                if (!VlcNativeBridge.HasNativeRenderer(player))
                    throw new InvalidOperationException(
                        "The media player did not receive a native D3D11 renderer.");
#elif VLCUNITY_ANDROID
                if (!VLCAndroidInitialization.EnsureInitialized())
                    throw new InvalidOperationException(
                        "The Android native texture renderer did not initialize.");
#endif

                player.EnableHardwareDecoding = true;
            }
            else
            {
                player.EnableHardwareDecoding = false;
                cpuBuffer = new VlcCpuVideoBuffer();
                player.SetVideoCallbacks(
                    cpuBuffer.LockCallback,
                    cpuBuffer.UnlockCallback,
                    cpuBuffer.DisplayCallback);
                player.SetVideoFormatCallbacks(cpuBuffer.FormatCallback, null);
            }

            player.NetworkCaching = (uint)Mathf.Clamp(networkCachingMs, 0, 60000);
            if (disableAudio) player.Mute = true;
            player.EncounteredError += delegate
            {
                nativeSignals.Enqueue(new PlaybackSignal
                {
                    Session = newSession,
                    Kind = PlaybackSignalKind.EncounteredError,
                });
            };
            player.Stopping += delegate
            {
                nativeSignals.Enqueue(new PlaybackSignal
                {
                    Session = newSession,
                    Kind = PlaybackSignalKind.EndReached,
                });
            };
        }

        private void FailAttemptStart(VlcDecodeMode attemptMode, Exception exception)
        {
            string reason = VlcLogSanitizer.Sanitize(
                "Unable to start the " + attemptMode + " path: " + exception.Message);
            HandlePlaybackFailure(reason);
        }

        private void WarmUpRuntime()
        {
            string error;
            if (TryInitializeLibVlc(out error))
            {
                if (status == "Idle") status = "Ready";
                return;
            }

            lastError = error;
            status = "Runtime warm-up failed";
            Debug.LogWarning("[VLC RTSP] " + VlcLogSanitizer.Sanitize(error), this);
        }

        private bool TryInitializeLibVlc(out string error)
        {
            error = null;
            string runtimeBasePath;
#if VLCUNITY_ANDROID
            runtimeBasePath = Application.dataPath;
            if (!VLCAndroidInitialization.EnsureInitialized())
            {
                error = "VLC Android native rendering plug-in initialization failed.";
                return false;
            }
#else
            if (!VlcWindowsRuntime.TryPrepare(out runtimeBasePath, out error))
                return false;
#endif

            try
            {
                if (!coreInitialized)
                {
                    Core.Initialize(runtimeBasePath);
                    coreInitialized = true;
                }

#if VLCUNITY_WINDOWS
                if (!VlcNativeBridge.TryInitialize(out error)) return false;
#endif

                if (libVlc == null)
                {
                    var arguments = new List<string>
                    {
                        "--no-video-title-show",
                        "-vv",
                    };
                    if (forceRtspTcp) arguments.Add("--rtsp-tcp");
                    if (disableAudio) arguments.Add("--no-audio");
#if VLCUNITY_ANDROID
                    arguments.Add("--network-caching=" +
                                  Mathf.Clamp(networkCachingMs, 0, 60000));
#endif
                    libVlc = new LibVLC(false, arguments.ToArray());
                    libVlc.Log += OnLibVlcLog;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "LibVLC initialization failed: " + exception.GetType().Name +
                        ": " + VlcLogSanitizer.Sanitize(exception.Message);
                return false;
            }
        }

        private void UpdateGpuFrame()
        {
            uint width = 0;
            uint height = 0;
            if (!player.Size(0, ref width, ref height) || width == 0 || height == 0)
                return;

#if VLCUNITY_ANDROID
            if (videoTexture == null || videoTexture.width != (int)width ||
                videoTexture.height != (int)height)
            {
                DestroyVideoTexture();
                videoTexture = AndroidTextureHelper.CreateNativeTexture(
                    player, linearTexture);
                if (videoTexture == null) return;

                videoTexture.name = "VLC Android Native RTSP Frame";
                videoTexture.wrapMode = TextureWrapMode.Clamp;
                videoTexture.filterMode = FilterMode.Bilinear;
                androidOutputTexture = new RenderTexture(
                    videoTexture.width,
                    videoTexture.height,
                    0,
                    RenderTextureFormat.ARGB32)
                {
                    name = "VLC Android RTSP Display",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                androidOutputTexture.Create();
                if (targetImage != null)
                {
                    targetImage.texture = androidOutputTexture;
                    targetImage.enabled = true;
                }
                if (manageAspectRatio && aspectRatioFitter != null && height != 0)
                    aspectRatioFitter.aspectRatio = (float)width / height;
                ApplyUvOrientation();
            }

            if (!AndroidTextureHelper.UpdateTexture(videoTexture, player)) return;
            Graphics.Blit(videoTexture, androidOutputTexture);
            MarkFrame(VlcActiveVideoPath.AndroidNativeTexture, width, height);
#else
            bool updated;
            IntPtr pointer = player.GetTexture(width, height, out updated);
            if (!updated || pointer == IntPtr.Zero) return;

            if (videoTexture == null || videoTexture.width != (int)width ||
                videoTexture.height != (int)height)
            {
                DestroyVideoTexture();
                videoTexture = Texture2D.CreateExternalTexture(
                    (int)width,
                    (int)height,
                    TextureFormat.RGBA32,
                    false,
                    linearTexture,
                    pointer);
                videoTexture.name = "VLC D3D11 Native RTSP Frame";
                videoTexture.wrapMode = TextureWrapMode.Clamp;
                videoTexture.filterMode = FilterMode.Bilinear;
                externalTexturePointer = pointer;
                BindTexture(width, height);
            }
            else if (externalTexturePointer != pointer)
            {
                videoTexture.UpdateExternalTexture(pointer);
                externalTexturePointer = pointer;
            }

            MarkFrame(VlcActiveVideoPath.D3D11NativeTexture, width, height);
#endif
        }

        private void UpdateCpuFrame()
        {
            if (cpuBuffer == null) return;

            byte[] pixels;
            int width;
            int height;
            if (!cpuBuffer.TryCopyLatestFrame(out pixels, out width, out height))
            {
                if (!string.IsNullOrEmpty(cpuBuffer.LastError))
                    lastDecoderDiagnostic = cpuBuffer.LastError;
                return;
            }

            if (videoTexture == null || videoTexture.width != width ||
                videoTexture.height != height)
            {
                DestroyVideoTexture();
                videoTexture = new Texture2D(
                    width, height, TextureFormat.ARGB32, false, linearTexture)
                {
                    name = "VLC CPU RTSP Frame",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                BindTexture((uint)width, (uint)height);
            }

            videoTexture.LoadRawTextureData(pixels);
            videoTexture.Apply(false, false);
            MarkFrame(VlcActiveVideoPath.CpuMemoryBuffer, (uint)width, (uint)height);
        }

        private void BindTexture(uint width, uint height)
        {
            if (targetImage != null)
            {
                targetImage.texture = videoTexture;
                targetImage.enabled = true;
            }

            if (manageAspectRatio && aspectRatioFitter != null && height != 0)
                aspectRatioFitter.aspectRatio = (float)width / height;
            ApplyUvOrientation();
        }

        private void MarkFrame(VlcActiveVideoPath path, uint width, uint height)
        {
            lastFrameAt = Time.realtimeSinceStartup;
            activeVideoPath = path;
            renderedFrameCount++;
            if (hasFirstFrame) return;

            hasFirstFrame = true;
            reconnectAttempt = 0;
            lastError = string.Empty;
            status = "Playing " + path + " " + width + "x" + height;
            Debug.Log("[VLC RTSP] First frame: " + DiagnosticsSummary, this);
            FirstFrameReady?.Invoke();
        }

        private void DrainNativeSignals()
        {
            PlaybackSignal signal;
            while (nativeSignals.TryDequeue(out signal))
            {
                if (signal.Session != session || player == null) continue;
                HandlePlaybackFailure(signal.Kind == PlaybackSignalKind.EndReached
                    ? "The RTSP stream ended."
                    : "LibVLC reported a playback error.");
            }
        }

        private void OnLibVlcLog(object sender, LogEventArgs args)
        {
            string module = args.Module ?? string.Empty;
            string message = VlcLogSanitizer.Sanitize(args.Message);
#if VLCUNITY_WINDOWS
            if (currentAttemptMode == VlcDecodeMode.Gpu &&
                VlcLogSanitizer.IsHardwareDecoderEvidence(module, message))
            {
                hardwareDecodeEvidence = module + ": " + message;
                Interlocked.Exchange(ref hardwareConfirmedFlag, 1);
            }
#endif
            if (VlcLogSanitizer.IsRelevantDiagnostic(module, message))
                diagnosticMessages.Enqueue(module + ": " + message);
        }

        private void DrainDiagnostics()
        {
            string diagnostic;
            while (diagnosticMessages.TryDequeue(out diagnostic))
                lastDecoderDiagnostic = diagnostic;
        }

        private void HandlePlaybackFailure(string reason)
        {
            if (shuttingDown) return;
            reason = VlcLogSanitizer.Sanitize(reason);
            lastError = reason;

            bool canFallback = activeRequestMode == VlcDecodeMode.Auto &&
                               currentAttemptMode == VlcDecodeMode.Gpu &&
                               autoFallbackToCpu && !autoFallbackUsed;
            if (canFallback)
            {
                autoFallbackUsed = true;
                fallbackReason = reason;
                Debug.LogWarning("[VLC RTSP] Auto fallback: " + reason, this);
                QueuePlaybackTransition(
                    VlcDecodeMode.Cpu,
                    "GPU unavailable; CPU fallback queued");
                return;
            }

            PlaybackFailed?.Invoke(reason);
            ScheduleReconnect(reason);
        }

        private void FailWithoutRetry(string reason)
        {
            playbackRequested = false;
            lastError = VlcLogSanitizer.Sanitize(reason);
            status = "Configuration error";
            scheduledStartAt = float.PositiveInfinity;
            Debug.LogError("[VLC RTSP] " + lastError, this);
            PlaybackFailed?.Invoke(lastError);
            QueueStopTransition("Configuration error", "Configuration error");
        }

        private void ScheduleReconnect(string reason)
        {
            if (!isActiveAndEnabled || applicationPaused || focusLost || shuttingDown)
                return;

            reconnectAttempt++;
            float exponent = Mathf.Pow(2f, Mathf.Min(reconnectAttempt - 1, 10));
            float delay = Mathf.Min(
                maximumReconnectDelaySeconds,
                initialReconnectDelaySeconds * exponent);
            scheduledStartAt = Time.realtimeSinceStartup + delay;
            string reconnectStatus = "Reconnect in " + delay.ToString("0.0") + "s";
            Debug.LogWarning("[VLC RTSP] " + reason + " " + reconnectStatus + ".", this);
            QueueStopTransition("Stopping before reconnect", reconnectStatus);
        }

        private void ScheduleResumeRestart()
        {
            if (!playbackRequested || !isActiveAndEnabled || shuttingDown) return;
            autoFallbackUsed = false;
            fallbackReason = string.Empty;
            reconnectAttempt = 0;
            scheduledStartAt = Time.realtimeSinceStartup + resumeDelaySeconds;
            status = "Resume rebuild scheduled";
        }

        private IEnumerator ReleasePlayerDeferred()
        {
            ++session;
            activeVideoPath = VlcActiveVideoPath.None;
            hasFirstFrame = false;
            externalTexturePointer = IntPtr.Zero;

            if (player != null)
            {
                status = "Stopping previous session";
                Task<bool> stopTask = null;
                try
                {
                    // StopAsync subscribes to Stopped and calls LibVLC4's non-blocking
                    // libvlc_media_player_stop_async. Dispose is deliberately delayed.
                    stopTask = player.StopAsync();
                }
                catch (Exception exception)
                {
                    lastDecoderDiagnostic = "Stop request failed: " +
                                            exception.GetType().Name + ".";
                }

                if (stopTask != null)
                {
                    float stopDeadline = Time.realtimeSinceStartup +
                                         Mathf.Max(0.25f, stopTransitionTimeoutSeconds);
                    while (!stopTask.IsCompleted &&
                           Time.realtimeSinceStartup < stopDeadline)
                    {
                        yield return null;
                    }

                    if (!stopTask.IsCompleted)
                    {
                        lastDecoderDiagnostic = "Timed out waiting for LibVLC Stopped event.";
                        Debug.LogWarning("[VLC RTSP] " + lastDecoderDiagnostic, this);
                    }
                    else if (stopTask.IsFaulted && stopTask.Exception != null)
                    {
                        lastDecoderDiagnostic = "Async stop failed: " +
                                                stopTask.Exception.GetBaseException()
                                                    .GetType().Name + ".";
                    }
                }

                try
                {
                    player.Dispose();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("MediaPlayer dispose", exception);
                }
                finally
                {
                    player = null;
                }

#if VLCUNITY_WINDOWS
                try
                {
                    VlcNativeBridge.QueueRendererCleanup();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("Renderer cleanup request", exception);
                }
#endif

                // Give Unity's render thread one frame to retire the native renderer
                // before destroying its external Texture2D wrapper.
                yield return null;
            }

            if (media != null)
            {
                try
                {
                    media.Dispose();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("Media dispose", exception);
                }
                finally
                {
                    media = null;
                }
                yield return null;
            }

            if (cpuBuffer != null)
            {
                try
                {
                    cpuBuffer.Dispose();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("CPU video buffer dispose", exception);
                }
                finally
                {
                    cpuBuffer = null;
                }
            }

            DestroyVideoTexture();
            yield return null;
        }

        private void RecordCleanupWarning(string operation, Exception exception)
        {
            lastDecoderDiagnostic = operation + " failed: " +
                                    exception.GetType().Name + ".";
            Debug.LogWarning("[VLC RTSP] " + lastDecoderDiagnostic, this);
        }

        private void CancelTransition()
        {
            transitionRequested = false;
            if (transitionCoroutine == null) return;
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        /// <summary>
        /// Synchronous cleanup is reserved for disable/destroy/suspend, where Unity may
        /// stop advancing coroutines. User-facing Play/Stop transitions use the deferred path.
        /// </summary>
        private void ReleasePlayerImmediate()
        {
            ++session;

            if (player != null)
            {
                try
                {
                    player.Stop();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("Immediate stop", exception);
                }

                try
                {
                    player.Dispose();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("Immediate player dispose", exception);
                }
                finally
                {
                    player = null;
                }

#if VLCUNITY_WINDOWS
                try
                {
                    VlcNativeBridge.QueueRendererCleanup();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("Immediate renderer cleanup", exception);
                }
#endif
            }

            if (media != null)
            {
                try
                {
                    media.Dispose();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("Immediate media dispose", exception);
                }
                finally
                {
                    media = null;
                }
            }

            if (cpuBuffer != null)
            {
                try
                {
                    cpuBuffer.Dispose();
                }
                catch (Exception exception)
                {
                    RecordCleanupWarning("Immediate CPU video buffer dispose", exception);
                }
                finally
                {
                    cpuBuffer = null;
                }
            }

            DestroyVideoTexture();
            activeVideoPath = VlcActiveVideoPath.None;
            hasFirstFrame = false;
            externalTexturePointer = IntPtr.Zero;
        }

        private void DestroyVideoTexture()
        {
#if VLCUNITY_ANDROID
            if (androidOutputTexture != null)
            {
                if (targetImage != null && targetImage.texture == androidOutputTexture)
                    targetImage.texture = null;
                if (RenderTexture.active == androidOutputTexture)
                    RenderTexture.active = null;
                androidOutputTexture.Release();
                Destroy(androidOutputTexture);
                androidOutputTexture = null;
            }
#endif
            if (videoTexture == null) return;
            if (targetImage != null && targetImage.texture == videoTexture)
                targetImage.texture = null;
            Destroy(videoTexture);
            videoTexture = null;
        }

        private void ApplyUvOrientation()
        {
            if (targetImage == null) return;
            float x = flipHorizontally ? 1f : 0f;
            float y = flipVertically ? 1f : 0f;
            float width = flipHorizontally ? -1f : 1f;
            float height = flipVertically ? -1f : 1f;
            targetImage.uvRect = new Rect(x, y, width, height);
        }

        private static bool TryValidateRtspUrl(
            string value, out Uri streamUri, out string error)
        {
            streamUri = null;
            error = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out streamUri) ||
                (!streamUri.Scheme.Equals("rtsp", StringComparison.OrdinalIgnoreCase) &&
                 !streamUri.Scheme.Equals("rtsps", StringComparison.OrdinalIgnoreCase)))
            {
                error = "Enter an absolute rtsp:// or rtsps:// URL.";
                return false;
            }
            return true;
        }

        private static string EmptyAsNone(string value)
        {
            return string.IsNullOrEmpty(value) ? "none" : value;
        }
    }
}
#endif
