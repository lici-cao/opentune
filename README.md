# OpenTune 调优台

Windows 手感与延迟调优工具箱（开源版），全部操作本地完成，不联网上传任何数据。

- 作者：**lici-cao**（https://github.com/lici-cao）
- 版本：1.0.0
- 许可：MIT License
- 平台：Windows 7 / 10 / 11（x64），单文件免安装，约 38 KB

> 本工具是按公开的系统调优原理独立重写的开源实现，没有使用、复制或反编译任何第三方收费软件的代码与资源。

---

## 功能

| 模块 | 能力 |
| --- | --- |
| 概览 / 一键优化 | 前台程序优先、关闭 Xbox Game DVR、多媒体调度响应度、解除网络限流、视觉特效、电源计划切换、计时器分辨率、刷新 DNS |
| 备份 / 还原 | 每次修改前自动生成备份（注册表原值 + 当前电源计划），支持一键还原到最近备份 |
| 进程优先级 / 亲和 | 查看进程列表，设置优先级类（Idle→RealTime），设置 CPU 亲和掩码 |
| 缓存清理 | DirectX D3DSCache、NVIDIA DXCache/GLCache、AMD DxCache/DxcCache/GLCache、缩略图缓存、%TEMP%，先统计占用再清理 |
| 实时监控 | CPU 占用、DPC 占用、内存占用、GPU(3D) 占用、当前计时器分辨率（1.5 s 刷新） |
| 网络测速 | 延迟测试（Ping）+ 下载测速（Cloudflare speed 端点） |

## 安全设计

1. 任何注册表 / 电源计划修改**前**都会写入备份文件：`%LOCALAPPDATA%\OpenTune\backup\backup_yyyyMMdd_HHmmss.otbak`
2. 清理只作用于固定白名单目录，不触碰用户文档、游戏本体与系统级目录
3. 不写自启动、不注册服务、不驻留后台，关掉窗口即完全退出
4. 不含任何联网上报逻辑；唯一的网络行为是用户主动点击的测速

## 使用

1. 解压 `OpenTune-v1.0.0-win64.zip`
2. 右键 `OpenTune.exe` → **以管理员身份运行**（带 `[需管理员]` 前缀的项需要管理员权限）
3. 建议先点「立即备份」，再勾选需要的项，点「执行所选优化」
4. 部分项需要重启或注销后生效

## 命令行自检

```bat
OpenTune.exe --selftest %TEMP%\opentune-selftest.log
```

输出系统信息、调优项当前值、备份创建与还原验证、缓存占用统计、监控计数器与网络测速结果，用于确认工具在本机可用。

## 构建

无需 Visual Studio / .NET SDK，使用 Windows 自带的 .NET Framework 4.x 编译器即可：

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 ^
  /target:winexe /optimize+ /win32manifest:app.manifest ^
  /out:OpenTune.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Management.dll ^
  src\OpenTune.cs
```

## 目录结构

```
OpenTune/
├─ src/OpenTune.cs      全部源码（单文件）
├─ app.manifest         清单（Win10/11 版本识别、DPI 感知、通用控件 v6）
├─ OpenTune.exe         编译产物
├─ README.md
└─ LICENSE
```

## 免责声明

修改系统参数存在风险。工具会在每次修改前自动备份，但仍建议在了解每一项作用后再执行，并自行保留系统还原点。因使用本工具造成的任何后果由使用者自行承担。

## 许可

MIT License，详见 LICENSE。保留作者署名即可自由使用、修改与分发。
