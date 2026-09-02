# Third-party notices

This repository contains or builds against the following projects. The list is
provided for engineering traceability and is not legal advice.

The repository's root MIT License applies only to original material authored
for this project. It does not relicense the derived or third-party components
listed below; those components remain subject to their respective notices and
license terms.

## VLC-Unity native rendering plug-in

- Upstream: <https://code.videolan.org/videolan/vlc-unity>
- Pinned source commit: `58afab6a2bf0b1b5dc668704faf780e384a99be6`
- License: LGPL-2.1-or-later; see
  `LICENSES/VLC-Unity-LGPL-2.1-or-later.txt`.
- Local changes: `RenderingPlugin.cpp` adds a versioned managed/native bridge
  API and a per-player CPU/native-GPU selection. `SHOW_WATERMARK` is not
  defined in this repository's build.

## LibVLCSharp

- Upstream: <https://code.videolan.org/videolan/LibVLCSharp>
- Pinned source commit: `333a98a54095c94c966c4fca117cd11cffeee919`
- Distributed file: `Assets/Plugins/Managed/LibVLCSharp.dll`
- SHA-256: `260EC9F6DCFD5DFC57372D3B1B1167A44D62F3A068BCB1D8EED541D4F529275B`
- License: LGPL-2.1-or-later; see
  `LICENSES/LibVLCSharp-LGPL-2.1-or-later.txt`.

## LibVLC Windows preview runtime

- Package: `VideoLAN.LibVLC.Windows` `4.0.0-alpha-20260831`
- Source URL and package SHA-256 are pinned in
  `scripts/setup-dependencies.ps1`.
- The 230 MB runtime is intentionally not committed. The setup script installs
  it under `External/` and the Unity build processor copies it into the player.
- License: LibVLC is LGPL-2.1-or-later, but a binary distribution may include
  optional plug-ins under other licenses. The default setup removes the obvious
  optional plug-ins listed in `scripts/gpl-plugin-denylist.txt`. Audit the exact
  package and your distribution obligations before publishing binaries.

## Unity headers

## LibVLC Android ARM64 runtime

- Distributed artifacts:
  - `Assets/Plugins/Android/VLCUnity/vlc-android-java.aar`
  - `Assets/Plugins/Android/VLCUnity/arm64-v8a/libvlc.so`
  - `Assets/Plugins/Android/VLCUnity/arm64-v8a/libVLCUnityPlugin.so`
- Pinned source commits and SHA-256 values are recorded in
  `docs/ANDROID_SOURCE_INFO.json`.
- The VLC-Unity Android bridge is based on commit
  `f2bbedd5bc84f3e1e979a543f4341a9b9c370dff`; the Android LibVLC build pins
  VLC, vlc-android and libvlcjni commits separately.
- Upstream license texts and the source package's notice mapping are retained
  under `Assets/Plugins/VLCUnityRuntime/LICENSES`.
- The recorded libvlcjni build used its `--license l` configuration. This does
  not waive LGPL/GPL or contributed-library obligations. Audit the final
  Android binary, provide required notices/source or relinking mechanism, and
  obtain legal review before public distribution.

## Unity headers

The native source contains the Unity native plug-in interface headers shipped
by the upstream VLC-Unity repository. Their original header notices apply.

## Hikvision HCNetSDK / PlayCtrl (optional, user supplied)

- Expected SDK: `CH-HCNetSDKV6.1.9.48_build20230410_win64`.
- The repository contains interoperability source written against the official
  headers, but does not contain Hikvision SDK DLLs or `HCNetSDKCom` binaries.
- `scripts/setup-hikvision.ps1` copies a user's local official runtime into the
  ignored `External/HikvisionWindows` directory. A Player build copies it only
  when that local directory exists.
- Hikvision binaries are proprietary and are not relicensed by this project's
  MIT License. Obtain the SDK through an authorized Hikvision source and review
  the vendor terms before distributing a build that contains those files.
- Third-party open-source notices shipped inside the vendor SDK remain part of
  the vendor package and do not establish redistribution rights for Hikvision's
  proprietary binaries.
