# Windows validation results — 2026-08-31

Environment: Windows 11 x64, Unity `2021.3.28f1c1`, Direct3D 11,
NVIDIA GeForce RTX 4060. Tests used a public H.264 RTSP endpoint that was live
at test time; the endpoint is intentionally not a repository dependency.

## Final build

- Editor self-tests: passed.
- Native MSVC x64 build: passed.
- Two consecutive native builds produced the same SHA-256:
  `2A9829479D69DA1EB3CDF381C8C96AAB05DD58E0776ED53A24D51DD3750EBABC`.
- Unity Windows Smoke player: passed.
- Unity Windows Demo player: passed.

## Decoding and texture paths

| Requested mode | Result | First frame | Hardware evidence |
|---|---|---:|---|
| CPU | `CpuMemoryBuffer` | 6.739 s | disabled; no evidence accepted |
| GPU | `D3D11NativeTexture` | 5.179 s | `Using D3D11VA (NVIDIA GeForce RTX 4060, ...)` |
| Auto | `D3D11NativeTexture` | 29.595 s | same D3D11VA device selection |

The GPU and Auto reports recorded `cpuCallbacks=n/a`; the managed GPU update
path used `CreateExternalTexture` and did not upload a CPU pixel buffer.

## Session rebuild

The Auto smoke run destroyed the first media session 0.5 seconds after its
first frame and started a new preferred session. It received two first-frame
events, stayed on `D3D11NativeTexture`, reconfirmed D3D11VA, and produced the
second frame 4.338 seconds after the scheduled restart.

These results prove actual decoding on this machine and stream at the stated
time. They do not replace testing every target GPU, driver, camera codec,
resolution, authentication mode, and network-loss pattern.
