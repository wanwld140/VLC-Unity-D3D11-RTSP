# Validation checklist

## Static and build

- Run `scripts/verify-repository.ps1`.
- Rebuild `VLCUnityPlugin.dll` from `Native~/`.
- Confirm required exports with `dumpbin /exports`.
- Compile Unity scripts and build both Demo and Smoke players.

## Runtime matrix

Run the smoke player against a controlled RTSP source for every codec/resolution
that matters to the product.

| Case | Required evidence |
|---|---|
| CPU | first frame, `CpuMemoryBuffer`, `RenderedFrameCount` and CPU callback counters advancing during an observation window |
| GPU | first frame, `D3D11NativeTexture`, `RenderedFrameCount` advancing, no CPU callback buffer |
| Auto / GPU available | same native path as GPU |
| Auto / GPU unavailable | `CpuMemoryBuffer` plus a non-empty fallback reason |
| Suspend/resume | a new media session and a second first frame |
| Network loss/recovery | backoff attempts followed by a new first frame |
| Repeated start/stop | no crash, stale frame, or unbounded resource growth |

`HardwareDecodeConfirmed` requires a LibVLC hardware-decoder log message. A
native D3D11 output texture alone is not sufficient evidence because software
decoding can also feed a GPU video output.

Set `VLC_SMOKE_MIN_FRAMES` and `VLC_SMOKE_OBSERVE_SECONDS` for continuous-frame
acceptance. A report that only proves the first frame is insufficient for live
monitoring playback.
