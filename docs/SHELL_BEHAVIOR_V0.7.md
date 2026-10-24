# V0.7 桌面栏与开始菜单行为

按用户要求，Dock 和顶部菜单栏共享桌面状态判断：

| 当前状态 | 结果 |
| --- | --- |
| 系统桌面在前台，包括 Win+D 返回桌面 | 两条栏展开，取消隐藏计时；后台保留最大化窗口也不影响此规则 |
| 没有可见、未最小化的最大化或全屏应用 | 两条栏保持显示 |
| 有最大化/全屏应用，且当前不在桌面 | 各自按原有自动隐藏设置工作，保留边缘唤出、悬停和菜单使用保护 |
| 关闭某条栏的自动隐藏设置 | 这条栏始终展开 |

`DesktopSceneMonitor` 通过前台窗口和原生窗口枚举获取状态，忽略系统桌面、工具窗口、最小化、隐藏和 DWM cloaked 窗口。窗口事件触发及时更新，350ms 定时检查补充后台窗口状态变化。Mosaic 自己的工具菜单获得焦点时沿用此前的桌面状态，防止打开菜单改变两条栏的状态。当前按所有屏幕的应用统一判断。

临时展开不注册新的 AppBar、不改变最大化工作区，避免展示栏位本身引发其他窗口缩放。原有“始终显示”的 AppBar 预留逻辑保留。

在 Mosaic 桌面覆盖模式内，专用线程安装临时 `WH_KEYBOARD_LL`：单按左/右 Win 时使用无功能菜单屏蔽键并正确释放 Win，保留 Win+D、Win+E、Win+方向键等组合；Ctrl+Esc 也被拦截，Ctrl+Shift+Esc 和普通 Esc 不受影响。切回 Windows 或退出时卸载钩子，不写系统键位注册表。主进程被结束后，系统会清理进程的钩子。

验证包括桌面/有无最大化应用的策略组合、真实窗口最大化/最小化/隐藏判断、Dock/顶部栏的展开与延迟收起、取消旧隐藏计时、菜单展开保护、最大化应用场景下的原生顶部栏隐藏、Win 按键序列与组合键保留，以及钩子安装、卸载、重新安装。自动检查不发送真实 Win 按键，也不把这些检查描述为物理键盘的端到端验收。

Release 构建 0 警告、0 错误；完整检查 29/29 通过。运行包为 `artifacts/Mosaic-v0.7.0-win-x64`。

参考：[Microsoft 键盘钩子回调](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)、[DWM cloaked 窗口](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)、[AutoHotkey 菜单屏蔽键说明](https://raw.githubusercontent.com/AutoHotkey/AutoHotkeyDocs/v2/docs/lib/A_MenuMaskKey.htm)。
