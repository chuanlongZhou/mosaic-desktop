namespace Mosaic.Core;

public static class PetExpressionKeyframes
{
    public static bool Supports(PetPose pose) => pose is PetPose.Tired or PetPose.BegTokens or PetPose.TokenDazed;

    // Each action plays once from the existing neutral drawing, then holds its
    // final expression until the normal action timeout returns it to idle.
    public static PetPose At(PetPose expression, long elapsedMilliseconds)
    {
        if (!Supports(expression)) return expression;
        var elapsed = Math.Max(0, elapsedMilliseconds);
        if (elapsed < 450) return PetPose.Idle;
        return expression switch
        {
            PetPose.Tired => elapsed < 1050 ? PetPose.TiredYawn : PetPose.Tired,
            PetPose.BegTokens => elapsed < 1100 ? PetPose.BegReach : PetPose.BegTokens,
            _ => elapsed < 800 ? PetPose.TokenFallHigh : elapsed < 1150 ? PetPose.TokenFallLow : PetPose.TokenDazed
        };
    }

    public static string Caption(PetPose expression, long elapsedMilliseconds, string finalCaption)
        => At(expression, elapsedMilliseconds) switch
        {
            PetPose.Idle => expression switch { PetPose.Tired => "有一点困了…", PetPose.BegTokens => "鱼片…", _ => "嗯？" },
            PetPose.TiredYawn => "哈欠…算力有点跟不上了…",
            PetPose.BegReach => "可以给我一点 token 吗？",
            PetPose.TokenFallHigh or PetPose.TokenFallLow => "咦？有 token 掉下来了！",
            _ => finalCaption
        };
}
