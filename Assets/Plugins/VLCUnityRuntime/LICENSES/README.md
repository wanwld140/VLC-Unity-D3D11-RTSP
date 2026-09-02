# Upstream notices

本目录保留组件使用的上游许可证原文。它们不替代各源码文件自身的版权头、contrib 许可证或产品正式发行前的完整第三方合规审计。

## Windows x64

- Windows 原生运行库来自 VideoLAN 官方 VideoLAN.LibVLC.Windows 4.0.0-alpha-20260831 NUPKG。
- NUPKG 声明许可证为 LGPL-2.1-or-later。
- 本包保留官方 x64 运行数据：libvlc、libvlccore、plugins、lua 与 hrtfs；组件不会主动启用 Lua HTTP 控制接口。
- 对应 VLC 库许可证原文见 vlc-COPYING.LIB.txt。
- 组件自己的最小 VLCUnityPlugin.dll 源码随包位于 Assets/VLCUnityComponent/Documentation/WindowsBootstrap。

## Android ARM64

- vlc-unity-LICENSE.txt：VideoLAN vlc-unity，提交 f2bbedd5，LGPL-2.1-or-later / 上游声明。
- LibVLCSharp-LICENSE.txt：VideoLAN LibVLCSharp，提交 333a98a5。
- libvlcjni-LICENSE.txt 与 libvlcjni-COPYING.LIB.txt：固定 JNI/Java 源码，提交 6269075a。
- vlc-COPYING.LIB.txt 与 vlc-COPYING.txt：固定 VLC 源码根，提交 275bbed0。后者作为上游源码说明保留，不表示每个编译组件都使用同一许可证。
- LGPL-3.txt 与 GPL-3.txt：Android contrib 预设所需的标准许可证文本。

Android VLC 原生构建使用 libvlcjni --license l，上游描述为 LGPLv3 + ad-clauses。完整 contrib 源码和原始许可证仍保留在构建环境；可复现提交、Windows NUPKG URL 与二进制 SHA-256 见 Assets/VLCUnityComponent/Documentation/SOURCE_INFO.json。
