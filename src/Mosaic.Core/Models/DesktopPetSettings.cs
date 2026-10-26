namespace Mosaic.Core.Models;

public class DesktopPetOptions
{
    public PetCharacter Character { get; set; } = PetCharacter.WhaleGirl;
    public bool Enabled { get; set; }
    public bool FollowCursor { get; set; } = true;
    public bool ReactToEnter { get; set; } = true;
    public bool ReactToTyping { get; set; } = true;
    public bool ReactToDesktop { get; set; } = true;
    public bool ReactToAppClose { get; set; } = true;
    public bool SleepWhenIdle { get; set; } = true;
    public bool ShowBubbles { get; set; } = true;
    public bool HideInFullscreen { get; set; } = true;
    public int IdleSeconds { get; set; } = 90;
    public int Size { get; set; } = 220;
    public double? Left { get; set; }
    public double? Top { get; set; }

    public virtual void Validate()
    {
        if (!Enum.IsDefined(Character)) throw new InvalidDataException("请选择有效的桌宠角色。");
        if (IdleSeconds is < 30 or > 1800 || Size is < 128 or > 320 ||
            Left is { } left && !double.IsFinite(left) || Top is { } top && !double.IsFinite(top))
            throw new InvalidDataException("桌宠设置无效：大小须为 128–320，入睡时间须为 30–1800 秒，位置须为有效坐标。");
    }

    public void CopyTo(DesktopPetOptions other)
    {
        other.Character = Character; other.Enabled = Enabled; other.FollowCursor = FollowCursor;
        other.ReactToEnter = ReactToEnter; other.ReactToTyping = ReactToTyping; other.ReactToDesktop = ReactToDesktop;
        other.ReactToAppClose = ReactToAppClose; other.SleepWhenIdle = SleepWhenIdle; other.ShowBubbles = ShowBubbles;
        other.HideInFullscreen = HideInFullscreen; other.IdleSeconds = IdleSeconds; other.Size = Size; other.Left = Left; other.Top = Top;
    }
}

public sealed class DesktopPetCompanion : DesktopPetOptions
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DesktopPetCompanion() => Enabled = true;
    public DesktopPetCompanion Copy() { var copy = new DesktopPetCompanion { Id = Id }; CopyTo(copy); return copy; }
}

public sealed class DesktopPetSettings : DesktopPetOptions
{
    public const int MaximumPets = 5;
    public const string PrimaryId = "primary";
    public bool PrimaryVisible { get; set; } = true;
    public List<DesktopPetCompanion> Companions { get; set; } = [];

    public DesktopPetOptions GetPet(string id) => id == PrimaryId ? this : Companions.Single(pet => pet.Id == id);
    public IEnumerable<(string Id, DesktopPetOptions Options, bool Visible)> Slots()
    {
        yield return (PrimaryId, this, PrimaryVisible);
        foreach (var companion in Companions) yield return (companion.Id, companion, companion.Enabled);
    }
    public void Enable()
    {
        Enabled = true;
        if (!Slots().Any(slot => slot.Visible)) PrimaryVisible = true;
    }
    public DesktopPetSettings Copy()
    {
        var copy = new DesktopPetSettings(); CopyAllTo(copy); return copy;
    }
    public void CopyAllTo(DesktopPetSettings other)
    {
        CopyTo(other); other.PrimaryVisible = PrimaryVisible; other.Companions = Companions.Select(pet => pet.Copy()).ToList();
    }
    public override void Validate()
    {
        base.Validate();
        if (Companions is null || Companions.Count >= MaximumPets) throw new InvalidDataException("最多同时保留 5 位桌宠。");
        var ids = new HashSet<string>(StringComparer.Ordinal) { PrimaryId };
        foreach (var companion in Companions)
        {
            if (companion is null || companion.Id is null || !Guid.TryParseExact(companion.Id, "N", out _) || !ids.Add(companion.Id))
                throw new InvalidDataException("桌宠的独立编号无效或重复。");
            companion.Validate();
        }
    }
}
