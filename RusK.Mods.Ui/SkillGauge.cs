using RusK.Mods.Shared;
using UnityEngine;
using static UnityEngine.Object;

namespace RusK.Mods.Ui;

/// <summary>
/// 追加攻撃 (スキル) のゲージの読み取り。
/// ゲーム自身の HUD (SkillUIController の fillImage) の塗り量をそのまま読むので、
/// キャラが変わっても、そのキャラのゲージが表示される。
/// </summary>
internal static class SkillGauge
{
    private static SkillUIController[] _controllers;
    private static float _nextScan;

    public static float? Read()
    {
        // FindObjectsOfType は重いので 1 秒に 1 回だけ探し直す
        if (_controllers == null || Time.unscaledTime >= _nextScan)
        {
            _nextScan = Time.unscaledTime + 1f;
            var found = FindObjectsOfType<SkillUIController>();
            _controllers = new SkillUIController[found.Length];
            for (int i = 0; i < found.Length; i++) _controllers[i] = found[i];
        }

        foreach (var c in _controllers)
        {
            if (c == null || !c.gameObject.activeInHierarchy) continue;
            var image = c.m_fillImage;
            if (image == null) continue;
            return image.fillAmount;
        }
        return null;
    }

    public static void Reset() => _controllers = null;
}
