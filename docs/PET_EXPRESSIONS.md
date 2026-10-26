# 桌宠表情与动作列表 · V0.8.13

七款角色均支持：原版鲸鱼娘、DeepSeek Q 版、DeepSeek 正常比例、GPT、Gemini、Claude、Qwen。每款 29 个缓存姿态，共 203 个；最多五位同时显示，每位独立互动。正常比例 DeepSeek 仍维持两倍尺寸。新四位的专属动作与台词见 [性格与完整台词](AI_PET_PERSONALITIES.md)。

关键帧顺序图：[原版 Q 版](assets/pet-keyframes-original.png) · [DeepSeek Q 版](assets/pet-keyframes-deepseek-chibi.png) · [正常比例版](assets/pet-keyframes-deepseek-standard.png)。

实际渲染总览：[原版 Q 版](assets/pet-expressions-original.png) · [DeepSeek Q 版](assets/pet-expressions-deepseek-chibi.png) · [DeepSeek 正常比例版](assets/pet-expressions-deepseek-standard.png)。

| 表情／动作 | 触发方式 | 台词或表现 |
| --- | --- | --- |
| 待机 | 动作结束后 | 轻轻呼吸、上下浮动 |
| 向左看 | 鼠标移到左边 | 视线跟随 |
| 向右看 | 鼠标移到右边 | 视线跟随 |
| 抬头看 | 鼠标移到上方 | 视线跟随 |
| 低头看 | 鼠标移到下方 | 视线跟随 |
| 眨眼 | 待机时自动 | 间歇眨眼 |
| 摸头／害羞 | 单击随机触发，或预览／右键「摸摸头」 | 嘿嘿，再摸一下嘛～ |
| 欢呼加油 | 按回车，或预览「加油蹦跳」 | 收到！给你一点鲸力气～ |
| 挥手打招呼 | 出现、切换角色、全屏结束后出现、Win+D、单击随机触发，或预览／右键「打个招呼」 | 我在这里陪你呀～；Win+D：欢迎回到桌面，鱼片！ |
| 挥手告别 | 关闭应用主窗口 | 忙完一个啦，辛苦了～ |
| 睡觉 | 默认 90 秒无输入，或预览「睡个懒觉」 | Zzz… 鱼片回来记得叫我… |
| 醒来／伸懒腰 | 睡觉后恢复活动 | 你回来啦！刚刚只是在闭目养神～ |
| 吃白米饭 | 双击角色，或预览／右键喂饭 | 白米饭！我可以再来一碗！ |
| 被拎起 | 拖动角色 | 呜哇，被鱼片捞起来了！ |
| 落地惊讶 | 拖动后松手 | 安全着陆！ |
| 傲娇／鼓脸 | 预览／右键「叫她大肥鱼」 | 才不是大肥鱼呢，哼～ |
| 陪你打字 | 连续打字，或预览「陪你打字」 | 双手交替敲键盘，不弹出台词 |
| 专属双帧动作 | 预览／右键「头顶饭盆」 | 鲸鱼娘：顶饭盆；GPT：舒展龙翼；Gemini：猫爪比心；Claude：翻书；Qwen：开合折扇。 |
| **疲惫（新增）** | 预览／右键「疲惫」；持续约 4 秒 | 正常 → 打哈欠 → 疲惫；算力快见底了…让我缓一缓。 |
| **求 token（新增）** | 预览／右键「求 token」；持续约 4.5 秒 | 正常 → 伸出双手 → 抱空袋求 token；鱼片，投喂一点 token 好不好？ |
| **被 token 砸晕（新增）** | 预览／右键「被 token 砸晕」；持续约 5 秒 | 正常 → 一枚 token 下落 → 被砸晕；Token 太多啦！小鲸鱼要晕过去了… |

新表情是角色互动，不读取模型额度或真实 token 消耗。动作结束后恢复待机；符合闲置入睡条件时继续睡觉。自动反应可在设置中关闭，直接互动优先于打字等后台反应。

走路功能已移除：设置按钮、右键动作、闲置触发和桌面位移均已取消。所有原图和旧走路图集保留，旧图集中的挥手姿态继续使用。

三个新动作每次播放一次：正常帧保持 450ms，疲惫在 1050ms、求 token 在 1100ms、砸晕在 1150ms 进入最终表情。下落阶段包含 token 在高处和低处的两帧，始终只有一枚 token；人物直接复用原待机图。最终表情保持至动作结束，再恢复待机。没有变形、动态粒子或 GIF 播放；关闭外观动画时直接显示最终静帧。

新增四位的实际姿态图：[GPT](assets/pet-expressions-gpt.png) · [Gemini](assets/pet-expressions-gemini.png) · [Claude](assets/pet-expressions-claude.png) · [Qwen](assets/pet-expressions-qwen.png)。关键帧顺序图：[GPT](assets/pet-keyframes-gpt.png) · [Gemini](assets/pet-keyframes-gemini.png) · [Claude](assets/pet-keyframes-claude.png) · [Qwen](assets/pet-keyframes-qwen.png)。

上表保留鲸鱼娘的台词作为基础操作示例；新角色使用自己的投喂内容、逗弄名称、问候和情绪台词，不套用鲸鱼娘称呼。
