<div align="center">
  <img src="src/Mosaic.Host/Assets/Mosaic.svg" width="72" alt="Mosaic Desktop" />
  <h1>Mosaic Desktop</h1>
  <p>让控件留在桌面，让应用各就其位。</p>
  <p>Windows 11 · C# / .NET 10 · WPF · 开发预览 v0.8.17</p>
</div>

Mosaic Desktop 是一款可切换的 Windows 桌面覆盖软件。它保留你的壁纸，把时钟、笔记、待办和系统监控固定在桌面层，用应用 Dock 和顶部菜单栏承接日常操作。主界面负责设置、插件管理、资源和工作空间。

启用 Mosaic 时，原 Windows 任务栏与桌面图标隐藏；切回 Windows 或退出后恢复。应用窗口仍按正常方式使用，桌面控件位于应用后面。

## 当前功能

- **二十一款 AI 娘桌宠，最多五开**：保留原有十一款与新增的 Deepseek Q版 mk2，并加入 Grok、Kimi、智谱社区图集及六款 CrossPet 画风。新增想一想、阅读、搜索、绘画、伸展、跑步和哼歌动作；原有形象以现有姿态响应。每位独立保存位置、大小和反应。社区图集仅供非商业使用并保留来源与署名，见 [社区素材许可](packaging/licenses/Community-Pets-NOTICE.md)。桌宠设置和历史设计详见 [桌宠说明](docs/DESKTOP_PET.md)、[素材说明](src/Mosaic.Host/Assets/Pet/README.md)。
- **Deepseek Q版 mk2**：导入 pi-deepseek-pet 的 106 段原版透明动画，提供动作库与原生播放，和原 Q 版共享肥鱼配音、整点报时与去重。素材仅限非商业使用，详见 [mk2 说明](docs/DEEPSEEK_MK2.md)。
- **DeepSeek Q 版系列整点语音报时（测试）**：桌宠设置中开启，提供独立音量和立即试听。24 条离线肥鱼可爱声线配音，多开只播一次；跟随全屏隐藏设置，启动和休眠恢复后不补报。详见 [报时说明](docs/HOURLY_CHIME.md)。
- **固定桌面控件**：时钟、随手记、待办、CPU、内存、GPU、显存、温度、风扇 RPM、网络、存储和快捷访问。平时锁定；进入「编辑桌面布局」后移动或缩放，支持完成保存、取消和 Escape 恢复。
- **应用 Dock**：读取 Windows 任务栏固定应用，合并运行窗口，展示新打开的应用；同一应用打开多个窗口时显示数量，点击图标可按窗口标题选择；支持 24–80 图标大小、横向滚动和原始应用图标。
- **按场景显示的菜单栏**：桌面或没有最大化应用时，Dock 与顶部菜单保持展开；有最大化或全屏应用且当前不在桌面时，按各自设置自动隐藏。顶部菜单展开时预留工作区，窗口从其下方开始，收起后释放空间；边缘唤出与菜单使用期间同样占位。
- **右侧状态与系统托盘**：日期时间、输入语言、网络、音量和电池状态集中在顶部右侧。主屏的「⌃」展开后台应用的真实托盘图标，支持左键、右键和双击；点击时间查看日历。托盘由 ManagedShell 接收应用通知，切回 Windows 或退出时交还系统。
- **可恢复的桌面模式**：Mosaic 桌面模式下，单按左／右 Win 同时展开 Dock 和所有屏幕的顶部菜单，并保持显示；再次单按 Win 结束临时固定。用户在设置中常驻固定的顶部菜单或 Dock 不受 Win 键影响。自动隐藏开启时仍可从屏幕边缘唤出。长按只在松开时切换一次。Win+D、Win+E 等组合键保留，Ctrl+Esc 继续拦截开始菜单。切回 Windows 或退出后解除拦截；临时显示状态不写入配置，覆盖期间另有恢复进程处理主进程异常退出。
- **全局搜索**：本地文件与应用搜索、收藏、标签和最近使用，可选接入已安装的 Everything。
- **资源与工作空间**：登记文件、文件夹和项目，使用普通、智能或项目集合组织资源，保存与切换 Dock 和工作空间配置。笔记与待办共享。
- **本地数据**：设置、内容和资源元数据使用 JSON，文件索引使用可重建 JSONL；不依赖数据库或浏览器运行时。

## V0.8 外观、监控与交互

- **主题组合**：设置中心 → 设置 → 外观，提供午夜蓝、石墨灰、森林绿、暮光紫、晨光浅色五种主题，以及五种强调色。选择后即时预览，取消时恢复原设置。
- **透明与动画**：透明质感、不透明度和界面动画分别设置。顶部菜单滑动与窗口让位同步缓动，支持中途反向；关闭动画立即切换。关闭透明后使用实色表面，原生窗口材质取决于 Windows 的支持和系统设置。
- **系统监控**：CPU、内存、GPU、专用显存、温度、风扇 RPM、网络和存储。GPU 优先读取 NVIDIA NVML，其他驱动使用 Windows GPU 性能计数器；非 NVIDIA 显存显示适配器专用内存合计。
- **温度与转速**：NVIDIA 驱动可直接提供 GPU 核心温度；Windows ACPI 可能提供热区温度。CPU/主板温度和风扇 RPM 可读取已运行的 Libre Hardware Monitor / Open Hardware Monitor 的本机 WMI 数据。没有对应接口时显示“—”及原因；ACPI 热区不冒充 CPU 温度，风扇百分比不冒充 RPM。应用不会安装硬件驱动或自动提权。
- **快捷键**：设置 → 快捷键，直接录入桌面切换、设置中心、搜索、资源库、工作空间和布局编辑快捷键；Backspace 清除，支持恢复默认。重复或系统占用时阻止修改并保留原绑定；清除搜索快捷键后仍可通过按钮使用搜索。
- **桌面编辑**：网格吸附、撤销/重做、恢复默认；编辑模式聚焦组件后可用方向键微调，Shift 加方向键每次移动 10 DIP。工作空间分别保存组件布局，笔记与待办继续共享。
- **日常操作**：组件卡片展示实时预览；待办可编辑、隐藏已完成及撤销删除；笔记与待办在桌面显示保存状态和重试。资源库支持文件拖入、Enter 打开和 Ctrl+C 复制路径。

