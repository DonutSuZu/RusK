using System.Collections.Generic;
using System.Diagnostics;
using RusK.API;

namespace RusK.Core;

internal sealed class Notification
{
    public string Text { get; init; }
    public NotifyLevel Level { get; init; }
    public double CreatedAt { get; init; }
}

/// <summary>画面右下に出す通知トーストのキュー</summary>
internal sealed class NotificationManager
{
    public const double Lifetime = 3.0;
    private const int MaxCount = 6;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Notification> _items = new();

    public double Now => _clock.Elapsed.TotalSeconds;
    public IReadOnlyList<Notification> Items => _items;

    public void Push(string text, NotifyLevel level = NotifyLevel.Info)
    {
        Rusk.Log.LogInfo($"[notify] {text}");
        _items.Add(new Notification { Text = text, Level = level, CreatedAt = Now });
        if (_items.Count > MaxCount) _items.RemoveAt(0);
    }

    public void Prune() => _items.RemoveAll(n => Now - n.CreatedAt > Lifetime);
}
