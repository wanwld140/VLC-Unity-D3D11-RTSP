# Pinned dependencies

| Component | Pin | Verification |
|---|---|---|
| Unity | `2021.3.28f1c1` | `ProjectSettings/ProjectVersion.txt` |
| VLC-Unity source | `58afab6a2bf0b1b5dc668704faf780e384a99be6` | source provenance below |
| LibVLCSharp | `333a98a54095c94c966c4fca117cd11cffeee919` | DLL SHA-256 `260EC9F6DCFD5DFC57372D3B1B1167A44D62F3A068BCB1D8EED541D4F529275B` |
| LibVLC Windows | `4.0.0-alpha-20260831` | nupkg SHA-256 `6982B57F7703368062002EDE57854F4C076C5D705522139FC4380E0FFA981697` |

The native source was copied from the pinned VLC-Unity tree. The local diff is
limited to the versioned per-player CPU/GPU selector and native renderer
diagnostics in `RenderingPlugin.cpp`. Build artifacts are regenerated locally
and are not treated as source provenance.
