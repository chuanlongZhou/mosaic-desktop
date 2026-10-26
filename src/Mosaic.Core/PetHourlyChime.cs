using Mosaic.Core.Models;

namespace Mosaic.Core;

/// <summary>Observes local hour boundaries without replaying missed or repeated hours.</summary>
public sealed class PetHourlyChimeSchedule
{
    private DateTimeOffset? _previous;
    private DateTime _latestHour;

    public void ResetObservation() => _previous = null;

    public bool TryTake(DateTimeOffset now, bool enabled)
    {
        var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
        var previous = _previous;
        _previous = now;
        if (previous is null) { if (hour > _latestHour) _latestHour = hour; return false; }
        if (hour <= _latestHour) return false;
        _latestHour = hour; // Disabled, hidden and missed boundaries are consumed as well.
        var elapsed = now - previous.Value;
        return enabled && now.Offset == previous.Value.Offset && elapsed > TimeSpan.Zero &&
            elapsed <= TimeSpan.FromSeconds(90) && now.Minute == 0 && now.Second < 30;
    }
}

public static class PetHourlyChime
{
    public static bool Supports(PetCharacter character) => character is PetCharacter.DeepSeekChibi or PetCharacter.DeepSeekChibiMk2;
    public static bool CanAnnounce(DesktopPetOptions options, bool visible, bool fullscreen) =>
        visible && Supports(options.Character) && options.HourlyChimeEnabled && options.HourlyChimeVolume > 0 &&
        !(fullscreen && options.HideInFullscreen);

    private static readonly string[] Hours = ["零", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十", "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十", "二十一", "二十二", "二十三"];
    public static string GetLine(int hour)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hour);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hour, 23);
        var reminder = hour switch
        {
            < 6 => "夜深啦，小鲸鱼陪着你，也要记得早点休息哦。",
            < 9 => "早上好呀，吃好早饭，今天也一起加油吧。",
            < 12 => "喝口水，伸个懒腰吧，小鲸鱼给你加油。",
            12 => "午饭时间到啦，认真吃饭，也是很重要的任务哦。",
            < 15 => "午后也别太辛苦，休息一下，再继续吧。",
            < 18 => "小鲸鱼提醒你，看看远处，让眼睛休息一下吧。",
            < 20 => "忙了一天，辛苦啦，记得按时吃晚饭哦。",
            < 22 => "放松一下吧，今天的你，已经很努力啦。",
            _ => "该慢慢收工啦，小鲸鱼祝你今晚做个好梦。"
        };
        var period = hour switch { 0 => "午夜", < 6 => "凌晨", < 9 => "早上", < 12 => "上午", 12 => "中午", < 18 => "下午", _ => "晚上" };
        var displayHour = hour == 12 ? 12 : hour % 12;
        var spokenHour = displayHour == 2 ? "两" : Hours[displayHour];
        return $"现在是{period}{spokenHour}点整。{reminder}";
    }
}
