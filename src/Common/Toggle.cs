using BepInEx.Logging;

namespace DiceModders.Shared;

/// <summary>Runtime on/off state flipped by a hotkey. Main-thread only.</summary>
internal sealed class Toggle
{
    private readonly ManualLogSource _log;
    public string Label { get; }
    public bool On { get; private set; }

    public Toggle(string label, bool initial, ManualLogSource log)
    {
        Label = label; On = initial; _log = log;
    }

    public void Flip()
    {
        On = !On;
        _log.LogMessage($"{Label}: {(On ? "ON" : "OFF")}");
    }
}
