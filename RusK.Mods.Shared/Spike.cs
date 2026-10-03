using System.Collections.Generic;
using System.Diagnostics;

namespace RusK.Mods.Shared;

/// <summary>
/// 引っかかりの調査: 処理ごとに、10 秒間で一番長かった 1 回の時間を測り、しきい値を超えたものだけログに出す
/// (平均の FPS は高いのに、ときどき 1 フレームだけ長くなる原因を探す用)
/// </summary>
internal static class Spike
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly Dictionary<string, (double max, double sum, int n)> Stats = new();
    private static double _nextReport = 10;
    public static System.Action<string> Log = null;
    public static double ThresholdMs = 10.0; // 1 フレーム (60 FPS で 16 ms) の大半を使ったときだけ

    public static long Begin() => Clock.ElapsedTicks;

    public static void End(string name, long begin)
    {
        double ms = (Clock.ElapsedTicks - begin) * 1000.0 / Stopwatch.Frequency;
        Stats.TryGetValue(name, out var s);
        Stats[name] = (System.Math.Max(s.max, ms), s.sum + ms, s.n + 1);
        double now = Clock.Elapsed.TotalSeconds;
        if (now < _nextReport) return;
        _nextReport = now + 10;
        foreach (var kv in Stats)
            if (kv.Value.max >= ThresholdMs)
                Log?.Invoke($"(引っかかり) {kv.Key}: 一番長い 1 回 {kv.Value.max:0.0} ms / 平均 {kv.Value.sum / System.Math.Max(1, kv.Value.n):0.00} ms ({kv.Value.n} 回)");
        Stats.Clear();
    }
}
