#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine;
using UnityEngine.UI;

namespace VlcD3D11Rtsp
{
    /// <summary>Small, dependency-free uGUI front end for the sample scene.</summary>
    public sealed class VlcDemoController : MonoBehaviour
    {
        [SerializeField] private VlcRtspPlayer player;
        [SerializeField] private InputField urlInput;
        [SerializeField] private Dropdown modeDropdown;
        [SerializeField] private Text statusText;

        private float nextStatusRefresh;

        private void Awake()
        {
            if (modeDropdown != null)
            {
                modeDropdown.ClearOptions();
                modeDropdown.AddOptions(new System.Collections.Generic.List<string>
                {
                    "Auto (GPU -> CPU fallback)",
                    "CPU callbacks",
                    "GPU / D3D11 native texture",
                });
                modeDropdown.SetValueWithoutNotify((int)player.DecodeMode);
            }

            if (urlInput != null) urlInput.SetTextWithoutNotify(player.Url);
        }

        private void Update()
        {
            if (statusText == null || player == null ||
                Time.unscaledTime < nextStatusRefresh) return;

            nextStatusRefresh = Time.unscaledTime + 0.2f;
            statusText.text = player.Status + "\n" + player.DiagnosticsSummary +
                              (string.IsNullOrEmpty(player.LastError)
                                  ? string.Empty
                                  : "\nlastError=" + player.LastError);
        }

        public void ApplyAndPlay()
        {
            if (player == null) return;
            if (urlInput != null) player.Url = urlInput.text.Trim();
            if (modeDropdown != null) player.SetDecodeMode(modeDropdown.value);
            player.Play();
        }

        public void Stop()
        {
            if (player != null) player.Stop();
        }

        public void RestartPreferred()
        {
            if (player != null) player.RestartPreferred();
        }
    }
}
#endif
