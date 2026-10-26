# Deepseek Q版 mk2

来自 [SchneeHertz/pi-deepseek-pet](https://github.com/SchneeHertz/pi-deepseek-pet)，原始素材来自 [PC2005-cloud/dsh-pet](https://github.com/PC2005-cloud/dsh-pet)。原版 106 段 640×360、24fps VP9 Alpha WebM 直接复用，无重绘或视频重编码。

在设置中心 → 桌宠中选择 **Deepseek Q版 mk2**，保存后使用。可与原 DeepSeek Q 版同时显示，各自保存位置、大小和反应。

- 点击、悬停、打字、投喂、拖动、睡眠和唤醒对应原版动画；原有动画结束后进入呼吸待机与随机小动作。
- 设置中的原版动作库和桌宠右键菜单可预览全部 106 段动作。
- 共用原 DeepSeek Q 版的肥鱼 cute 配音文件、播放器和整点调度器；两种 Q 版同时启用时，每个整点只播一次。报时开关和音量仍随各自伙伴保存。
- 遵循外观动画开关和全屏隐藏设置。关闭动画时使用静态海报，收起时释放视频解码器。
- 保持 16:9 动画画布与透明像素穿透；清除 VP9 压缩产生的 alpha 1–2 背景残留，保留人物边缘透明度。

这是 Mosaic 原生桌宠角色。Pi HTTP bridge、Pi 状态联动与自动漫游窗口未接入；原素材中的走路动作仍可在动作库播放，桌面位置由用户拖动决定。

## 素材与运行库许可

原始动画与衍生视觉素材仅限非商业使用。完整条款与署名保留于运行包 `licenses/Pi-DeepSeek-Pet-ASSET_LICENSE.md`、`licenses/Pi-DeepSeek-Pet-MIT-LICENSE.txt`；设置页亦保留提示。不是将动画素材改为 MIT 授权。

视频解码使用动态链接 FFmpeg LGPL 共享库和 FFmpeg.AutoGen 绑定；运行包保留对应许可。无需安装 Electron、浏览器运行时或视频编解码器。

## 开发与验证

- 原素材导入：`scripts/import-mk2-animations.py --source <pi-deepseek-pet目录> --output src/Mosaic.Host/Assets/Pet/DeepSeekChibiMk2`。
- 构建侧解码库准备：`scripts/setup-mk2-runtime.ps1`，校验固定 FFmpeg 构建与库文件哈希。
- 专项检查：`Mosaic.Tests.exe --mk2-check <报告目录>`；涵盖 106 段原文件哈希、透明解码、动态帧、独立播放、拖动、关闭动画、配置重启、设置面板与共享报时。
- 既有十一款角色继续使用原来的静态姿态缓存与关键帧逻辑；mk2 使用独立原视频播放器。
