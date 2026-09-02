# VLC Unity D3D11 RTSP

一个面向 Windows x64 与 Android ARM64 的独立 Unity RTSP 播放工程。播放器
脚本在两个平台都可选 `CPU`、`GPU` 或 `Auto`：Windows GPU 路径使用
LibVLC 4 output callbacks 和 `ID3D11Texture2D`，Android GPU 路径使用
OpenGL ES/Vulkan 原生纹理桥，Unity 都不做逐帧 CPU 回读。工程还提供独立的
Windows `Hikvision SDK / PlayM4` 后端，用海康官方 HCNetSDK 直接登录设备、
取流和解码。

> 当前锁定的是 LibVLC 4 预览包，不是稳定版。仓库适合验证和继续工程化，
> 发布产品前请完成播放器回归、目标显卡覆盖以及二进制许可证审计。

## VLC 三种模式

| 模式 | 解码请求 | Windows 输出 | Android 输出 | 失败行为 |
|---|---|---|---|---|
| `Cpu` | 禁用 LibVLC 硬解 | RV32 回调上传 | RV32 回调上传 | 按退避策略重建 |
| `Gpu` | 请求硬解 | D3D11 原生纹理 | Android native texture | 保持 GPU，按退避策略重建 |
| `Auto` | 先请求硬解 | D3D11 native 优先 | Android native 优先 | 原生桥/首帧失败后转 CPU |

“D3D11 原生纹理已生效”和“视频解码器确实使用 GPU”是两个独立状态：

- `ActiveVideoPath == D3D11NativeTexture` 证明 Unity 侧没有 CPU 帧上传；
- `ActiveVideoPath == AndroidNativeTexture` 证明 Android Unity 侧使用原生纹理；
- `HardwareDecodeConfirmed == true` 只在 LibVLC 日志出现可识别的
  `d3d11va` 证据后成立。

当前硬解确认器只识别 Windows `d3d11va` 证据。Android native texture 仍可能
由软件解码器供帧；代码不会仅凭 `EnableHardwareDecoding = true` 或原生纹理就
宣称 Android 硬解成功。

## 环境

- Windows 10/11 x64
- Android ARM64，最低 API 29，IL2CPP；默认 OpenGL ES 3（与已真机验证的源工程一致）
- 工程锁定版本：Unity `2021.3.28f1c1`
- 本机已验证：Unity `2021.3.28f1c1`、`2022.3.34f1c1`、`6000.4.3f1`
- Direct3D 11（工程构建脚本会强制只保留 D3D11）
- 对应 Unity 版本的 Android Build Support、SDK、NDK 和 OpenJDK
- PowerShell 7 或 Windows PowerShell 5.1

仓库已经带有编译好的 `VLCUnityPlugin.dll`，首次运行不需要 Visual Studio。
只有修改 `Native~/` 或需要自己复核原生构建时，才需要 Visual Studio 2022
C++ x64 工具与 Windows SDK。

