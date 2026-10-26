namespace Mosaic.Core;

[Flags]
public enum PetInputActivity { None = 0, Typing = 1, Enter = 2, ShowDesktop = 4, AppClosed = 8 }

/// <summary>A bounded activity mailbox: input callbacks never enqueue UI work or retain text.</summary>
public sealed class PetInputBuffer
{
    private int _pending;
    public void Record(PetTrigger trigger)
    {
        var activity = trigger switch
        {
            PetTrigger.Typing => PetInputActivity.Typing,
            PetTrigger.Enter => PetInputActivity.Enter,
            PetTrigger.ShowDesktop => PetInputActivity.ShowDesktop,
            PetTrigger.AppClosed => PetInputActivity.AppClosed,
            _ => PetInputActivity.None
        };
        if (activity != PetInputActivity.None) Interlocked.Or(ref _pending, (int)activity);
    }
    public PetInputActivity Take() => (PetInputActivity)Interlocked.Exchange(ref _pending, 0);
}
