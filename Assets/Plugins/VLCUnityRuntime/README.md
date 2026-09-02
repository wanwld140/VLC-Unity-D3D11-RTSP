# VLC Android runtime

This folder contains the Android-specific glue used with the shared
`Assets/Plugins/Managed/LibVLCSharp.dll`. The managed DLL is byte-identical to
the pinned Android build and is kept only once in the Unity project.

Runtime artifacts live under `Assets/Plugins/Android/VLCUnity`:

- `vlc-android-java.aar`: Java 8-compatible LibVLC Android classes and the
  `INTERNET` permission;
- `arm64-v8a/libvlc.so`: LibVLC 4 core and statically selected modules;
- `arm64-v8a/libVLCUnityPlugin.so`: Unity OpenGL ES/Vulkan texture bridge.

The project targets Android ARM64, IL2CPP, API 29 or newer, and uses OpenGL ES
3 by default to match the user-tested source project. `VlcRtspPlayer` exposes
the same requested modes as Windows:

- `Cpu`: RV32 LibVLC callbacks copied into a Unity `Texture2D`;
- `Gpu`: Android native texture output, with hardware decoding requested but
  not claimed until device evidence exists;
- `Auto`: native texture first, then a real CPU callback fallback.

The imported artifacts came from pinned VideoLAN sources. Exact commits and
SHA-256 values are recorded in `docs/ANDROID_SOURCE_INFO.json`; upstream
license texts are retained in `LICENSES/`. The user reports that the original
package passed playback on Android hardware in the source project
`lysc-prj-20241205-231-main-20260828`. This repository's migrated APK passed
Unity 2021 ARM64/IL2CPP packaging, installation, OpenGL ES 3 initialization,
and an Auto/native first frame on a Xiaomi 24129PN74C running Android 16. The
public stream then stalled, so the full mode, recovery, and soak matrix remains
pending on a controlled RTSP source.
