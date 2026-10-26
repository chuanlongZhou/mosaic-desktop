# DeepSeek Q 版整点语音报时（肥鱼配音测试）

入口：设置中心 → 桌宠 → 选择 DeepSeek · Q 版 → DeepSeek Q 版 · 整点报时。
勾选「每到整点，用语音提醒我」，设置音量后保存。默认关闭；已有开关和音量设置继续保留。
「立即试听当前整点」可直接测试，无需保存或开启报时；桌宠右键也有「试听整点报时」。

## 当前配音

采用 [astrbot_plugin_feiyu_voice](https://github.com/Esmerubphire/astrbot_plugin_feiyu_voice) 对应的肥鱼 CosyVoice3 声库，选择 `cute`（可爱）声线，使用发布者的 F6 checkpoint 与 N01 合成参考音频。
模型来源：[Esmerubphire/FeiyuVoice-CosyVoice3](https://huggingface.co/Esmerubphire/FeiyuVoice-CosyVoice3)。原始声源与训练授权说明归属该项目及 B 站 UP 主 [星落落_oi](https://space.bilibili.com/30584746/)。这是模型合成配音。

24 条报时语音已预先生成，日常播放完全离线，无需运行 AstrBot、Python、声音服务或占用显卡推理。
台词随小时变化，含喝水、午饭、休息及晚安提醒。素材位于 `src/Mosaic.Host/Assets/Voice/DeepSeekChibi/`，`lines.json` 记录文字、声线、模型/参考哈希与各音频的 SHA256；`generation.json` 记录实际生成环境。

生成采用模型原始 24 kHz 输出，再高质量重采样为兼容现有播放器的 22.05 kHz、16 位单声道 WAV，不额外变调或变速。

## 重新生成（构建侧）

Windows 构建侧依赖快照见 `scripts/feiyu-windows-requirements.txt`。使用独立的 Python 3.11.17、CUDA torch/torchaudio 2.11.0+cu128、发布者指定的 CosyVoice 与 Matcha 提交，以及完整、通过 SHA256 校验的肥鱼模型包。
执行 `scripts/generate-hourly-chime-voice.ps1`；可以通过 `-Python`、`-RuntimeRepo`、`-ModelPackage`、`-ServiceSource` 指定既有环境和模型位置。
脚本先在临时目录生成，24 条全部成功后才替换项目素材。普通构建只复制成品 WAV，无需安装合成环境。

本机使用 Windows 原生 CUDA 环境与 RTX 4070 Laptop GPU，FP32 推理、CPU ONNX 前端。Windows 上通过 SoundFile 适配 torchaudio 2.11 的 WAV 读取接口，模型权重、参考文件和上游源码保持不变。未使用该项目的 WSL 安装器，也未安装 RVC、唱歌组件或常驻 HTTP 服务。

## 播放规则

按当前电脑本地时间在整点播放，允许 30 秒以内的短暂调度延迟。同一小时只播一次，多个 DeepSeek Q 版或 mk2 仅由第一位符合条件的伙伴播报。Deepseek Q版 mk2 使用同一组语音和整点调度器，原 Q 版与 mk2 同时显示也只播一次；其他角色暂不发声。
伙伴必须启用并显示，音量大于零；全屏时跟随这位伙伴的「全屏游戏或视频时自动藏起来」设置。收起、关闭报时、拖动或全屏隐藏期间错过的整点不补播。
启动、休眠恢复、系统时钟回拨和时区切换不追补错过的小时。试听和自动报时共用播放器，避免叠音。
语音缺失或设备不可用时显示错误信息；静音与设备音量仍由 Windows 控制。

验证：`Mosaic.Tests.exe --hourly-chime-check <报告目录>`。
覆盖小时边界/去重、跨天、隐藏和关闭、唤醒、回拨/时区、旧设置迁移/保存/副本、角色限制、24 段音频的文字、生成哈希、PCM 格式与非静音检查，以及 WPF 音频解码和设置面板渲染。
