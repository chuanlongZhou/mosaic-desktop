# AI 娘桌宠素材

## 当前社区扩展（2026-10-26）

原有 11 款素材继续保留，另加 9 款可选形象：`GrokCommunity`、`KimiCommunity`、`ZhipuCommunity` 来自 fywdy 的 [chatgpt-chibi-pets](https://github.com/fywdy/chatgpt-chibi-pets)；`CrossPetGpt`、`CrossPetClaude`、`CrossPetDeepSeek`、`CrossPetGemini`、`CrossPetQwen`、`CrossPetGlm` 来自 [CrossPet](https://github.com/lokicorvus/crosspet)。每款有 `atlas.png`、`frames.json` 和 `provenance.json`。Mosaic 离线读取 PNG 图集；外部 WebP 原图不在运行时下载。

现有 29 个姿态之外，增加想一想、读一读、找线索、画一画、伸懒腰、跑起来、哼首歌七个手动动作。CrossPet 形象使用对应的原画动作；fywdy 的角色使用其 v2 图集中的等待、审查、奔跑、跳跃等格。原有 11 款为新动作使用最接近的已有姿态。所有角色仍保留原先的自动响应和交互。

转换脚本为 [import_community_pets.py](../../../../../tools/import_community_pets.py)，来源、原作者、各自的非商业许可证及修改说明见 [Community-Pets-NOTICE.md](../../../../../packaging/licenses/Community-Pets-NOTICE.md)。CrossPet 衍生图集以 CC BY-NC-SA 4.0 提供，fywdy 衍生图集以 CC BY-NC 4.0 提供；许可证分别适用于对应目录，不改变其他素材或代码的许可。

## 当前：四位正常比例伙伴（V0.8.15）

新增 [GptStandard](GptStandard/idle.png)、[GeminiStandard](GeminiStandard/idle.png)、[ClaudeStandard](ClaudeStandard/idle.png)、[QwenStandard](QwenStandard/idle.png)。每套包含 idle.png、social.png、actions.png 与 frames.json，覆盖 27 幅独立绘制的成年动漫比例关键帧，再复用本角色待机图与单枚 token 组成两帧下落。各角色共有 29 个缓存姿态，十一款共 319 个；所有正常比例版默认显示高度为 Q 版两倍。

使用内置 image_gen，根据各参考图左侧正常比例身份生成；完整提示词、排版修订见 [generation-prompts-standard-companions.txt](generation-prompts-standard-companions.txt)。分析脚本只记录透明轮廓的矩形区域，没有修改 PNG 像素。Q 版与所有旧 PNG 保留。同一角色两个比例共用性格台词、投喂与专属动作；最多五开可混合比例。

正常版姿态与说明见 [完整列表](../../../../../docs/NORMAL_PET_VARIANTS.md)，来源继续见 [署名记录](../../../../../packaging/licenses/AI-Companions-NOTICE.md)。

## Q 版素材（V0.8.12，原图保留）

保留全部鲸鱼娘素材，新增 [Gpt](Gpt/idle.png)、[Gemini](Gemini/idle.png)、[Claude](Claude/idle.png)、[Qwen](Qwen/idle.png) 四个目录。每个目录包含 idle.png、social.png、actions.png 和 frames.json，分别覆盖九个独立姿态，共 27 幅绘制关键帧；另外两帧 token 下落直接复用各自的 Idle 与同一枚 token。四套身份来自 References 中的完整 v5 参考图右侧 Q 版。

内置 image_gen 生成，保留透明通道。完整提示词及两张图集的排版修订见 [generation-prompts-ai-companions.txt](generation-prompts-ai-companions.txt)。Python 仅分析透明连通轮廓和矩形取图区域，不修改图片像素。角色动作分别包含舒展龙翼、猫爪比心、翻书、开合折扇，台词由本项目按气质另行编写。每款 29 个缓存姿态，七款共 203 个；最多五位同时显示。

来源、参考图哈希与署名条件见 [新角色来源记录](../../../../../packaging/licenses/AI-Companions-NOTICE.md)；四款不套用 DeepSeek 的 CC 协议。性格与全部台词见 [设计说明](../../../../../docs/AI_PET_PERSONALITIES.md)。

## 关键帧记录（V0.8.11）

移除上一版的变形、粒子与 GIF 效果。新增每款两幅准备姿态：打哈欠 `TiredYawn`、伸手 `BegReach`。素材使用内置 image_gen 参考各自已有 idle 图集的左上角色生成；完整提示词见 [generation-prompts-keyframes.txt](generation-prompts-keyframes.txt)。

- [原版过渡帧](whale-keyframes.png) 与 `whale-keyframes.json`。
- [DeepSeek Q 版过渡帧](DeepSeekChibi/keyframes.png) 与 `DeepSeekChibi/keyframes.json`。
- [DeepSeek 正常比例过渡帧](DeepSeekStandard/keyframes.png) 与 `DeepSeekStandard/keyframes.json`。
- [单枚 token](token-chip.png) 与 `token-chip.json`。

正常阶段直接复用原有 `Idle`；下落高低两帧只在该图上叠加同一枚 token，角色像素不重画或变形；最终阶段复用原有 `Tired`、`BegTokens`、`TokenDazed`。程序按关键帧时序播放一次后保持最终表情，预览与桌面一致。现在每款共 29 个缓存姿态（包含两帧 token 合成），全部旧 PNG 和走路图集保留。Python 仅分析透明轮廓以确定取图区域，没有修改 PNG 像素。

## 三个新表情（V0.8.9，原图保留）

三款角色各追加疲惫 `Tired`、求 token `BegTokens`、被 token 砸晕 `TokenDazed`：

- [原版新表情图](whale-emotions.png) 与 `whale-emotion-frames.json`。
- [DeepSeek Q 版新表情图](DeepSeekChibi/emotions.png) 与 `DeepSeekChibi/emotion-frames.json`。
- [DeepSeek 正常比例版新表情图](DeepSeekStandard/emotions.png) 与 `DeepSeekStandard/emotion-frames.json`。

使用内置 image_gen 工具，以原角色素材为身份参考生成，保留真正的透明通道。完整生成及排版修订提示词见 [generation-prompts-emotions.txt](generation-prompts-emotions.txt)。Python 仅分析透明轮廓和取图区域；图片像素未由脚本修改。来源和署名沿用下方记录。

V0.8.9 移除走路功能，全部旧 PNG、旧取图清单与提示词保留。运行时跳过 `WalkA` 至 `WalkD`，仅继续加载动作图集里的两帧打招呼。当时每款为原有 20 姿态 + 2 打招呼 + 3 新表情，共 25 姿态，三款共 75；完整列表见 [表情与动作](../../../../../docs/PET_EXPRESSIONS.md)。

## 历史素材记录（走路功能已移除）

V0.8.7 在三份 `motion-frames.json` 的四个走路条目追加 `anchor`（取图区域内的身体定位点）、`target`（512 画布中的目标点）和统一 `scale`。人物不再随尾巴、腿部轮廓变化而分别居中缩放，转身也绕同一身体定位点。图片像素、打招呼条目和原有姿态保持原样。

## 走路与打招呼（V0.8.6）

三款角色各追加独立的四帧走路与两帧打招呼：原版为 `whale-motion.png` / `whale-motion-frames.json`，DeepSeek 两款各为目录中的 `motion.png` / `motion-frames.json`。运行时将追加清单与各角色原有 20 姿态清单合并，原有图片与取图区域不变。当时每款共 26 个姿态；左行通过 WPF 镜像完整角色，透明命中坐标随变换转换。

新素材由内置 image_gen 参考原版 `whale-idle-v2.png` 和 `References/deepseek-reference.png` 生成。完整提示词见 [generation-prompts-motion.txt](generation-prompts-motion.txt)。透明轮廓仅用于分析取图区域，没有手工或 Python 改写图片像素；原有署名与使用条件继续适用。

## 新增 DeepSeek 两款角色（V0.8.4）

保留下面的原版素材与 `frames-v2.json`，新增 `DeepSeekChibi/`（Q 版）和 `DeepSeekStandard/`（正常成人动漫比例）。每套包含 `idle.png`、`social.png`、`typing-bowl.png` 和 `frames.json`，覆盖相同的 20 个姿态。图片由内置 image_gen 工具生成，未用 Python 或手工改写图像像素；Python 仅分析透明轮廓的取图区域，WPF 在运行时取图和缩放。

用户指定参考：[SelinLee / AI Moe Atlas 的 DeepSeek v5](https://selinlee.github.io/ai-moe-atlas/zh/references/#deepseek)。下载的完整参考位于 `References/deepseek-reference.png`，SHA-256 已与参考页校验一致，仅用于身份核对，不作为运行时资源打包。新增素材的完整提示词见 `generation-prompts-deepseek.txt`，来源、作者与素材使用条件见 [署名记录](../../../../../packaging/licenses/DeepSeek-Pets-NOTICE.md)。

## 原版（保留）

当前使用内置 image_gen 工具重新生成的三张透明图集，完整提示词见 `generation-prompts-v2.txt`：

- `whale-idle-v2.png`：4 × 2，1774 × 887，待机、视线、眨眼、睡觉与伸懒腰。
- `whale-social-v2.png`：4 × 2，1774 × 887，摸头、欢呼、挥手、吃饭、拎起、惊讶与傲娇。
- `whale-typing-bowl-v2.png`：2 × 2，1254 × 1254，左右手交替打字和头顶饭盆的两个平衡姿态。

三张图中的 20 个姿态均检查了格间透明空隙；`frames-v2.json` 明确指定每个姿态包含完整轮廓和道具的取图区域。程序呈现到 512 × 512 透明画布，统一脚底基线和留白，再进行呼吸、跳跃和摇摆。运行时不依赖生成目录或在线服务。

旧版 `whale-girl-atlas.png` 与 `generation-prompt.txt` 保留为角色参考和生成记录，不再打包使用。旧图按 4 × 4 等分造成的邻格残影、呆毛和尾巴裁切由新版独立取图区域解决。没有通过手工绘图或 Python 修改生成图的像素。

人物采用用户指定的 DeepSeek 社区「鲸鱼娘」同人方向：蓝色长发、鲸鳍、鲸尾、蓝白女仆服和白米饭。角色设定的社区创作链包括上善无形（溟月原型）、ZipZipPipe（DeepSeek 女仆风二创）和这个刀子真甜（Q 版）。本图由工具重新生成，没有下载、裁用网络原图。

参考与署名线索：[社区起源整理](https://fight.bigbluefish.net/)、[鲸鱼娘同人档案馆](https://fisharchive.cc/)。这些是非官方同人资料；素材沿用社区角色的署名、非商业及相同方式共享约定（[CC BY-NC-SA 4.0](https://creativecommons.org/licenses/by-nc-sa/4.0/)），不改变项目代码的许可。
