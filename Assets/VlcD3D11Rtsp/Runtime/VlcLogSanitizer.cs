using System;
using System.Text.RegularExpressions;

namespace VlcD3D11Rtsp
{
    public static class VlcLogSanitizer
    {
        private static readonly Regex RtspUri = new Regex(
            "rtsps?://[^\\s\\\"'<>]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return RtspUri.Replace(value, "<redacted-rtsp-url>");
        }

        public static bool IsHardwareDecoderEvidence(string module, string message)
        {
            // Module discovery and option echoes can contain "d3d11va" even
            // when no hardware decoder was opened. VLC's d3d11va module emits
            // this exact prefix only after it has selected a device.
            string text = (message ?? string.Empty).ToLowerInvariant();
            return text.Contains("using d3d11va (") ||
                   text.Contains("using d3d11 video decoder (");
        }

        public static bool IsRelevantDiagnostic(string module, string message)
        {
            string text = ((module ?? string.Empty) + " " + (message ?? string.Empty))
                .ToLowerInvariant();
            return text.Contains("d3d11") || text.Contains("d3d11va") ||
                   text.Contains("hardware") || text.Contains("decoder") ||
                   text.Contains("avcodec") || text.Contains("video output");
        }
    }
}
