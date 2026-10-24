<div align="center">
  <img src="src/Mosaic.Host/Assets/Mosaic.svg" width="72" alt="Mosaic Desktop" />
  <h1>Mosaic Desktop</h1>
  <p>让控件留在桌面，让应用各就其位。</p>
  <p>Windows 11 · C# / .NET 10 · WPF · 开发预览 v0.7.0</p>
</div>

Mosaic Desktop 是一款可切换的 Windows 桌面覆盖软件。它保留你的壁纸，把时钟、笔记、待办和系统监控固定在桌面层，用应用 Dock 和顶部菜单栏承接日常操作。主界面负责设置、插件管理、资源和工作空间。

启用 Mosaic 时，原 Windows 任务栏与桌面图标隐藏；切回 Windows 或退出后恢复。应用窗口仍按正常方式使用，桌面控件位于应用后面。

## 当前功能

- **固定桌面控件**：时钟、随手记、待办、CPU、内存、网络、存储和快捷访问。平时锁定；进入「编辑桌面布局」后移动或缩放，支持完成保存、取消和 Escape 恢复。
- **应用 Dock**：读取 Windows 任务栏固定应用，合并运行窗口，展示新打开的应用；支持窗口切换、24–80 图标大小、横向滚动和原始应用图标。
- **按场景显示的菜单栏**：桌面或没有最大化应用时，Dock 与顶部菜单保持展开；有最大化或全屏应用且当前不在桌面时，按各自设置自动隐藏。移至屏幕边缘可以唤出，使用菜单时保持展开。
- **可恢复的桌面模式**：Mosaic 模式下单按左/右 Win 或 Ctrl+Esc 不打开开始菜单，保留 Win+D、Win+E 等组合快捷键。切回 Windows 后解除拦截；覆盖期间另有恢复进程处理主进程异常退出。
- **全局搜索**：本地文件与应用搜索、收藏、标签和最近使用，可选接入已安装的 Everything。
- **资源与工作空间**：登记文件、文件夹和项目，使用普通、智能或项目集合组织资源，保存与切换 Dock 和工作空间配置。笔记与待办共享。
- **本地数据**：设置、内容和资源元数据使用 JSON，文件索引使用可重建 JSONL；不依赖数据库或浏览器运行时。

## 从源码开始

环境：**Windows 11 x64、PowerShell、.NET 10 SDK**。SDK 版本选择见 [global.json](global.json)。

```powershell
git clone https://github.com/chuanlongZhou/mosaic-desktop.git
cd mosaic-desktop

# 已安装 .NET 10 SDK 时可直接构建
.\scripts\build.ps1
.\scripts\run.ps1 -Dashboard
```

在设置中心点击「启用 Mosaic 桌面」，或使用 `Ctrl+Alt+M` 切换。通过插件管理开启控件，从「编辑桌面布局」调整位置。

如果未安装 SDK，可以先运行以下脚本。它将 SDK 安装到项目的 `.tools/dotnet`，项目脚本会优先使用这个本地副本。

```powershell
.\scripts\setup.ps1
```

### 构建运行包

```powershell
.\scripts\publish.ps1
```

输出位于 `artifacts/Mosaic-v0.7.0-win-x64/`，同级生成 ZIP。运行包需要 **.NET 10 Desktop Runtime**，不包含运行时或 SDK。

源码仓库不跟踪本机 SDK、编译输出和运行包。

## 日常操作

| 操作 | 入口 |
| --- | --- |
| 切换 Mosaic / Windows | `Ctrl+Alt+M` |
| 打开设置与插件管理 | `Ctrl+Alt+D` |
| 打开全局搜索 | `Ctrl+Alt+Space`，可在搜索设置修改 |
| 返回系统桌面 | `Win+D` |
| 调整桌面控件 | 插件管理或顶部菜单 → 编辑桌面布局 |
| 取消布局调整 | 编辑工具栏的「取消」或 `Escape` |
| 退出软件 | 顶部 MOSAIC 菜单或托盘菜单 |

设置中可以分别关闭 Dock 或顶部菜单的自动隐藏，让它们始终显示。自动隐藏开启时，顶部菜单通过屏幕顶边呼出，Dock 通过主屏底边呼出。

### 其他入口与隔离试用

```powershell
.\scripts\run.ps1 -Palette
.\scripts\run.ps1 -Library
.\scripts\run.ps1 -Workspaces

# 使用单独的数据目录，保留日常配置
.\scripts\run.ps1 -Dashboard -DataDirectory .\.artifacts\preview-data

# 保存后退出默认数据目录对应的实例
.\scripts\run.ps1 -Exit
```

可执行文件支持 `--overlay`、`--dashboard`、`--palette`、`--library`、`--workspaces`、`--exit` 和 `--data-dir <目录>`。

## 数据与恢复

默认数据目录为 `%LOCALAPPDATA%\Mosaic`。设置、控件内容、工作空间、资源元数据和索引保存在该目录；源码目录与用户数据分开。

JSON 保存包含结构校验、版本与外部修改检查、临时文件替换和备份恢复。资源库登记资源的位置与元数据，不搬移原文件。任务栏固定应用只读取，不修改 Windows 固定配置。

## 开发与验证

```powershell
.\scripts\build.ps1

# 先退出正在运行的 Mosaic，或切回 Windows 桌面
.\scripts\test.ps1
```

当前完整检查为 **29/29 通过**。测试涵盖 JSON 恢复和冲突、布局、搜索、资源和工作空间、WPF 交互、桌面控件层级、菜单栏与 Dock 状态、按键序列，以及正常退出和强制终止后的 Windows 任务栏与工作区恢复。

部分检查会在 Windows 桌面上创建隔离的测试窗口并切换覆盖模式，需要可交互的桌面会话。自动检查不等同于所有设备上的长期稳定性验收，也不发送真实 Win 按键。

```text
src/
  Mosaic.Host/              WPF 界面、桌面宿主、Dock 与系统集成
  Mosaic.Core/              模型、命令、布局与搜索类型
  Mosaic.Platform.Windows/  Windows 应用发现、启动、系统采样与单实例
  Mosaic.Storage.Json/      JSON 存储、资源元数据与文件索引
tests/Mosaic.Tests/         自动检查与 Windows 集成检查
scripts/                   SDK 准备、构建、运行、测试与打包
docs/                      设计、实现复核与开发历史
```

## 当前边界

- 当前为开发预览；桌面控件画布与 Dock 主要覆盖主屏，顶部菜单栏支持各屏幕。
- 控件目前为内置组件，不加载第三方插件代码；桌面控件位置属于全局设置。
- 工作空间不是 Windows 虚拟桌面；切换不自动启动、关闭或恢复第三方应用。
- 多屏混合 DPI、Explorer 重启、锁屏与唤醒、独占全屏和长期驻留仍需扩展实机验证。
- 尚未提供签名安装包、自动更新和完整主题编辑器。

## 文档

- [桌面栏与开始菜单行为](docs/SHELL_BEHAVIOR_V0.7.md)
- [固定桌面控件实现复核](docs/DESKTOP_REVIEW_2026-10-24.md)
- [开发与验证记录](docs/DEVELOPMENT.md)
- [版本与开发历史](docs/CHANGELOG.md)
- [项目实施文档 V2.1](docs/Mosaic_Desktop_项目实施文档_V2.1.md)
