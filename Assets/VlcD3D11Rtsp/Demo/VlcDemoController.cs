#if UNITY_ANDROID && !UNITY_EDITOR
#define VLCUNITY_ANDROID
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
#define VLCUNITY_WINDOWS
#endif

#if VLCUNITY_ANDROID || VLCUNITY_WINDOWS
using UnityEngine;
using UnityEngine.UI;

namespace VlcD3D11Rtsp
{
    /// <summary>Small, dependency-free uGUI front end for the sample scene.</summary>
    public sealed class VlcDemoController : MonoBehaviour
    {
        [SerializeField] private VlcRtspPlayer player;
#if VLCUNITY_WINDOWS
        [SerializeField] private HikvisionRtspPlayer hikvisionPlayer;
#endif
        [SerializeField] private InputField urlInput;
        [SerializeField] private Dropdown modeDropdown;
        [SerializeField] private Text statusText;

        private float nextStatusRefresh;
#if VLCUNITY_WINDOWS
        private int previousMode;
#endif
        private string lastVlcUrl = VlcRtspPlayer.DefaultTestUrl;
#if VLCUNITY_WINDOWS
        private string lastHikvisionEndpoint = HikvisionRtspPlayer.DefaultEndpoint;
#endif

        private void Awake()
        {
#if VLCUNITY_WINDOWS
            if (hikvisionPlayer == null && player != null)
            {
                hikvisionPlayer = player.GetComponent<HikvisionRtspPlayer>();
                if (hikvisionPlayer == null)
                    hikvisionPlayer = player.gameObject.AddComponent<HikvisionRtspPlayer>();
            }
#endif

            if (modeDropdown != null)
            {
                modeDropdown.ClearOptions();
#if VLCUNITY_ANDROID
                modeDropdown.AddOptions(new System.Collections.Generic.List<string>
                {
                    "Auto (native texture -> CPU fallback)",
                    "CPU callbacks",
                    "GPU request / Android native texture",
                });
                const int rowCount = 3;
#else
                modeDropdown.AddOptions(new System.Collections.Generic.List<string>
                {
                    "Auto (GPU -> CPU fallback)",
                    "CPU callbacks",
                    "GPU / D3D11 native texture",
                    "Hikvision SDK / PlayM4",
                });
                const int rowCount = 4;
#endif
                int initialMode = player == null ? 0 : (int)player.DecodeMode;
                modeDropdown.SetValueWithoutNotify(initialMode);
#if VLCUNITY_WINDOWS
                previousMode = initialMode;
#endif
                ResizeDropdown(modeDropdown, rowCount);
                modeDropdown.onValueChanged.AddListener(OnModeChanged);
            }

            if (player != null) lastVlcUrl = player.Url;
#if VLCUNITY_WINDOWS
            if (hikvisionPlayer != null)
                lastHikvisionEndpoint = hikvisionPlayer.Endpoint;
#endif
            if (urlInput != null) urlInput.SetTextWithoutNotify(lastVlcUrl);
        }

        private void OnDestroy()
        {
            if (modeDropdown != null)
                modeDropdown.onValueChanged.RemoveListener(OnModeChanged);
        }

        private void Update()
        {
            if (statusText == null || Time.unscaledTime < nextStatusRefresh) return;

            nextStatusRefresh = Time.unscaledTime + 0.2f;
#if VLCUNITY_WINDOWS
            bool hikvisionMode = modeDropdown != null && modeDropdown.value == 3;
            if (hikvisionMode && hikvisionPlayer != null)
            {
                statusText.text = hikvisionPlayer.Status + "\n" +
                                  hikvisionPlayer.DiagnosticsSummary +
                                  (string.IsNullOrEmpty(hikvisionPlayer.LastError)
                                      ? string.Empty
                                      : "\nlastError=" + hikvisionPlayer.LastError);
                return;
            }
#endif
            if (player != null)
            {
                statusText.text = player.Status + "\n" + player.DiagnosticsSummary +
                                  (string.IsNullOrEmpty(player.LastError)
                                      ? string.Empty
                                      : "\nlastError=" + player.LastError);
            }
        }

        public void ApplyAndPlay()
        {
            int mode = modeDropdown == null ? 0 : modeDropdown.value;
#if VLCUNITY_WINDOWS
            if (mode == 3)
            {
                if (hikvisionPlayer == null) return;
                if (urlInput != null)
                {
                    string endpointError;
                    if (!hikvisionPlayer.TryConfigureEndpoint(
                            urlInput.text.Trim(), out endpointError))
                    {
                        if (statusText != null) statusText.text = endpointError;
                        return;
                    }
                    lastHikvisionEndpoint = hikvisionPlayer.Endpoint;
                }
                if (player != null) player.Stop();
                hikvisionPlayer.Play();
                return;
            }
#endif

            if (player == null) return;
#if VLCUNITY_WINDOWS
            if (hikvisionPlayer != null) hikvisionPlayer.Stop();
#endif
            if (urlInput != null)
            {
                lastVlcUrl = urlInput.text.Trim();
                player.Url = lastVlcUrl;
            }
            player.SetDecodeMode(mode);
            player.Play();
        }

        public void Stop()
        {
            if (player != null) player.Stop();
#if VLCUNITY_WINDOWS
            if (hikvisionPlayer != null) hikvisionPlayer.Stop();
#endif
        }

        public void RestartPreferred()
        {
#if VLCUNITY_WINDOWS
            if (modeDropdown != null && modeDropdown.value == 3)
            {
                if (hikvisionPlayer != null) hikvisionPlayer.RestartPreferred();
            }
            else if (player != null)
            {
                player.RestartPreferred();
            }
#else
            if (player != null) player.RestartPreferred();
#endif
        }

        private void OnModeChanged(int mode)
        {
#if VLCUNITY_WINDOWS
            if (urlInput == null)
            {
                previousMode = mode;
                return;
            }

            if (mode == 3 && previousMode != 3)
            {
                lastVlcUrl = urlInput.text;
                urlInput.SetTextWithoutNotify(lastHikvisionEndpoint);
            }
            else if (mode != 3 && previousMode == 3)
            {
                string ignored;
                if (hikvisionPlayer != null &&
                    hikvisionPlayer.TryConfigureEndpoint(urlInput.text, out ignored))
                    lastHikvisionEndpoint = hikvisionPlayer.Endpoint;
                urlInput.SetTextWithoutNotify(lastVlcUrl);
            }
            previousMode = mode;
#endif
        }

        private static void ResizeDropdown(Dropdown dropdown, int rowCount)
        {
            if (dropdown.template == null) return;
            float height = Mathf.Max(1, rowCount) * 30f;
            Vector2 templateSize = dropdown.template.sizeDelta;
            templateSize.y = height;
            dropdown.template.sizeDelta = templateSize;

            ScrollRect scroll = dropdown.template.GetComponent<ScrollRect>();
            if (scroll == null || scroll.content == null) return;
            Vector2 contentSize = scroll.content.sizeDelta;
            contentSize.y = height;
            scroll.content.sizeDelta = contentSize;
        }
    }
}
#endif
