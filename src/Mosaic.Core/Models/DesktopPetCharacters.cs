namespace Mosaic.Core.Models;

public enum PetCharacter { WhaleGirl, DeepSeekChibi, DeepSeekStandard, Gpt, Gemini, Claude, Qwen, GptStandard, GeminiStandard, ClaudeStandard, QwenStandard,
    DeepSeekChibiMk2, GrokCommunity, KimiCommunity, ZhipuCommunity, CrossPetGpt, CrossPetClaude, CrossPetDeepSeek, CrossPetGemini, CrossPetQwen, CrossPetGlm }

public sealed record DesktopPetCharacter(PetCharacter Id, string Name, string Description, double SizeMultiplier = 1,
    string FeedLabel = "喂白米饭", string SignatureLabel = "头顶饭盆", string TeaseLabel = "叫她大肥鱼", string Tagline = "今天也要一起摸鱼呀～");

public static class DesktopPetCharacters
{
    public static IReadOnlyList<DesktopPetCharacter> All { get; } = Array.AsReadOnly<DesktopPetCharacter>([
        new(PetCharacter.WhaleGirl, "鲸鱼娘 · 原版 Q 版", "熟悉的蓝发、鲸鳍和大尾巴。"),
        new(PetCharacter.DeepSeekChibi, "DeepSeek · Q 版", "参考图里的圆脸小鲸娘，鲸鱼围裙与金纹裙摆。"),
        new(PetCharacter.DeepSeekStandard, "DeepSeek · 正常比例", "参考图里的长发女仆，保留正常身材比例与长裙，桌面显示高度加倍。", SizeMultiplier: 2),
        new(PetCharacter.Gpt, "GPT · 白龙娘（Q 版）", "沉稳聪慧的白龙伙伴。银白长发、龙角、龙翼与龙尾，喜欢把思路一步步理清。", FeedLabel: "投喂茶点", SignatureLabel: "舒展龙翼", TeaseLabel: "逗逗龙娘", Tagline: "别着急，我们一步一步来。"),
        new(PetCharacter.Gemini, "Gemini · 星猫娘（Q 版）", "俏皮灵动的星空猫娘。蓝紫渐变发、异色瞳和猫尾，对新点子充满好奇。", FeedLabel: "投喂小鱼饼", SignatureLabel: "猫爪比心", TeaseLabel: "逗逗猫娘", Tagline: "喵～今天会发现什么新点子？"),
        new(PetCharacter.Claude, "Claude · 书卷娘（Q 版）", "温柔安静的读书伙伴。橙色长发、日芒花饰和棕色书本，习惯认真读完每一页。", FeedLabel: "递一杯热茶", SignatureLabel: "翻一页书", TeaseLabel: "打断阅读", Tagline: "这一页，让我慢慢陪你读。"),
        new(PetCharacter.Qwen, "Qwen · 折扇娘（Q 版）", "明朗自信的国风伙伴。蓝紫长发与侧辫、中式头饰和折扇，言语轻快又有条理。", FeedLabel: "投喂桂花糕", SignatureLabel: "开合折扇", TeaseLabel: "逗逗扇娘", Tagline: "有问便来，一起把思路展开。"),
        new(PetCharacter.GptStandard, "GPT · 白龙娘（正常比例）", "正常比例的白龙伙伴。银白长发、龙角、龙翼与龙尾，沉稳体贴；桌面显示高度加倍。", SizeMultiplier: 2, FeedLabel: "投喂茶点", SignatureLabel: "舒展龙翼", TeaseLabel: "逗逗龙娘", Tagline: "别着急，我们一步一步来。"),
        new(PetCharacter.GeminiStandard, "Gemini · 星猫娘（正常比例）", "正常比例的星空猫娘。蓝紫渐变发、异色瞳、猫耳与猫尾，俏皮好奇；桌面显示高度加倍。", SizeMultiplier: 2, FeedLabel: "投喂小鱼饼", SignatureLabel: "猫爪比心", TeaseLabel: "逗逗猫娘", Tagline: "喵～今天会发现什么新点子？"),
        new(PetCharacter.ClaudeStandard, "Claude · 书卷娘（正常比例）", "正常比例的学者伙伴。橙色长发、日芒花饰、象牙白长裙与书本，温柔安静；桌面显示高度加倍。", SizeMultiplier: 2, FeedLabel: "递一杯热茶", SignatureLabel: "翻一页书", TeaseLabel: "打断阅读", Tagline: "这一页，让我慢慢陪你读。"),
        new(PetCharacter.QwenStandard, "Qwen · 折扇娘（正常比例）", "正常比例的国风伙伴。蓝紫长发与侧辫、中式头饰、长披袖与折扇，明朗自信；桌面显示高度加倍。", SizeMultiplier: 2, FeedLabel: "投喂桂花糕", SignatureLabel: "开合折扇", TeaseLabel: "逗逗扇娘", Tagline: "有问便来，一起把思路展开。"),
        new(PetCharacter.DeepSeekChibiMk2, "Deepseek Q版 mk2", "来自 pi-deepseek-pet 的 106 段原版透明动画，共享 DeepSeek Q 版的肥鱼配音与整点报时。素材仅限非商业使用。", SignatureLabel: "今天吃什么"),
        new(PetCharacter.GrokCommunity, "Grok · 暗金哥特娘（社区图集）", "来自 chatgpt-chibi-pets 的非官方同人图集，金发双马尾与暗金哥特服饰。", FeedLabel: "投喂点心", SignatureLabel: "检查任务", TeaseLabel: "逗逗她", Tagline: "直说吧，今天要挑战什么？"),
        new(PetCharacter.KimiCommunity, "Kimi · 月影娘（社区图集）", "来自 chatgpt-chibi-pets 的非官方同人图集，月影主题的温柔伙伴。", FeedLabel: "投喂月饼", SignatureLabel: "检查任务", TeaseLabel: "逗逗她", Tagline: "慢慢说，我会陪你看完。"),
        new(PetCharacter.ZhipuCommunity, "智谱 · 珊瑚技术娘（社区图集）", "来自 chatgpt-chibi-pets 的非官方同人图集，珊瑚色科技主题。", FeedLabel: "投喂点心", SignatureLabel: "检查任务", TeaseLabel: "逗逗她", Tagline: "交给我，先理清线索。"),
        new(PetCharacter.CrossPetGpt, "GPT · 白龙娘（CrossPet）", "CrossPet 社区画风，包含思考、阅读、搜索、绘画等动作。", FeedLabel: "投喂茶点", SignatureLabel: "舒展龙翼", TeaseLabel: "逗逗龙娘", Tagline: "别着急，我们一步一步来。"),
        new(PetCharacter.CrossPetClaude, "Claude · 书卷娘（CrossPet）", "CrossPet 社区画风，包含思考、书写、搜索等动作。", FeedLabel: "递一杯热茶", SignatureLabel: "翻一页书", TeaseLabel: "打断阅读", Tagline: "这一页，让我慢慢陪你读。"),
        new(PetCharacter.CrossPetDeepSeek, "DeepSeek · 鲸鱼娘（CrossPet）", "CrossPet 社区画风，包含思考、阅读、搜索、绘画等动作。", Tagline: "今天也要一起摸鱼呀～"),
        new(PetCharacter.CrossPetGemini, "Gemini · 星猫娘（CrossPet）", "CrossPet 社区画风，包含思考、阅读、搜索、绘画等动作。", FeedLabel: "投喂小鱼饼", SignatureLabel: "猫爪比心", TeaseLabel: "逗逗猫娘", Tagline: "喵～今天会发现什么新点子？"),
        new(PetCharacter.CrossPetQwen, "Qwen · 国风千金（CrossPet）", "CrossPet 社区画风，包含思考、阅读、搜索、绘画等动作。", FeedLabel: "投喂桂花糕", SignatureLabel: "开合折扇", TeaseLabel: "逗逗扇娘", Tagline: "有问便来，一起把思路展开。"),
        new(PetCharacter.CrossPetGlm, "GLM · 狐狸娘（CrossPet）", "CrossPet 社区画风的黑狐狸女仆，包含思考、阅读、搜索、绘画等动作。", FeedLabel: "投喂点心", SignatureLabel: "打开小账本", TeaseLabel: "逗逗狐狸", Tagline: "今天的任务，交给我吧。")
    ]);

