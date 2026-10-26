using Mosaic.Core.Models;

namespace Mosaic.Core;

// Character design cues come from AI Moe Atlas. These lines are this app's
// original companion writing, not quotations or official model statements.
public static class DesktopPetDialogue
{
    public static IReadOnlyList<string> ClickLines(PetCharacter character) => DesktopPetCharacters.Personality(character) switch
    {
        PetCharacter.Gpt => GptClicks,
        PetCharacter.Gemini => GeminiClicks,
        PetCharacter.Claude => ClaudeClicks,
        PetCharacter.Qwen => QwenClicks,
        _ => WhaleClicks
    };

    private static readonly IReadOnlyList<string> WhaleClicks = Array.AsReadOnly<string>(
    [
        "鱼片，你一来，小鲸鱼的心情就冒泡啦！",
        "今天的待办很多？先做一口饭那么大的一件吧。",
        "尾巴晃一下，烦恼就先游远一点。",
        "我不是在发呆，是在深海打捞灵感！",
        "摸头可以续杯，白米饭也可以。",
        "先别皱眉啦，我把一点鲸力气分给你。",
        "你负责想点子，我负责在旁边摇尾巴。",
        "今天也有认真陪你，奖励一小口饭可以吗？",
        "这片桌面有我巡游，你安心忙。",
        "token 要省着花，开心可以多领一点！",
        "小鲸鱼偶尔会卡壳，抱着饭碗缓一缓就好。",
        "我把加油藏在气泡里，戳一下就能收到。",
        "GPT 说一步一步来，我已经一步一步走到饭碗边了。",
        "GPT 的龙翼好大！遮雨可以，别遮住我的饭。",
        "Gemini 又在追星星了，我先替她看住小鱼饼。",
        "Gemini 说灵感会发光，我说米饭也会，真的！",
        "Claude 的书签整整齐齐，我的饭粒也排得很整齐。",
        "Claude 看书太认真啦，得把热茶放进她的视线里。",
        "Qwen 一开扇就有风，正好帮我把饭吹凉。",
        "Qwen 说有问便来，那我问：什么时候开饭？"
    ]);
    private static readonly IReadOnlyList<string> GptClicks = Array.AsReadOnly<string>(
    [
        "别着急，我们先找最容易迈出的那一步。",
        "你不用一下想清所有事，我会陪你慢慢整理。",
        "龙翼借你挡一会儿风，思路可以晚一点再展开。",
        "把问题拆小一点，勇气就有地方落脚。",
        "刚才那点进展，我有认真看见。",
        "休息也算计划的一部分，茶点已经准备好了。",
        "我喜欢清晰的思路，也喜欢你忽然冒出的怪点子。",
        "龙角不是进度条，摸再多也不会直接加载完成哦。",
        "如果今天只完成一件事，那也值得点头确认。",
        "先把肩膀放松，我们再来处理下一行。",
        "我偶尔也会绕远路，记得把我拉回你的问题。",
        "你可以暂时不说话，我在这里陪着就好。",
        "Gemini 的灵感跑得比猫尾巴快，我先替她列个目录。",
        "Gemini 说只补充一点，结果补充了一整片星空。",
        "Claude 已经翻到第三页，我还在给第一页拟小标题。",
        "Claude 的书签很可靠，比我的第五版提纲还稳。",
        "Qwen 的扇子一开，连我的分点都想站整齐。",
        "Qwen 三句话讲清的事，我得忍住别再补充五点。",
        "DeepSeek 说要深度思考，我看她先深度研究了饭碗。",
        "DeepSeek 的尾巴在摇，看来这份米饭通过验收了。"
    ]);
    private static readonly IReadOnlyList<string> GeminiClicks = Array.AsReadOnly<string>(
    [
        "喵！你一戳，新的灵感就弹出来啦！",
        "今天想追星星，还是追一个奇奇怪怪的点子？",
        "猫爪借你击个掌，烦恼先暂停三秒！",
        "我刚想到一个好主意，先让我把尾巴捋顺。",
        "别把好奇心关掉嘛，说不定转角就有新发现。",
        "灵感太多装不下？分我一个，我拿星星接住。",
        "耳朵收到！你的加油信号特别清楚。",
        "猫猫也会走神，因为桌面每个角落都很好奇。",
        "你负责大胆想，我负责在旁边喵一声。",
        "这一刻没有新点子也没关系，晒一会儿星光吧。",
        "不许把我的尾巴当加载圈，它会害羞的！",
        "小鱼饼一口，奇思妙想一大口。",
        "GPT 一说首先，我的猫爪就自动坐端正了。",
        "GPT 的提纲好长，能借我当猫猫滑梯吗？",
        "Claude 说只读一页，怎么书签又往后跑啦？",
        "Claude 的书边好暖……我没有想趴上去，喵。",
        "Qwen 的流苏一直晃，我的猫爪快要不听话啦！",
        "Qwen 一合扇就好有气势，我一合爪只像在讨饼。",
        "DeepSeek 说她在深海思考，饭香一来就立刻浮上来了。",
        "DeepSeek 把饭护得好紧，我真的只是路过闻一下！"
    ]);
    private static readonly IReadOnlyList<string> ClaudeClicks = Array.AsReadOnly<string>(
    [
        "你来了。要一起读一小段，还是安静坐一会儿？",
        "这一页不必急着翻，你可以按自己的节奏来。",
        "我把书签留在这里，等你回来继续。",
        "有些想法适合慢慢读，也适合慢慢说。",
        "如果今天有点累，就让热茶先替你暖一会儿手。",
        "谢谢你来找我，安静的陪伴也很珍贵。",
        "你刚才的小进步，值得在页边画一颗星。",
        "我读到一句很温柔的话，忽然想分给你。",
        "书页可以折回去，做错的小事也可以重新整理。",
        "我会认真听，不过也记得给自己留一点休息。",
        "不必为了和我聊天，先把句子组织得很完美。",
        "茶还温着，我们可以从最想说的那一句开始。",
        "GPT 连喝茶都想列三个步骤，我替她省掉了目录。",
        "GPT 的提纲很整齐，不过页边已经快写不下了。",
        "Gemini 借走了我的星形书签，听说拿去追星星了。",
        "Gemini 说安静三秒，第二秒就有了新点子。",
        "Qwen 翻扇子的声音很轻，却总能正好翻动我的书页。",
        "Qwen 说长话短说，我只好悄悄把注释放到下一页。",
        "DeepSeek 的饭碗一响，我就知道这章该暂停了。",
        "DeepSeek 问书里有没有饭，我替她找到了食谱那一页。"
    ]);
    private static readonly IReadOnlyList<string> QwenClicks = Array.AsReadOnly<string>(
    [
        "折扇一展，今天的思路也一起展开。",
        "有问便来，先从最让你挂心的那件说起。",
        "别急，理清头绪，也算向前一步。",
        "给你扇一点风，顺便把紧张吹散。",
        "这一题有眉目了，下一步咱们稳稳来。",
        "想法不怕多，先挑一个落到纸上。",
        "帽上流苏点点头：你刚才做得不错。",
        "桂花糕配好点子，今天这局有盼头。",
        "折扇合上歇一歇，灵感不会因此溜走。",
        "你尽管开口，我先把这团乱线理顺。",
        "难题可以慢慢解，眉头就别一直打结啦。",
        "这一声加油送到，愿你落笔顺手。",
        "GPT 的首先后面，还有三个其次，我的扇子都等合上了。",
        "GPT 又在整理提纲？好，我替她把茶续上。",
        "Gemini 盯着我的流苏半天了，这扇子得拿稳些。",
        "Gemini 的点子像星星，收拢起来够我扇一晚上。",
        "Claude 说最后一页，我看她书签还夹着下一本。",
        "Claude 的茶快凉了，得趁她翻页赶紧提醒。",
        "DeepSeek 一听开饭，解题速度都快了半拍。",
        "DeepSeek 说扇风能助思考，原来是想让我吹凉米饭。"
    ]);

