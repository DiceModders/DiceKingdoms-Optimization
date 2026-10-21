using UnityEngine;

namespace DiceModders.Optimization;

/// <summary>A tiny frame-rate meter (average over the last ~5 seconds) so the effect of every option can be seen in the Settings screen without any tool.</summary>
internal static class FrameStats
{
    private static float _acc;
    private static int _frames;
    private static float _lastFps;
    private static float _lastMs;

    internal static float Fps => _lastFps;
    internal static float FrameMs => _lastMs;

    internal static void Tick()
    {
        _acc += Time.unscaledDeltaTime;
        _frames++;
        if (_acc < 5f) return;
        _lastFps = _frames / _acc;
        _lastMs = 1000f * _acc / _frames;
        _acc = 0f;
        _frames = 0;
    }

    internal static string Text() => _lastFps <= 0f ? "measuring ..." : $"{_lastFps:0} fps  ({_lastMs:0.0} ms per frame, average of the last 5 seconds)";
}
