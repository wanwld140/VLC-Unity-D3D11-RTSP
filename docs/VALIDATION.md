# Validation checklist

## Static and build

- Run `scripts/verify-repository.ps1`.
- Run `scripts/run-editor-tests.ps1` with the selected Unity editor.
- Build the Demo and Smoke players with `scripts/build-unity-player.ps1`.
- Build the Android ARM64 Demo or Smoke APK with
  `scripts/build-android-player.ps1`; inspect the resulting APK rather than
  accepting the Unity exit code alone.
- When `Native~/` changes, close Unity/players, run `scripts/build-native.ps1`,
  and confirm the required exports. Native rebuild is not a first-run step.
- For a cross-version copy, use `-AllowVersionMismatch`. Unity 6 upgrades from
  an old China-editor version also require `-NormalizeChinaVersionForUpgrade`.

## Runtime matrix

Run the smoke player against a controlled RTSP source for every codec/resolution
that matters to the product.

| Case | Required evidence |
|---|---|
| CPU | first frame, `CpuMemoryBuffer`, `RenderedFrameCount` and CPU callback counters advancing during an observation window |
| GPU | first frame, `D3D11NativeTexture`, `RenderedFrameCount` advancing, no CPU callback buffer |
| Auto / GPU available | same native path as GPU |
| Auto / GPU unavailable | `CpuMemoryBuffer` plus a non-empty fallback reason |
| Android CPU | device first/continuous frames, `CpuMemoryBuffer`, callback counters and no native-texture claim |
| Android GPU | device first/continuous frames, `AndroidNativeTexture`; hardware decode remains separate evidence |
| Android Auto fallback | force native-texture failure, observe a fallback reason and advancing CPU callbacks |
| Android packaging | ARM64-only APK, API29 floor, IL2CPP, INTERNET permission, expected AAR/classes and exact native hashes |
| Android suspend/resume | background/screen-off, old session disposal, resumed second first frame and continued frame count |
| Suspend/resume | a new media session and a second first frame |
| Network loss/recovery | backoff attempts followed by a new first frame |
| Repeated start/stop | no crash, stale frame, or unbounded resource growth |
| Hikvision SDK missing | VLC modes still build/play; selecting Hikvision gives a clear installation error |
| Hikvision SDK installed | ABI self-test, SDK Init/Cleanup, device login, first decoded YV12 frame, continuous frame count, stop/reconnect |

`HardwareDecodeConfirmed` requires a LibVLC hardware-decoder log message. A
native D3D11 output texture alone is not sufficient evidence because software
decoding can also feed a GPU video output.

Likewise, `AndroidNativeTexture` proves only the Android Unity output path. Do
not report Android hardware decoding as confirmed without decoder-specific
device evidence. A successful APK build is packaging evidence, not RTSP
playback or screen-off recovery evidence.

The imported source package has a user-reported Android hardware playback pass
in `lysc-prj-20241205-231-main-20260828`. Record that as source-package runtime
evidence. The migrated APK has a separate OpenGL ES 3 Auto/native first-frame
pass on Xiaomi 24129PN74C / Android 16; keep continuous playback and the full
mode/recovery matrix pending until they pass on a controlled stream.

Set `VLC_SMOKE_MIN_FRAMES` and `VLC_SMOKE_OBSERVE_SECONDS` for continuous-frame
acceptance. A report that only proves the first frame is insufficient for live
monitoring playback.

The public Demo URL is a convenience endpoint, not a stable acceptance source.
Use a controlled camera or LAN RTSP server for timing and recovery tests. The
Hikvision path requires a real device account; SDK loading and ABI checks alone
do not prove device login or live decoding.
