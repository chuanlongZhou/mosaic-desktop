# GPT、Gemini、Claude、Qwen 桌宠素材来源记录

角色社区设计：ZipZipPipe。作者：https://www.pixiv.net/users/18604994
作者作品与使用声明：https://www.pixiv.net/artworks/148186519
参考图整理：SelinLee / AI Moe Atlas，v5 固定形象参考。
参考页：https://selinlee.github.io/ai-moe-atlas/zh/references/

Mosaic 于 2026-10-25 使用内置 image_gen 参考右侧 Q 版身份，重新生成四套各三张透明动作图集，每套 27 幅绘制姿态。正常图用于两个 token 下落复用帧，因此每款有 29 个缓存姿态。原参考 PNG 保留在源码 References 目录中，不打包为运行时素材。没有裁用作者原立绘作为动作帧，没有用脚本修改生成 PNG 像素；取图区域仅由透明轮廓分析获得。

2026-10-26 追加四套正常比例版本，分别参考同一原参考图左侧成年动漫比例身份，使用内置 image_gen 重新生成每套三张透明动作图集。Q 版素材全部保留，正常比例各 27 幅绘制姿态及两幅 token 下落复用帧；提示词与排版修订见 generation-prompts-standard-companions.txt。原来源和使用条件保持，不新增或扩张授权声明。

参考文件及 SHA-256：
- openai-reference.png：37bf617c72092f73a8f56d3db6d95456679fea9c5ecf1a38fc1d64c2704e6d6a
- gemini-reference.png：f280188303731d17205de926795a8fd0463dc8995b7342cddf4b93a5dbf50e85
- claude-reference.png：b2d2c1a89f3cf059a8558b6fd505104f4309a0c18a96288be23a8ddd63509edb
- qwen-reference.png：818a393ee6767b65cae5a8d8d6a3dd34d8436e953760035d3dacc939ea9753fa

参考图地址前缀：https://selinlee.github.io/ai-moe-atlas/generated/ai-models-v5/

沿用参考资料保留的作者署名和非商业条件（LicenseRef-ZipZipPipe-NC）。不把四款素材标作 DeepSeek 的 CC BY-NC-SA 4.0；参考站保留的状态说明，不能等同于确认了所有后续改绘或再分发权利。代码的许可不覆盖第三方角色设计。素材是社区角色的生成派生图，不是作者原图或恢复细节，不代表 AI 品牌官方形象或背书。

完整性格扩写、动作分镜与中文台词为本项目新增，来自参考图的外观与简短气质提示，不冒充作者已发布的完整性格设定。
