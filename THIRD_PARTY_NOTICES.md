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

The native source contains the Unity native plug-in interface headers shipped
by the upstream VLC-Unity repository. Their original header notices apply.