## 首次配置

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup-dependencies.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-unity-player.ps1 -Target Demo
powershell -ExecutionPolicy Bypass -File .\scripts\build-android-player.ps1 -Target Demo
```

依赖脚本会下载并校验固定 SHA-256 的
`VideoLAN.LibVLC.Windows 4.0.0-alpha-20260831`。大体积 LibVLC runtime
安装在被 Git 忽略的 `External/`，构建播放器时自动复制到
`<Player>_Data/VLCUnityWindows`。

Android 的 AAR、ARM64 `.so`、托管初始化和许可证已经放在 `Assets/Plugins`，
无需运行 Windows 依赖安装脚本。Android 输出为
`Build/Android/VlcRtspAndroidDemo.apk`；该 APK 默认使用 Unity debug 签名，
正式发布前必须改用项目自己的 keystore。

离线安装可传入已经下载的包：

```powershell
.\scripts\setup-dependencies.ps1 -PackagePath D:\Downloads\videolan.libvlc.windows.4.0.0-alpha-20260831.nupkg
```

### Unity 版本选择与升级

默认会读取 `ProjectSettings/ProjectVersion.txt`，先找精确版本，再找同一
major/minor 下已安装的最新补丁版。也可以显式指定版本或编辑器路径：

```powershell
.\scripts\run-editor-tests.ps1 -UnityVersion 2021.3.28f1c1
.\scripts\build-unity-player.ps1 -Target Demo -UnityPath 'C:\Program Files\Unity\Hub\Editor\2022.3.34f1c1\Editor\Unity.exe' -AllowVersionMismatch
```

跨 major/minor 会改写 Unity 工程，只应在已提交的分支或工程副本中执行：

```powershell
.\scripts\run-editor-tests.ps1 -UnityVersion 2022.3.34f1c1 -AllowVersionMismatch
```

Unity 6 的 Package Manager 不接受旧中国版 Unity 写入的 `f1c1` 版本尾缀。
以下显式开关会先把版本标识规范成 `f1`，并在
`Build/UnityUpgradeBackup` 保存原文件，然后再让 Unity 正常升级工程：

```powershell
.\scripts\run-editor-tests.ps1 -UnityVersion 6000.4.3f1 -AllowVersionMismatch -NormalizeChinaVersionForUpgrade
```

脚本不会静默跨版本。升级完成后应检查 Unity 写入的 `Packages` 和
`ProjectSettings` 差异，再决定是否保留。

### 可选：重编译原生桥

关闭正在使用插件的 Unity Editor 和已打包播放器后执行：

```powershell
.\scripts\build-native.ps1
```

脚本先在 `Build/Native/staging` 生成并校验 DLL，最后才替换仓库插件。
如果目标 DLL 被占用，会保留原文件并提示关闭占用进程，不再报难以判断的
链接器 `LNK1104`。

## Unity 使用

Windows 打开 `Assets/VlcD3D11Rtsp/Demo/Demo.unity`，Android 打开
`Assets/VlcD3D11Rtsp/Demo/AndroidDemo.unity`。也可以把
`VlcRtspPlayer` 挂到带 `RawImage` 的对象上：

```csharp
player.Url = VlcRtspPlayer.DefaultTestUrl;
player.DecodeMode = VlcDecodeMode.Auto; // Cpu / Gpu / Auto
player.Play();
```

Demo 当前默认公网地址为
`rtsp://stream.strba.sk:1935/strba/VYHLAD_JAZERO.stream`。公网测试源可能随时
停用；产品验收应改用可控的摄像机或局域网 RTSP 服务。

`RawImage.color` 必须保持白色，否则 uGUI 会把视频纹理乘成黑色。组件会自动
查找同一对象上的 `RawImage` 和 `AspectRatioFitter`；Demo 场景同时显式保存了
这两个引用。随包 D3D11 输出需要水平和垂直翻转，Inspector 中的
`Flip Horizontally` / `Flip Vertically` 默认均已开启，可按摄像头调整。

相同 URL 和模式重复调用 `Play()` 是幂等的，不会销毁正在工作的会话。
真正切换 URL/模式时，停止、释放和重新创建会拆到多个 Unity 帧中执行；首次
LibVLC 初始化默认在 `Awake` 预热，避免把所有原生工作堆在按钮点击帧。

Windows 普通失焦在 `Run In Background` 开启时不会断流。真正的应用暂停、
首帧超时、画面卡死或 LibVLC 报错仍会释放旧 `MediaPlayer` 并创建新会话；
组件不会尝试复用暂停前的 RTSP socket。

Android 切后台、息屏或失焦会释放旧会话，恢复后按延时创建新的 RTSP 会话。
本次复用的源工程 Android 包已由用户确认在真机播放通过。当前独立工程已完成
Unity Android 脚本、IL2CPP 与 APK 打包验证，并已在 Xiaomi 24129PN74C / Android
16 安装启动；OpenGL ES 3 + Auto native texture 已取得首帧。该次公网流随后停帧，
因此 CPU/GPU/Auto 连续帧、息屏恢复和长时间运行仍需用可控 RTSP 源复测。

