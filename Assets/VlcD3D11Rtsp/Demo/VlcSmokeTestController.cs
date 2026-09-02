#if (UNITY_ANDROID && !UNITY_EDITOR) || UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.IO;
using UnityEngine;

namespace VlcD3D11Rtsp
{
    /// <summary>
    /// Standalone acceptance harness. Runtime values come only from environment
    /// variables, so test stream credentials are never committed to the repo.
    /// </summary>
    public sealed class VlcSmokeTestController : MonoBehaviour
    {
        private const string UrlVariable = "VLC_RTSP_TEST_URL";
        private const string ModeVariable = "VLC_DECODE_MODE";
        private const string ReportVariable = "VLC_SMOKE_REPORT";
        private const string RestartVariable = "VLC_SMOKE_RESTART_AFTER_FIRST_FRAME";
        private const string TimeoutVariable = "VLC_SMOKE_TIMEOUT_SECONDS";
        private const string MinimumFramesVariable = "VLC_SMOKE_MIN_FRAMES";
        private const string ObserveSecondsVariable = "VLC_SMOKE_OBSERVE_SECONDS";
        private const string RepeatPlayVariable = "VLC_SMOKE_REPEAT_PLAY_AFTER_FIRST_FRAME";

        [SerializeField] private VlcRtspPlayer player;
        [SerializeField, Min(5f)] private float timeoutSeconds = 40f;
        [SerializeField, Min(0f)] private float gpuEvidenceGraceSeconds = 3f;
        [SerializeField, Min(1)] private int minimumFrames = 1;
        [SerializeField, Min(0f)] private float observeSeconds;

        private float startedAt;
        private float firstFrameAt = -1f;
        private float secondFrameAt = -1f;
        private float scheduledRestartAt = -1f;
        private float scheduledRepeatPlayAt = -1f;
        private string reportPath;
        private int firstFrameCount;
        private bool requireSessionRestart;
        private bool restartIssued;
        private bool repeatSamePlay;
        private bool repeatPlayIssued;
        private long framesBeforeRepeatPlay;
        private bool finished;

        [Serializable]
        private sealed class SmokeReport
        {
            public bool passed;
            public string requestedMode;
            public string attemptedMode;
            public string activeVideoPath;
            public bool hardwareDecodeRequested;
            public bool hardwareDecodeConfirmed;
            public string hardwareDecodeEvidence;
            public bool nativeTextureWithoutCpuReadback;
            public string fallbackReason;
            public string lastError;
            public string graphicsDevice;
            public float firstFrameSeconds;
            public int firstFrameCount;
            public long renderedFrameCount;
            public bool sessionRestartVerified;
            public bool repeatPlayVerified;
            public float recoveryFrameSeconds;
            public float totalSeconds;
            public string diagnostics;
        }

        private void Start()
        {
            startedAt = Time.realtimeSinceStartup;
            reportPath = Environment.GetEnvironmentVariable(ReportVariable);
            string streamUrl = Environment.GetEnvironmentVariable(UrlVariable);
            string requestedMode = Environment.GetEnvironmentVariable(ModeVariable);
            requireSessionRestart = string.Equals(
                Environment.GetEnvironmentVariable(RestartVariable),
                "true",
                StringComparison.OrdinalIgnoreCase) ||
                Environment.GetEnvironmentVariable(RestartVariable) == "1";
            repeatSamePlay = string.Equals(
                Environment.GetEnvironmentVariable(RepeatPlayVariable),
                "true",
                StringComparison.OrdinalIgnoreCase) ||
                Environment.GetEnvironmentVariable(RepeatPlayVariable) == "1";
            float parsedTimeout;
            if (float.TryParse(Environment.GetEnvironmentVariable(TimeoutVariable),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out parsedTimeout) && parsedTimeout >= 5f)
            {
                timeoutSeconds = parsedTimeout;
            }

            int parsedMinimumFrames;
            if (int.TryParse(Environment.GetEnvironmentVariable(MinimumFramesVariable),
                    out parsedMinimumFrames) && parsedMinimumFrames >= 1)
            {
                minimumFrames = parsedMinimumFrames;
            }

            float parsedObserveSeconds;
            if (float.TryParse(Environment.GetEnvironmentVariable(ObserveSecondsVariable),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out parsedObserveSeconds) && parsedObserveSeconds >= 0f)
            {
                observeSeconds = parsedObserveSeconds;
            }

            if (player == null)
            {
                Finish(false, "Smoke scene has no VlcRtspPlayer reference.", 3);
                return;
            }

            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                Finish(false, "Set " + UrlVariable + " before launching the smoke player.", 3);
                return;
            }

            VlcDecodeMode parsedMode;
            if (!Enum.TryParse(requestedMode, true, out parsedMode))
                parsedMode = VlcDecodeMode.Auto;

            player.Url = streamUrl;
            player.DecodeMode = parsedMode;
            player.FirstFrameReady += OnFirstFrame;
            player.PlaybackFailed += OnPlaybackFailed;
            player.Play();
        }

