#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace VlcD3D11Rtsp.Editor
{
    public static class VlcEditorSelfTests
    {
        private const string ExpectedLibVlcSharpHash =
            "260EC9F6DCFD5DFC57372D3B1B1167A44D62F3A068BCB1D8EED541D4F529275B";

        [MenuItem("VLC RTSP/Run editor self-tests")]
        public static void Run()
        {
            string secretUrl = "rtsp://viewer:secret@camera.local/live?token=private";
            string sanitized = VlcLogSanitizer.Sanitize(
                "failed to open " + secretUrl + " at input");
            Assert(!sanitized.Contains("secret") && !sanitized.Contains("private"),
                "RTSP URL sanitizer leaked credentials or a query token.");
            Assert(sanitized.Contains("<redacted-rtsp-url>"),
                "RTSP URL sanitizer did not insert its redaction marker.");

            Assert(!VlcLogSanitizer.IsHardwareDecoderEvidence(
                    "d3d11va", "looking for hw decoder module matching any"),
                "Module discovery was incorrectly accepted as hardware evidence.");
            Assert(VlcLogSanitizer.IsHardwareDecoderEvidence(
                    "d3d11va",
                    "Using D3D11VA (Example GPU, vendor 1234, device 5678, revision 1)"),
                "A real VLC D3D11VA selection message was not recognized.");

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 Directory.GetCurrentDirectory();
            string managedDll = Path.Combine(
                projectRoot, "Assets", "Plugins", "Managed", "LibVLCSharp.dll");
            Assert(File.Exists(managedDll), "LibVLCSharp.dll is missing.");
            Assert(HashFile(managedDll) == ExpectedLibVlcSharpHash,
                "LibVLCSharp.dll does not match the pinned source artifact.");

            Debug.Log("[VLC RTSP] Editor self-tests passed.");
        }

        public static void BatchRun()
        {
            Run();
        }

        private static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return BitConverter.ToString(algorithm.ComputeHash(stream))
                    .Replace("-", string.Empty);
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