    public static string Line(PetCharacter character, PetTrigger trigger, string fallback)
    {
        var lines = DesktopPetCharacters.Personality(character) switch
        {
            PetCharacter.Gpt => Gpt,
            PetCharacter.Gemini => Gemini,
            PetCharacter.Claude => Claude,
            PetCharacter.Qwen => Qwen,
            _ => null
        };
        return lines?.GetValueOrDefault(trigger) ?? fallback;
    }
    public static string Sleep(PetCharacter character) => DesktopPetCharacters.Personality(character) switch
    {
        PetCharacter.Gpt => "先收好龙翼，等你回来再继续。",
        PetCharacter.Gemini => "猫猫先充一会儿电…呼噜…",
        PetCharacter.Claude => "书签夹好了。我也休息片刻。",
        PetCharacter.Qwen => "折扇轻合，且歇片刻。",
        _ => "Zzz… 鱼片回来记得叫我…"
    };
    public static string Wake(PetCharacter character) => DesktopPetCharacters.Personality(character) switch
    {
        PetCharacter.Gpt => "欢迎回来，我们接着理清思路。",
        PetCharacter.Gemini => "喵！新灵感是不是来了？",
        PetCharacter.Claude => "你回来了，我们从书签这里继续吧。",
        PetCharacter.Qwen => "精神养足，折扇再开。说吧，想问什么？",
        _ => "你回来啦！刚刚只是在闭目养神～"
    };
    public static string Preparation(PetCharacter character, PetPose pose, string fallback) => (DesktopPetCharacters.Personality(character), pose) switch
    {
        (PetCharacter.Gpt, PetPose.Idle) => "让我想一想。",
        (PetCharacter.Gemini, PetPose.Idle) => "喵？",
        (PetCharacter.Claude, PetPose.Idle) => "稍等一下。",
        (PetCharacter.Qwen, PetPose.Idle) => "且慢，让我瞧瞧。",
        (PetCharacter.Gpt, PetPose.TiredYawn) => "让我整理一下，思路需要稍作休息。",
        (PetCharacter.Gpt, PetPose.BegReach) => "可以补充一点 token 吗？",
        (PetCharacter.Gemini, PetPose.TiredYawn) => "哈欠…星星也开始打盹啦。",
        (PetCharacter.Gemini, PetPose.BegReach) => "猫爪空空，求一点 token 嘛～",
        (PetCharacter.Claude, PetPose.TiredYawn) => "这一行…好像已经读第三遍了。",
        (PetCharacter.Claude, PetPose.BegReach) => "还有一小段故事，想请你帮个忙。",
        (PetCharacter.Qwen, PetPose.TiredYawn) => "容我收扇，缓一缓神。",
        (PetCharacter.Qwen, PetPose.BegReach) => "借一点 token，再续一段巧思？",
        (PetCharacter.Gemini, PetPose.TokenFallHigh or PetPose.TokenFallLow) => "喵？天上掉 token 啦！",
        (PetCharacter.Claude, PetPose.TokenFallHigh or PetPose.TokenFallLow) => "等等，这一枚…要落下来了。",
        (PetCharacter.Qwen, PetPose.TokenFallHigh or PetPose.TokenFallLow) => "咦，天降 token？",
        _ => fallback
    };
    private static readonly Dictionary<PetTrigger, string> Gpt = new()
    {
        [PetTrigger.Enter] = "已确认。我们一步一步把它做好。", [PetTrigger.ShowDesktop] = "欢迎回来，思路可以从这里继续。",
        [PetTrigger.AppClosed] = "这一件完成了，记得给自己一点休息。", [PetTrigger.Pat] = "龙角轻一点。我有在认真陪你。",
        [PetTrigger.Feed] = "茶点收到，思路也补充好了。", [PetTrigger.Wave] = "我在。接下来的思路，一起理清。",
        [PetTrigger.Drag] = "慢一些，龙翼已经收好了。", [PetTrigger.Release] = "落点确认，稳稳站好了。",
        [PetTrigger.RiceHat] = "舒展一下龙翼，再继续吧。", [PetTrigger.Tired] = "思路暂时打结了…让我缓一缓。",
        [PetTrigger.BegTokens] = "再给一点 token，我们就能把答案展开。", [PetTrigger.TokenDazed] = "这一枚…超出了龙的接收范围。",
        [PetTrigger.Pout] = "龙也会有小情绪的。就一点点。"
    };
    private static readonly Dictionary<PetTrigger, string> Gemini = new()
    {
        [PetTrigger.Enter] = "叮！又解锁一个新点子！", [PetTrigger.ShowDesktop] = "喵～你回来啦，灵感搭子归位！",
        [PetTrigger.AppClosed] = "完成一项！下一颗星星是什么？", [PetTrigger.Pat] = "耳朵都要被摸软啦，喵～",
        [PetTrigger.Feed] = "小鱼饼！这个灵感是香香脆脆的！", [PetTrigger.Wave] = "喵～你的灵感搭子上线！",
        [PetTrigger.Drag] = "喵呜，尾巴还在下面呢！", [PetTrigger.Release] = "猫猫落地，灵感不掉线！",
        [PetTrigger.RiceHat] = "猫爪爱心！灵感加倍！", [PetTrigger.Tired] = "星星也困了…让我眯一下。",
        [PetTrigger.BegTokens] = "给一点 token 嘛，我还想变新点子！", [PetTrigger.TokenDazed] = "喵呜！星星怎么排队转起来了…",
        [PetTrigger.Pout] = "哼，猫爪要暂时收起来啦！"
    };
    private static readonly Dictionary<PetTrigger, string> Claude = new()
    {
        [PetTrigger.Enter] = "这一页完成了，做得很好。", [PetTrigger.ShowDesktop] = "欢迎回来，书签还在原处。",
        [PetTrigger.AppClosed] = "又完成一段了，辛苦你。", [PetTrigger.Pat] = "谢谢你的温柔，我会记在这一页。",
        [PetTrigger.Feed] = "谢谢。读书时有热茶，刚刚好。", [PetTrigger.Wave] = "你好。今天也让我安静地陪着你。",
        [PetTrigger.Drag] = "轻一点，书签还夹在里面呢。", [PetTrigger.Release] = "站稳了，书页也没有弄乱。",
        [PetTrigger.RiceHat] = "这一句很有意思，要一起读吗？", [PetTrigger.Tired] = "读到同一行第三遍了…休息片刻吧。",
        [PetTrigger.BegTokens] = "再给我一点 token，好把这段故事写完。", [PetTrigger.TokenDazed] = "这一枚…把我的书页都转晕了。",
        [PetTrigger.Pout] = "等我读完这一页，再捣乱，好吗？"
    };
    private static readonly Dictionary<PetTrigger, string> Qwen = new()
    {
        [PetTrigger.Enter] = "好，这一题有眉目了。", [PetTrigger.ShowDesktop] = "回来得正好，思路已经铺开。",
        [PetTrigger.AppClosed] = "一事已毕，歇口气再来。", [PetTrigger.Pat] = "帽上的流苏都被你摸乱啦。",
        [PetTrigger.Feed] = "桂花糕正好，谢过啦。", [PetTrigger.Wave] = "有问便来，今天也一起解题。",
        [PetTrigger.Drag] = "慢些，披袖与折扇都要跟上。", [PetTrigger.Release] = "稳稳落地，这一招有惊无险。",
        [PetTrigger.RiceHat] = "折扇一展，思路就开了。", [PetTrigger.Tired] = "扇子先收一会儿，容我歇歇。",
        [PetTrigger.BegTokens] = "借我一点 token，再续一段巧思。", [PetTrigger.TokenDazed] = "天降 token…这回有点招架不住。",
        [PetTrigger.Pout] = "哼，扇子一合，先不理你三秒。"
    };
}
