namespace Mosaic.PetWorldDemo;

public static class WorldStory
{
    public static IReadOnlyList<StoryBeat> Create(WorldActor[] actors)
    {
        var beats = new List<StoryBeat>();
        var chapter = 0;
        Direction D(int who, SceneAction action, string activity, FloorPoint? destination = null) => new(who, action, activity, destination);
        Direction[] All(SceneAction action, string activity) => actors.Select(a => D(a.Id, action, activity)).ToArray();
        void B(double seconds, string summary, int? speaker = null, string? line = null, params Direction[] changes)
        {
            var directions = actors.Select(a => D(a.Id, SceneAction.Idle, a.Id == speaker ? "和同伴聊天" : "听同伴说话")).ToArray();
            foreach (var d in changes) directions[d.Actor] = d;
            beats.Add(new(chapter, seconds, summary, directions, speaker, line));
        }
        // A loose semicircle keeps all five faces legible in the isometric camera.
        var social = new[] { new FloorPoint(3.5, 7), new(7, 2), new(10, 4.5), new(3.5, 4), new(7.5, 7.5) };
        var rest = new[] { new FloorPoint(4.5, 8), new(7, 8), new(9.5, 7), new(2, 7.5), new(9.5, 4.5) };

        B(4, "午后的图书馆开门了，五位老朋友各自归位。", 3, "今天还是老规矩：书可以慢慢读，茶可以慢慢喝。",
            actors.Select(a => D(a.Id, SceneAction.Greet, "向大家打招呼", a.Home)).ToArray());
        B(7, "GPT 准备主持读书会，Gemini 已经盯上她的提纲。", 1, "读书会开始。首先，我准备了一个非常简短的提纲。");
        B(6, "Gemini 探头看了看那份并不短的提纲。", 2, "GPT 的提纲好长，能借我当猫猫滑梯吗？", D(2, SceneAction.Signature, "猫爪比划提纲"));
        B(6, "GPT 被逗得收了收龙翼。", 1, "那我把第五版先收起来。今天真的只写一页。", D(1, SceneAction.Pout, "被戳穿了第五版提纲"));
        B(6, "Claude 接过了那一页纸。", 3, "我替你省掉目录。剩下的刚好是一页。", D(3, SceneAction.Signature, "翻开共同笔记"));
        B(6, "大家同意先做一件小事。", 4, "有条理很好，有结尾更好。开始吧。", All(SceneAction.Wave, "举手同意"));
        B(15, "窗外很安静，她们各自观察房间，准备自己的那一句。", null, null, All(SceneAction.Idle, "安静整理思路"));

        chapter = 1;
        B(7, "每人用随身小键盘给共同笔记写一句话。", 1, "每个人写一句，最后拼成一段。不要抢着替别人补完。", All(SceneAction.Work, "给共同笔记写一句"));
        B(6, "DeepSeek 已经写完，并悄悄加了一条。", 0, "我写的是：认真读书。括号，读完开饭。", D(0, SceneAction.Work, "补充开饭时间"), D(3, SceneAction.Signature, "校对大家的句子"));
        B(6, "Qwen 检查了那条括号里的内容。", 4, "你这叫备注？你把重点全塞进括号里了。", D(4, SceneAction.Signature, "合扇点出重点"));
        B(6, "DeepSeek 把饭碗抱得更紧了一点。", 0, "这叫需求明确，不许删！", D(0, SceneAction.Pout, "维护她的开饭备注"));
        B(7, "Gemini 又加了一个会发光的点子。", 2, "我只补充一点点：星星、窗台、会飞的书签……", D(2, SceneAction.Work, "加了一串星星"));
        B(6, "GPT 给星星留了一个页边的位置。", 1, "Gemini 说只补充一点，结果补充了一整片星空。", D(1, SceneAction.Work, "整理页边的小星星"));
        B(5, "Gemini 假装生气，尾巴却很诚实。", 2, "星空又不占正文！我的猫爪已经很克制了。", D(2, SceneAction.Pout, "保护页边的星空"));
        B(7, "五句话终于拼成了一页，大家都很满意。", 3, "这一页很好。每个人都在里面，连饭也在。", All(SceneAction.Cheer, "共同完成了这一页"));

        chapter = 2;
        B(4, "笔记暂时合上，大家绕过书桌，来到茶点区。", null, null,
            actors.Select(a => D(a.Id, SceneAction.Idle, "茶点区", social[a.Id])).ToArray());
        B(6, "DeepSeek 端起自己的饭碗，认真做了一次平衡测试。", 0, "我宣布：这一碗通过了稳定性验收。", D(0, SceneAction.Signature, "顶饭碗验收"));
        B(6, "GPT 看了看那条开心的鲸尾。", 1, "DeepSeek 的尾巴在摇，看来这份米饭通过验收了。");
        B(6, "Qwen 替热茶扇了两下风。", 4, "饭也不用放头上验收呀。来，我给茶扇点风。", D(4, SceneAction.Signature, "替热茶扇风"));
        B(6, "Gemini 盯着折扇的流苏，努力管住猫爪。", 2, "Qwen 的流苏一直晃，我的猫爪快要不听话啦！", D(2, SceneAction.Signature, "收住想追流苏的猫爪"));
        B(6, "Qwen 假装严肃地合起扇子。", 4, "流苏是装饰，不是猫猫逗号。看我手里的桂花糕。", D(4, SceneAction.Pout, "护住折扇流苏"));
        B(12, "每个人都拿到了自己的茶点，聊天也慢了下来。", null, null, All(SceneAction.Eat, "享用各自的茶点"));
        B(6, "Claude 从茶杯后抬起头。", 3, "GPT 连喝茶都想列三个步骤，我替她省掉了目录。", D(3, SceneAction.Eat, "喝一口热茶"));
        B(5, "GPT 这次决定听劝。", 1, "第一步，喝茶。好了，这一版只有一步。", D(1, SceneAction.Eat, "实践只有一步的计划"));

        chapter = 3;
        B(4, "茶点吃完，大家回到各自喜欢的角落。", null, null,
            actors.Select(a => D(a.Id, SceneAction.Idle, "自己的座位", a.Home)).ToArray());
        B(7, "Claude 翻到刚才夹着书签的那一页。", 3, "这一句很有意思，要一起读吗？", D(3, SceneAction.Signature, "翻到留着书签的那页"));
        B(6, "Qwen 一开扇，桌边的书页轻轻翻动了。", 4, "我只扇一点风，保证不翻到结局。", D(4, SceneAction.Signature, "开扇送来一阵风"));
        B(5, "书页真的翻过去了，Claude 按住了书签。", 3, "等我读完这一页，再捣乱，好吗？", D(3, SceneAction.Pout, "按住被吹动的书页"));
        B(6, "GPT 试着用龙翼挡住书桌旁的风。", 1, "那我挡一下。龙翼借来当临时屏风。", D(1, SceneAction.Signature, "舒展龙翼挡风"));
        B(6, "Gemini 看见龙翼展开，立刻来了精神。", 2, "屏风好大！不过 DeepSeek 说：别挡住她看饭。", D(2, SceneAction.Surprise, "看见展开的龙翼"));
        B(6, "DeepSeek 用饭碗表示赞同。", 0, "我没有说不让挡风，我说要留一个看饭的窗口。", D(0, SceneAction.Signature, "举起饭碗示意位置"));
        B(6, "Gemini 用猫爪比了一个小小的窗口。", 2, "这个大小可以吗？顺便送你一个猫爪爱心。", D(2, SceneAction.Signature, "猫爪比心化解小争论"));
        B(9, "Qwen 收小了扇子，Claude 终于读完这一页。", 4, "好，风量调小，书签归位，饭碗视野保留。", D(4, SceneAction.Signature, "把风量调小"), D(3, SceneAction.Signature, "安心读完这一页"));

        chapter = 4;
        B(4, "读书会要用纸制 token 选下一本书。", null, null,
            actors.Select(a => D(a.Id, SceneAction.Idle, "投票区", social[a.Id])).ToArray());
        B(6, "大家翻开代币袋，才发现还没有发投票用的 token。", 4, "每人一枚纸 token，投给你想读的下一本书。", All(SceneAction.AskToken, "伸手领取纸制投票 token"));
        B(6, "Gemini 想让代币的出场更有仪式感。", 2, "如果把纸 token 轻轻抛起来，会像五颗小星星！", D(2, SceneAction.Cheer, "提出一个过于活泼的发放办法"));
        B(5, "五枚轻飘飘的纸 token 落了下来，大家被这个开场吓了一跳。", null, null, All(SceneAction.TokenOops, "被纸 token 的出场晃了眼"));
        B(5, "大家看着落在面前的纸 token，先安静了一秒。", 0, "鱼片……这算天降预算，还是天降小纸片？", All(SceneAction.Surprise, "确认只是轻轻的纸片"));
        B(6, "GPT 决定取消投票环节里的抛物线。", 1, "预算已经到账。下次改成手递，流程可以少一步。");
        B(6, "Gemini 收好剩下的纸片。", 2, "收到。星星可以在页边发光，不一定要从头顶路过。", D(2, SceneAction.Pout, "承认发放方式需要改进"));
        B(7, "Claude 接过代币，替这次小事故留了一句批注。", 3, "今日批注：有创意的发放方式，不一定适合图书馆。", D(3, SceneAction.Work, "记下今天的小插曲"));
        B(6, "投票完成，大家选好了下次读的书。", 4, "下一本定好了。现在谁也不用补充第五点了。", All(SceneAction.Cheer, "选好了下一本书"));

        chapter = 5;
        B(5, "茶室安静下来，她们不约而同打了个哈欠。", 3, "读到同一行第三遍了……休息片刻吧。", All(SceneAction.Tired, "有点困了，准备休息"));
        B(4, "大家绕开家具，各自找了一块柔软的坐垫。", null, null,
            actors.Select(a => D(a.Id, SceneAction.Idle, "休息坐垫", rest[a.Id])).ToArray());
        B(25, "书签夹好了，茶杯放稳了。房间只剩下安静的呼吸。", null, null, All(SceneAction.Sleep, "在自己的坐垫上休息"));
        B(5, "午后的短觉结束了，大家慢慢伸了个懒腰。", 1, "休息也算计划的一部分。现在思路又有地方落脚了。", All(SceneAction.Wake, "慢慢醒来，伸个懒腰"));
        B(7, "今天这一页已经写完，五位朋友约好下次继续。", 4, "下次还是这里。书签不动，桂花糕可以补货。", All(SceneAction.Goodbye, "和你挥手，下次继续"));
        B(12, "这一页告一段落。她们会继续过自己的小日子。", null, null, All(SceneAction.Idle, "享受一会儿安静"));
        return beats;
    }
}
