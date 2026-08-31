#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using LibVLCSharp;
using UnityEngine;
using UnityEngine.UI;

namespace VlcD3D11Rtsp
{
    /// <summary>
    /// Windows RTSP player with an explicit CPU, GPU, or automatic video path.
    /// GPU mode exposes LibVLC 4's D3D11 output texture directly to Unity.
    /// CPU mode uses LibVLC video callbacks and uploads completed RV32 frames.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class VlcRtspPlayer : MonoBehaviour
    {
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
        [SerializeField] private string url = "rtsp://127.0.0.1:8554/live";
        [SerializeField] private bool playOnEnable;
        [SerializeField] private bool forceRtspTcp = true;
        [SerializeField, Range(0, 60000)] private int networkCachingMs = 500;
        [SerializeField] private bool disableAudio = true;

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

        [Header("Display")]
        [SerializeField] private RawImage targetImage;
        [SerializeField] private AspectRatioFitter aspectRatioFitter;
        [SerializeField] private bool manageAspectRatio = true;
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
        private IntPtr externalTexturePointer;
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
        public Texture VideoTexture => videoTexture;

        public string DiagnosticsSummary
        {
            get
            {
                string cpu = cpuBuffer == null ? "n/a" : cpuBuffer.Diagnostics;
                return "requested=" + decodeMode +
                       ", attempt=" + currentAttemptMode +
                       ", active=" + activeVideoPath +
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
            if (targetImage == null) targetImage = GetComponent<RawImage>();
            if (aspectRatioFitter == null)
                aspectRatioFitter = GetComponent<AspectRatioFitter>();

            if (runInBackground) Application.runInBackground = true;
            ApplyUvOrientation();
        }

        private void OnEnable()
        {
            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            scheduledStartAt = float.PositiveInfinity;
            ReleasePlayer();
        }

        private void OnDestroy()
        {
            shuttingDown = true;
            ReleasePlayer();

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
                StartAttempt(DetermineRetryMode());
            }

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
                ReleasePlayer();
            }
            else if (!focusLost)
            {
                ScheduleResumeRestart();
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            focusLost = !focused;
            if (!reconnectOnResumeOrFocus || applicationPaused) return;

            if (!focused)
            {
                status = "Focus lost";
                ReleasePlayer();
            }
            else
            {
                ScheduleResumeRestart();
            }
        }

        /// <summary>Starts a new session using the currently requested mode.</summary>
        public void Play()
        {
            autoFallbackUsed = false;
            fallbackReason = string.Empty;
            lastError = string.Empty;
            reconnectAttempt = 0;
            Interlocked.Exchange(ref hardwareConfirmedFlag, 0);
            hardwareDecodeEvidence = string.Empty;
            scheduledStartAt = float.PositiveInfinity;
            StartAttempt(DetermineInitialMode());
        }

        /// <summary>Stops playback and cancels automatic reconnect.</summary>
        public void Stop()
        {
            scheduledStartAt = float.PositiveInfinity;
            reconnectAttempt = 0;
            status = "Stopped";
            ReleasePlayer();
        }

        /// <summary>Rebuilds the LibVLC media session and retries the preferred path.</summary>
        public void RestartPreferred()
        {
            Play();
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
            return decodeMode == VlcDecodeMode.Auto ? VlcDecodeMode.Gpu : decodeMode;
        }

        private VlcDecodeMode DetermineRetryMode()
        {
            if (decodeMode == VlcDecodeMode.Auto)
                return autoFallbackUsed ? VlcDecodeMode.Cpu : VlcDecodeMode.Gpu;
            return decodeMode;
        }

        private void StartAttempt(VlcDecodeMode attemptMode)
        {
            if (shuttingDown || !isActiveAndEnabled || applicationPaused || focusLost)
                return;

            ReleasePlayer();
            currentAttemptMode = attemptMode;
            activeVideoPath = VlcActiveVideoPath.None;
            hasFirstFrame = false;
            externalTexturePointer = IntPtr.Zero;
            lastDecoderDiagnostic = string.Empty;
            Interlocked.Exchange(ref hardwareConfirmedFlag, 0);
            hardwareDecodeEvidence = string.Empty;

            Uri streamUri;
            string validationError;
            if (!TryValidateRtspUrl(url, out streamUri, out validationError))
            {
                FailWithoutRetry(validationError);
                return;
            }

            string initializationError;
            if (!TryInitializeLibVlc(out initializationError))
            {
                FailWithoutRetry(initializationError);
                return;
            }

            int newSession = ++session;
            try
            {
                VlcNativeBridge.PrepareNextMediaPlayer(attemptMode);
                player = new MediaPlayer(libVlc);

                if (attemptMode == VlcDecodeMode.Gpu)
                {
                    if (!VlcNativeBridge.IsD3D11Renderer())
                        throw new InvalidOperationException(
                            "GPU mode requires Unity to run with Direct3D 11. Active renderer: " +
                            VlcNativeBridge.RendererDescription() + ".");
                    if (!VlcNativeBridge.HasNativeRenderer(player))
                        throw new InvalidOperationException(
                            "The media player did not receive a native D3D11 renderer.");

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

                media = new Media(streamUri);
                openingStartedAt = Time.realtimeSinceStartup;
                lastFrameAt = openingStartedAt;
                status = "Opening " + attemptMode + " path";

                if (!player.Play(media))
                    throw new InvalidOperationException("LibVLC rejected the playback request.");
            }
            catch (Exception exception)
            {
                string reason = VlcLogSanitizer.Sanitize(
                    "Unable to start the " + attemptMode + " path: " + exception.Message);
                HandlePlaybackFailure(reason);
            }
        }

        private bool TryInitializeLibVlc(out string error)
        {
            error = null;
            string runtimeBasePath;
            if (!VlcWindowsRuntime.TryPrepare(out runtimeBasePath, out error))
                return false;

            try
            {
                if (!coreInitialized)
                {
                    Core.Initialize(runtimeBasePath);
                    coreInitialized = true;
                }

                if (!VlcNativeBridge.TryInitialize(out error))
                    return false;

                if (libVlc == null)
                {
                    var arguments = new List<string>
                    {
                        "--no-video-title-show",
                        "-vv",
                    };
                    if (forceRtspTcp) arguments.Add("--rtsp-tcp");
                    if (disableAudio) arguments.Add("--no-audio");
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
            if (currentAttemptMode == VlcDecodeMode.Gpu &&
                VlcLogSanitizer.IsHardwareDecoderEvidence(module, message))
            {
                hardwareDecodeEvidence = module + ": " + message;
                Interlocked.Exchange(ref hardwareConfirmedFlag, 1);
            }
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

            bool canFallback = decodeMode == VlcDecodeMode.Auto &&
                               currentAttemptMode == VlcDecodeMode.Gpu &&
                               autoFallbackToCpu && !autoFallbackUsed;
            if (canFallback)
            {
                autoFallbackUsed = true;
                fallbackReason = reason;
                status = "GPU unavailable; falling back to CPU";
                Debug.LogWarning("[VLC RTSP] Auto fallback: " + reason, this);
                StartAttempt(VlcDecodeMode.Cpu);
                return;
            }

            PlaybackFailed?.Invoke(reason);
            ScheduleReconnect(reason);
        }

        private void FailWithoutRetry(string reason)
        {
            lastError = VlcLogSanitizer.Sanitize(reason);
            status = "Configuration error";
            scheduledStartAt = float.PositiveInfinity;
            Debug.LogError("[VLC RTSP] " + lastError, this);
            PlaybackFailed?.Invoke(lastError);
            ReleasePlayer();
        }

        private void ScheduleReconnect(string reason)
        {
            ReleasePlayer();
            if (!isActiveAndEnabled || applicationPaused || focusLost || shuttingDown)
                return;

            reconnectAttempt++;
            float exponent = Mathf.Pow(2f, Mathf.Min(reconnectAttempt - 1, 10));
            float delay = Mathf.Min(
                maximumReconnectDelaySeconds,
                initialReconnectDelaySeconds * exponent);
            scheduledStartAt = Time.realtimeSinceStartup + delay;
            status = "Reconnect in " + delay.ToString("0.0") + "s";
            Debug.LogWarning("[VLC RTSP] " + reason + " " + status + ".", this);
        }

        private void ScheduleResumeRestart()
        {
            if (!isActiveAndEnabled || shuttingDown) return;
            ReleasePlayer();
            autoFallbackUsed = false;
            fallbackReason = string.Empty;
            reconnectAttempt = 0;
            scheduledStartAt = Time.realtimeSinceStartup + resumeDelaySeconds;
            status = "Resume rebuild scheduled";
        }

        private void ReleasePlayer()
        {
            ++session;

            if (player != null)
            {
                try { player.Stop(); }
                catch (Exception) { }
                player.Dispose();
                player = null;
                VlcNativeBridge.QueueRendererCleanup();
            }

            if (media != null)
            {
                media.Dispose();
                media = null;
            }

            if (cpuBuffer != null)
            {
                cpuBuffer.Dispose();
                cpuBuffer = null;
            }

            DestroyVideoTexture();
            activeVideoPath = VlcActiveVideoPath.None;
            hasFirstFrame = false;
            externalTexturePointer = IntPtr.Zero;
        }

        private void DestroyVideoTexture()
        {
            if (videoTexture == null) return;
            if (targetImage != null && targetImage.texture == videoTexture)
                targetImage.texture = null;
            Destroy(videoTexture);
            videoTexture = null;
        }

        private void ApplyUvOrientation()
        {
            if (targetImage == null) return;
            targetImage.uvRect = flipVertically
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
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