## 海康官方 SDK 后端（可选）

该后端针对 Windows x64，已按官方
`CH-HCNetSDKV6.1.9.48_build20230410_win64` 的当前头文件实现。海康专有 DLL
不在仓库中，需自行从海康官方 SDK 安装：

```powershell
.\scripts\setup-hikvision.ps1 -SdkPath 'D:\SDK\CH-HCNetSDKV6.1.9.48_build20230410_win64'
```

也可以设置 `HIKVISION_SDK_PATH` 后不传参数。脚本会核验 x64 DLL，并复制到
被 Git 忽略的 `External/HikvisionWindows`；Windows Player 构建时再复制到
`<Player>_Data/HikvisionWindows`。不安装海康 SDK 不影响 VLC 三种模式。

在 Demo 下拉框选择 `Hikvision SDK / PlayM4`，输入不含密码的设备端点：

```text
hikvision://admin@192.168.1.64:8000/1?stream=0
```

其中 `8000` 是 HCNetSDK 设备服务端口，`1` 是通道，`stream=0/1/2` 分别表示
主码流/子码流/第三码流。密码不会序列化到 Scene 或 Prefab，也禁止放进 URL；
通过环境变量或运行时 API 注入：

```powershell
$env:HIKVISION_PASSWORD = 'device-password'
```

```csharp
hikvisionPlayer.DeviceAddress = "192.168.1.64";
hikvisionPlayer.DevicePort = 8000;
hikvisionPlayer.Username = "admin";
hikvisionPlayer.Password = GetPasswordFromSecureConfiguration();
hikvisionPlayer.Channel = 1;
hikvisionPlayer.StreamType = 0;
hikvisionPlayer.Play();
```

组件遵循 `Init -> Login_V40 -> RealPlay_V40 -> PlayM4 -> Stop -> Logout -> Cleanup`
生命周期。海康回调线程只写入有界的最新帧缓冲，YV12 纹理由 Unity 主线程上传；
该接口没有 stride 字段，因此会明确拒绝尺寸不是紧凑
`width * height * 3 / 2` 的异常帧。这里新增的是 VLC Android；海康 Android
仍需要对应平台的海康 SDK/JNI 接入，不包含在当前实现中。

## 目录

- `Assets/VlcD3D11Rtsp/Runtime`：VLC 与海康播放器、回调、shader 和运行库封装
- `Assets/VlcD3D11Rtsp/Demo`：示例 UI 与无凭据 smoke harness
- `Assets/Plugins/Android/VLCUnity`：Android ARM64 LibVLC、Java AAR 和 Unity bridge
- `Assets/Plugins/VLCUnityRuntime`：Android 初始化、纹理 helper、link.xml 和许可
- `Native~/VLCUnityPlugin`：可审计的 VLC-Unity D3D11 原生源码
- `scripts`：依赖、原生 DLL、Unity player 和仓库校验脚本
- `docs`：架构、验证和依赖锁定说明

## 许可与分发

本仓库原创代码使用 [MIT License](LICENSE)。原生桥派生自 VideoLAN
VLC-Unity，继续使用 LGPL-2.1-or-later；托管 DLL 来自 LibVLCSharp，
LibVLC 运行库及其他第三方内容也不因项目采用 MIT 而改变原许可。
具体适用范围、固定版本、哈希和修改说明见
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) 与 [`LICENSES`](LICENSES)。

LibVLC 二进制包可能包含不同许可的可选 plug-in。默认依赖脚本会移除
`scripts/gpl-plugin-denylist.txt` 中列出的明显可选项，但这不构成完整法律
审计。发布二进制前应按实际保留的 plug-in 做一次清单审计并提供对应源码、
许可证和可替换/重链接方式。

海康 HCNetSDK/PlayCtrl 是用户本地提供的可选专有依赖，不受本仓库 MIT
License 覆盖。分发包含海康 DLL 的播放器前，需自行核对海康许可和目标项目的
再分发授权。
