# VLC Unity D3D11 RTSP

一个面向 Windows x64 / Unity 的独立 RTSP 播放工程。播放器脚本可选
`CPU`、`GPU` 或 `Auto`，GPU 路径使用 LibVLC 4 output callbacks 和
`ID3D11Texture2D` 原生纹理桥，Unity 不做逐帧 CPU 回读。

> 当前锁定的是 LibVLC 4 预览包，不是稳定版。仓库适合验证和继续工程化，
> 发布产品前请完成播放器回归、目标显卡覆盖以及二进制许可证审计。

## 三种模式

| 模式 | 解码请求 | Unity 输出路径 | 失败行为 |
|---|---|---|---|
| `Cpu` | 禁用 LibVLC 硬解 | RV32 回调、`Texture2D.LoadRawTextureData` | 按退避策略重建 |
| `Gpu` | 请求 Windows `d3d11va` | D3D11 原生外部纹理 | 保持 GPU，按退避策略重建 |
| `Auto` | 先请求 `d3d11va` | 优先 D3D11 原生纹理 | 原生桥/首帧失败后转 CPU |

“D3D11 原生纹理已生效”和“视频解码器确实使用 GPU”是两个独立状态：

- `ActiveVideoPath == D3D11NativeTexture` 证明 Unity 侧没有 CPU 帧上传；
- `HardwareDecodeConfirmed == true` 只在 LibVLC 日志出现可识别的
  `d3d11va` 证据后成立。

代码不会仅凭 `EnableHardwareDecoding = true` 就宣称硬解成功。

## 环境

- Windows 10/11 x64
- Unity `2021.3.28f1c1`
- Direct3D 11（工程构建脚本会强制只保留 D3D11）
- Visual Studio 2022 C++ x64 工具与 Windows SDK
- PowerShell 7 或 Windows PowerShell 5.1

## 首次配置

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-dependencies.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-native.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-unity-player.ps1 -Target Demo
```

依赖脚本会下载并校验固定 SHA-256 的
`VideoLAN.LibVLC.Windows 4.0.0-alpha-20260831`。大体积 LibVLC runtime
安装在被 Git 忽略的 `External/`，构建播放器时自动复制到
`<Player>_Data/VLCUnityWindows`。

离线安装可传入已经下载的包：

```powershell
.\scripts\setup-dependencies.ps1 -PackagePath D:\Downloads\videolan.libvlc.windows.4.0.0-alpha-20260831.nupkg
```

## Unity 使用

打开 `Assets/VlcD3D11Rtsp/Demo/Demo.unity`。也可以把
`VlcRtspPlayer` 挂到带 `RawImage` 的对象上：

```csharp
player.Url = "rtsp://camera-or-server/path";
player.DecodeMode = VlcDecodeMode.Auto; // Cpu / Gpu / Auto
player.Play();
```

组件在应用暂停、失焦、首帧超时、画面卡死或 LibVLC 报错后会释放旧
`MediaPlayer` 并创建新会话。它不会尝试复用息屏前的 RTSP socket。

## 自动验收

先构建 smoke player：

```powershell
.\scripts\build-unity-player.ps1 -Target Smoke
```

然后通过环境变量传入测试流。URL 不会写入仓库或 JSON 报告：

```powershell
$env:VLC_RTSP_TEST_URL = 'rtsp://your-test-stream/path'
$env:VLC_DECODE_MODE = 'Gpu' # Cpu / Gpu / Auto
$env:VLC_SMOKE_REPORT = "$PWD\Build\Reports\gpu.json"
& .\Build\Smoke\VlcD3D11RtspSmoke.exe -screen-fullscreen 0 -screen-width 960 -screen-height 540
```

报告分别记录请求模式、实际路径、首帧耗时、Auto 回退原因，以及是否获得
硬解日志证据。请依次跑 `Cpu`、`Gpu`、`Auto`，并在目标显卡和目标 RTSP
编码格式上复核。

## 目录

- `Assets/VlcD3D11Rtsp/Runtime`：播放器、CPU 回调、运行库和原生桥封装
- `Assets/VlcD3D11Rtsp/Demo`：示例 UI 与无凭据 smoke harness
- `Native~/VLCUnityPlugin`：可审计的 VLC-Unity D3D11 原生源码
- `scripts`：依赖、原生 DLL、Unity player 和仓库校验脚本
- `docs`：架构、验证和依赖锁定说明

本机最终验收记录见
[`docs/VALIDATION_RESULTS_2026-08-31.md`](docs/VALIDATION_RESULTS_2026-08-31.md)。

## 许可与分发

仓库使用 LGPL-2.1-or-later。原生桥派生自 VideoLAN VLC-Unity；托管 DLL
来自 LibVLCSharp。具体固定版本、哈希和修改说明见
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)。

LibVLC 二进制包可能包含不同许可的可选 plug-in。默认依赖脚本会移除
`scripts/gpl-plugin-denylist.txt` 中列出的明显可选项，但这不构成完整法律
审计。发布二进制前应按实际保留的 plug-in 做一次清单审计并提供对应源码、
许可证和可替换/重链接方式。