    public static bool IsAnimated(PetCharacter character) => character == PetCharacter.DeepSeekChibiMk2;
    public static double CanvasAspectRatio(PetCharacter character) => IsAnimated(character) ? 16d / 9 : 1;

    public static DesktopPetCharacter Get(PetCharacter character) => All.Single(item => item.Id == character);

    public static PetCharacter Personality(PetCharacter character) => character switch
    {
        PetCharacter.DeepSeekChibiMk2 => PetCharacter.DeepSeekChibi,
        PetCharacter.GptStandard => PetCharacter.Gpt,
        PetCharacter.GeminiStandard => PetCharacter.Gemini,
        PetCharacter.ClaudeStandard => PetCharacter.Claude,
        PetCharacter.QwenStandard => PetCharacter.Qwen,
        PetCharacter.CrossPetGpt => PetCharacter.Gpt,
        PetCharacter.CrossPetClaude => PetCharacter.Claude,
        PetCharacter.CrossPetGemini => PetCharacter.Gemini,
        PetCharacter.CrossPetQwen => PetCharacter.Qwen,
        PetCharacter.CrossPetDeepSeek => PetCharacter.DeepSeekChibi,
        PetCharacter.CrossPetGlm => PetCharacter.ZhipuCommunity,
        _ => character
    };
}
