#if UNITY_EDITOR_WIN
using System;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace VlcD3D11Rtsp.Editor
{
    /// <summary>Copies local VLC and optional Hikvision runtimes beside a Windows player.</summary>
    public sealed class VlcRuntimeBuildProcessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != UnityEditor.BuildTarget.StandaloneWindows64)
                return;

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                                 Directory.GetCurrentDirectory();
            string source = Path.Combine(projectRoot, "External", "VLCUnityWindows");
            if (!Directory.Exists(source))
                throw new BuildFailedException(
                    "Pinned LibVLC runtime is missing. Run scripts/setup-dependencies.ps1.");

            string executable = report.summary.outputPath;
            string dataDirectory = Path.Combine(
                Path.GetDirectoryName(executable) ?? projectRoot,
                Path.GetFileNameWithoutExtension(executable) + "_Data");
            string destination = Path.Combine(dataDirectory, "VLCUnityWindows");
            CopyDirectory(source, destination);

            // 海康 SDK 是用户本地提供的专有依赖；未安装时 VLC-only 构建仍然有效。
            string hikvisionSource = Path.Combine(
                projectRoot, "External", "HikvisionWindows");
            if (Directory.Exists(hikvisionSource))
            {
                string hikvisionDestination = Path.Combine(
                    dataDirectory, "HikvisionWindows");
                CopyDirectory(hikvisionSource, hikvisionDestination);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(
                         source, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (string file in Directory.GetFiles(
                         source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? destination);
                File.Copy(file, target, true);
            }
        }
    }
}
#endif
