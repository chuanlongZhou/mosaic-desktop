using System.Text.Json.Serialization;

namespace Mosaic.Core.Models;

public sealed class AppearanceSettings
{
    public string Theme { get; set; } = "Midnight";
    public string Accent { get; set; } = "Blue";
    public bool TransparencyEnabled { get; set; }
    public double SurfaceOpacity { get; set; } = .9;
    public bool AnimationsEnabled { get; set; } = true;
    public static readonly string[] Themes = ["Midnight", "Graphite", "Forest", "Plum", "Light"];
    public static readonly string[] Accents = ["Blue", "Mint", "Violet", "Rose", "Amber"];
    public void Validate()
    {
        if (!Themes.Contains(Theme) || !Accents.Contains(Accent) || !double.IsFinite(SurfaceOpacity) || SurfaceOpacity is < .65 or > 1)
            throw new InvalidDataException("外观设置无效：请选择有效主题与强调色，透明表面不透明度为 65%–100%。");
    }
}

public sealed class ShortcutBinding
{
    public bool Enabled { get; set; } = true;
    public uint Modifiers { get; set; } = 3;
    public uint Key { get; set; }
    [JsonIgnore] public string Identity => $"{Modifiers}:{Key}";
    public ShortcutBinding Copy() => new() { Enabled = Enabled, Modifiers = Modifiers, Key = Key };
    public void Validate()
    {
        if (Modifiers is 0 or > 7 || !(Key == 0x20 || Key is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 or >= 0x70 and <= 0x7B))
            throw new InvalidDataException("快捷键须为 Ctrl / Alt / Shift 加字母、数字、空格或 F1–F12。");
    }
}

public sealed class ShortcutSettings
{
    private ShortcutBinding _pinMenuBar = new() { Key = 0x54 };
    private bool _pinMenuBarSpecified;
    public ShortcutBinding Desktop { get; set; } = new() { Key = 0x4D };
    public ShortcutBinding Dashboard { get; set; } = new() { Key = 0x44 };
    public ShortcutBinding Library { get; set; } = new() { Key = 0x4C };
    public ShortcutBinding Workspaces { get; set; } = new() { Key = 0x57 };
    public ShortcutBinding EditLayout { get; set; } = new() { Key = 0x47 };
    public ShortcutBinding PinMenuBar { get => _pinMenuBar; set { _pinMenuBar = value; _pinMenuBarSpecified = true; } }
    public IEnumerable<(string Action, string Label, ShortcutBinding Binding)> Entries()
    {
        yield return ("desktop", "切换桌面模式", Desktop);
        yield return ("dashboard", "打开设置中心", Dashboard);
        yield return ("library", "打开资源库", Library);
        yield return ("workspaces", "打开工作空间", Workspaces);
        yield return ("edit", "编辑桌面布局", EditLayout);
        yield return ("menu-pin", "固定／取消固定顶部菜单", PinMenuBar);
    }
    internal void MigrateMenuPin(uint searchModifiers, uint searchKey, bool searchEnabled)
    {
        // A newly added default must not invalidate an older user's custom key.
        if (_pinMenuBarSpecified) return;
        if (Entries().Any(x => !ReferenceEquals(x.Binding, PinMenuBar) && x.Binding is { Enabled: true } && x.Binding.Identity == PinMenuBar.Identity) ||
            (searchEnabled && PinMenuBar.Identity == $"{searchModifiers}:{searchKey}")) PinMenuBar.Enabled = false;
    }
    public void Validate(uint searchModifiers, uint searchKey, bool searchEnabled)
    {
        var used = new HashSet<string>();
        if (searchEnabled) used.Add($"{searchModifiers}:{searchKey}");
        foreach (var (_, label, binding) in Entries())
        {
            if (binding is null) throw new InvalidDataException("快捷键设置不能为空。");
            binding.Validate();
            if (binding.Enabled && !used.Add(binding.Identity)) throw new InvalidDataException($"“{label}”与其他操作使用了同一组快捷键。");
        }
    }
}