        private void Update()
        {
            if (finished || player == null) return;

            float elapsed = Time.realtimeSinceStartup - startedAt;
            if (elapsed >= timeoutSeconds)
            {
                Finish(false, string.IsNullOrEmpty(player.LastError)
                    ? "Smoke test timed out."
                    : player.LastError, 2);
                return;
            }

            if (firstFrameAt < 0f) return;

            if (scheduledRestartAt >= 0f && !restartIssued &&
                Time.realtimeSinceStartup >= scheduledRestartAt)
            {
                restartIssued = true;
                player.RestartPreferred();
                return;
            }

            if (scheduledRepeatPlayAt >= 0f && !repeatPlayIssued &&
                Time.realtimeSinceStartup >= scheduledRepeatPlayAt)
            {
                repeatPlayIssued = true;
                framesBeforeRepeatPlay = player.RenderedFrameCount;
                player.Play();
                return;
            }

            if (requireSessionRestart && secondFrameAt < 0f) return;
            if (repeatSamePlay)
            {
                if (!repeatPlayIssued ||
                    player.RenderedFrameCount <= framesBeforeRepeatPlay) return;
                if (firstFrameCount != 1)
                {
                    Finish(false, "Repeated Play rebuilt the active session.", 2);
                    return;
                }
            }
            if (player.RenderedFrameCount < minimumFrames) return;
            if (Time.realtimeSinceStartup - firstFrameAt < observeSeconds) return;

            bool needsEvidenceGrace = player.CurrentAttemptMode == VlcDecodeMode.Gpu &&
                                      !player.HardwareDecodeConfirmed &&
                                      Time.realtimeSinceStartup - firstFrameAt <
                                      gpuEvidenceGraceSeconds;
            if (!needsEvidenceGrace) Finish(true, string.Empty, 0);
        }

        private void OnFirstFrame()
        {
            firstFrameCount++;
            if (firstFrameAt < 0f)
            {
                firstFrameAt = Time.realtimeSinceStartup;
                if (requireSessionRestart)
                    scheduledRestartAt = firstFrameAt + 0.5f;
                if (repeatSamePlay)
                    scheduledRepeatPlayAt = firstFrameAt + 0.5f;
            }
            else if (restartIssued && secondFrameAt < 0f)
            {
                secondFrameAt = Time.realtimeSinceStartup;
            }
        }

        private void OnPlaybackFailed(string reason)
        {
            // The player owns reconnect policy. The harness waits until its
            // overall timeout so a temporary network loss can still recover.
        }

        private void Finish(bool passed, string error, int exitCode)
        {
            if (finished) return;
            finished = true;

            float now = Time.realtimeSinceStartup;
            var report = new SmokeReport
            {
                passed = passed,
                requestedMode = player == null ? "Unknown" : player.DecodeMode.ToString(),
                attemptedMode = player == null ? "Unknown" : player.CurrentAttemptMode.ToString(),
                activeVideoPath = player == null ? "None" : player.ActiveVideoPath.ToString(),
                hardwareDecodeRequested = player != null && player.HardwareDecodeRequested,
                hardwareDecodeConfirmed = player != null && player.HardwareDecodeConfirmed,
                hardwareDecodeEvidence = player == null
                    ? string.Empty
                    : player.HardwareDecodeEvidence,
                nativeTextureWithoutCpuReadback = player != null &&
                                                  (player.ActiveVideoPath ==
                                                   VlcActiveVideoPath.D3D11NativeTexture ||
                                                   player.ActiveVideoPath ==
                                                   VlcActiveVideoPath.AndroidNativeTexture),
                fallbackReason = player == null ? string.Empty : player.FallbackReason,
                lastError = string.IsNullOrEmpty(error) && player != null
                    ? player.LastError
                    : error,
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                firstFrameSeconds = firstFrameAt < 0f ? -1f : firstFrameAt - startedAt,
                firstFrameCount = firstFrameCount,
                renderedFrameCount = player == null ? 0 : player.RenderedFrameCount,
                sessionRestartVerified = requireSessionRestart && secondFrameAt >= 0f,
                repeatPlayVerified = repeatSamePlay && repeatPlayIssued &&
                                     firstFrameCount == 1 && player != null &&
                                     player.RenderedFrameCount > framesBeforeRepeatPlay,
                recoveryFrameSeconds = secondFrameAt < 0f
                    ? -1f
                    : secondFrameAt - scheduledRestartAt,
                totalSeconds = now - startedAt,
                diagnostics = player == null ? string.Empty : player.DiagnosticsSummary,
            };

            WriteReport(report);
            Debug.Log("[VLC SMOKE] " + JsonUtility.ToJson(report));

#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(exitCode);
#else
            Application.Quit(exitCode);
#endif
        }

        private void WriteReport(SmokeReport report)
        {
            if (string.IsNullOrWhiteSpace(reportPath)) return;
            try
            {
                string fullPath = Path.GetFullPath(reportPath);
                string directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(fullPath, JsonUtility.ToJson(report, true));
            }
            catch (Exception exception)
            {
                Debug.LogError("[VLC SMOKE] Unable to write report: " +
                               exception.GetType().Name + ".");
            }
        }
    }
}
#endif
