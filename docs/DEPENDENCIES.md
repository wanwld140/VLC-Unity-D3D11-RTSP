# Pinned dependencies

| Component | Pin | Verification |
|---|---|---|
| Unity project | `2021.3.28f1c1` | `ProjectSettings/ProjectVersion.txt` |
| Unity compatibility | `2022.3.34f1c1`, `6000.4.3f1` | local Editor self-test and Windows Demo build on 2026-09-01 |
| VLC-Unity source | `58afab6a2bf0b1b5dc668704faf780e384a99be6` | source provenance below |
| LibVLCSharp | `333a98a54095c94c966c4fca117cd11cffeee919` | DLL SHA-256 `260EC9F6DCFD5DFC57372D3B1B1167A44D62F3A068BCB1D8EED541D4F529275B` |
| LibVLC Windows | `4.0.0-alpha-20260831` | nupkg SHA-256 `6982B57F7703368062002EDE57854F4C076C5D705522139FC4380E0FFA981697` |
| VLC Android Java AAR | pinned source commits in `ANDROID_SOURCE_INFO.json` | SHA-256 `864E5A261EA71F3FF4A8F63789F02008180BA0A448F96C6AC364CF0FA1BB315F` |
| LibVLC Android ARM64 | VLC `275bbed0a08433de13c007bf00f1aad2ebd7acbb` | SHA-256 `89726D8B607C373CA9394D2564D00273CFC83F0D1469ECB999F9878EB5F8925E` |
| VLC-Unity Android bridge | VLC-Unity `f2bbedd5bc84f3e1e979a543f4341a9b9c370dff` | SHA-256 `B4E52BC78C2967901CA8C46EFB43DCD3F0140E5064A91067199CBA643E7CE62D` |
| Hikvision HCNetSDK / PlayCtrl (optional) | `V6.1.9.48_build20230410_win64` | `scripts/setup-hikvision.ps1` validates required x64 PE files and writes a local SHA-256 manifest |

The native source was copied from the pinned VLC-Unity tree. The local diff is
limited to the versioned per-player CPU/GPU selector and native renderer
diagnostics in `RenderingPlugin.cpp`. Build artifacts are regenerated locally
and are not treated as source provenance.

The Android AAR and ARM64 shared libraries are committed Unity plug-ins because
they are the platform runtime itself. Their exact source commits, hashes, ABI,
API floor and earlier package-build boundary are recorded in
`docs/ANDROID_SOURCE_INFO.json`. The shared `LibVLCSharp.dll` is byte-identical
for the Windows and Android integrations and is imported once with explicit
platform settings.

The Hikvision SDK is a user-supplied proprietary dependency. Its binaries and
the generated `External/HikvisionWindows/DEPENDENCY_MANIFEST.json` are not
committed. A Windows player includes this runtime only when the local directory
exists at build time. See `THIRD_PARTY_NOTICES.md` before redistribution.

The repository remains pinned to Unity 2021.3. Cross-version tests run in
copies because Unity rewrites project files. Unity 6 also requires the explicit
`-NormalizeChinaVersionForUpgrade` preflight when the source version contains
the China-editor `c1` suffix; the script backs up the original marker under
`Build/UnityUpgradeBackup`.