## 从源码开始

环境：**Windows 11 x64、PowerShell、.NET 10 SDK**。SDK 版本选择见 [global.json](global.json)。首次构建通过 NuGet 还原固定版本的 ManagedShell 和依赖，需要联网。

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

输出位于 `artifacts/Mosaic-v0.8.17-win-x64/`，同级生成 ZIP；发布成功后只保留当前版本的目录与 ZIP，旧版本发行包自动清理，正在运行的旧版暂时保留。运行包需要 **.NET 10 Desktop Runtime**，不包含运行时或 SDK。

源码仓库不跟踪本机 SDK、编译输出和运行包。

## 日常操作

| 操作 | 入口 |
| --- | --- |
| 切换 Mosaic / Windows | `Ctrl+Alt+M` |
| 打开设置与插件管理 | `Ctrl+Alt+D` |
| 打开全局搜索 | `Ctrl+Alt+Space`，可在搜索设置修改 |
| 打开资源库 | `Ctrl+Alt+L` |
| 打开工作空间 | `Ctrl+Alt+W` |
| 显示并保持 Dock 与顶部菜单／再次收起 | 单按 `Win` |
| 返回系统桌面 | `Win+D` |
| 调整桌面控件 | `Ctrl+Alt+G`，或插件管理／顶部菜单 → 编辑桌面布局 |
| 取消布局调整 | 编辑工具栏的「取消」或 `Escape` |
| 退出软件 | 顶部 MOSAIC 菜单或托盘菜单 |

表中是默认快捷键；六组 Mosaic 全局快捷键均可在「设置 → 快捷键」录入、清除或恢复默认。设置中可以分别关闭 Dock 或顶部菜单的自动隐藏，让它们始终显示。自动隐藏开启时，顶部菜单通过屏幕顶边呼出，Dock 通过主屏底边呼出。

顶部右侧的时间打开日历，「⌃」展开主屏后台托盘图标；输入语言可切换当前应用的键盘布局，网络、音量和电池入口打开对应 Windows 设置。

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

此前完整检查为 **37/37 通过**。V0.8.17 的 Release 构建、Dock 双窗口检查和顶部菜单固定／工作区专项检查通过；实测本机 Chrome 的 5 个窗口归入同一 Dock 项。整套界面回归在本桌面会话的焦点与弹出菜单时序断言上仍未稳定通过，专项检查单独运行通过。新增[桌宠配置、动作与原生窗口检查](docs/DESKTOP_PET.md)。测试同时涵盖 JSON 恢复和冲突、外观与快捷键迁移、硬件监控可用／不可用状态、工作空间独立布局、搜索、资源、WPF 交互、托盘回调、顶部工作区占位，以及正常退出和强制终止后的 Windows 恢复。此前验证见 [V0.8 验证记录](docs/EXPERIENCE_V0.8.md#最终验证)。

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

## 独立场景演示

[图书馆茶室](docs/PET_WORLD_DEMO.md)复用五位 Q 版角色，提供六章、48 幕的 2.5D 场景、导航和互动。运行 `scripts/run-pet-world-demo.ps1`；这是独立窗口演示，尚未挂载到壁纸层。程序输出与验证报告位于本地 `output/`，不纳入源码仓库。

## 当前边界

- 当前为开发预览；桌面控件画布与 Dock 主要覆盖主屏，顶部菜单栏支持各屏幕。
- 控件目前为内置组件，不加载第三方插件代码；桌面控件的开关、位置和大小可以随工作空间切换。
- 工作空间不是 Windows 虚拟桌面；切换不自动启动、关闭或恢复第三方应用。
- 多屏混合 DPI、Explorer 重启、锁屏与唤醒、独占全屏和长期驻留仍需扩展实机验证。
- 尚未提供签名安装包、自动更新和完整主题编辑器。

## 文档

- [正常比例角色与素材说明](docs/NORMAL_PET_VARIANTS.md)
- [图书馆茶室场景演示与验证](docs/PET_WORLD_DEMO.md)

- [V0.8 系列外观、快捷键、监控与托盘指南](docs/EXPERIENCE_V0.8.md)
- [桌面栏与开始菜单行为](docs/SHELL_BEHAVIOR_V0.7.md)
- [固定桌面控件实现复核](docs/DESKTOP_REVIEW_2026-10-24.md)
- [开发与验证记录](docs/DEVELOPMENT.md)
- [版本与开发历史](docs/CHANGELOG.md)
- [V0.7 交互评审与后续落实说明](docs/UX_REVIEW_2026-10-24.md)
- [项目实施文档 V2.1](docs/Mosaic_Desktop_项目实施文档_V2.1.md)
