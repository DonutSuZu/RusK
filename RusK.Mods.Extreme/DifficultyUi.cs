using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Michsky.UI.Reach;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RusK.Mods.Extreme;

/// <summary>EXTREME の表示名・説明文と、表示の差し替え</summary>
internal static class DifficultyUi
{
    /// <summary>ゲームに表示名が無いときに使う翻訳キー (GameUtil.GetLocale で RusK が答える)</summary>
    public const string NameKey = "RUSK_DIFFICULTY_EXTREME";
    public static string Name => L.T("エクストリーム");
    public const string Title = "EXTREME";
    public static string Description =>
        L.T("HARD をさらに超える最高難度。敵の強さは<color=#FF6666>最高</color>で、HP・攻撃力・攻撃の頻度・シールドが大幅に強化されます。");

    private static readonly Color ExtremeTint = new(1f, 0.45f, 0.45f, 1f);
    private static readonly Dictionary<IntPtr, Color> OriginalImageColor = new();
    private static readonly List<WindowDifficultyChoose> Windows = new();

    /// <summary>その翻訳キーの文章がゲームにあるか (無いと空やキーそのものが返る)</summary>
    public static bool HasLocale(string key)
    {
        if (string.IsNullOrEmpty(key) || key == NameKey) return false;
        try
        {
            var text = GameUtil.GetLocale(key);
            return !string.IsNullOrWhiteSpace(text) && text != key;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>説明文として使える文章か (空・翻訳キーのまま・HARD の説明の流用なら false)</summary>
    public static bool LooksMissing(string text) =>
        string.IsNullOrWhiteSpace(text) || (!text.Contains(" ") && !text.Contains("。") && text.Contains("_"));

    // ---------------------------------------------------------------- 初回の難易度選択画面 (ボタン式)

    /// <summary>HARD ボタンを複製して EXTREME ボタンを作る</summary>
    public static void Inject(WindowDifficultyChoose window)
    {
        var list = window.m_difficultyButtons;
        if (list == null) return;

        DifficultyButton hard = null, normal = null;
        for (int i = 0; i < list.Count; i++)
        {
            var b = list[i];
            if (b == null) continue;
            if (b.difficultyType == GameDifficultyType.Extreme) return; // 追加済み (またはゲームに元からある)
            if (b.difficultyType == GameDifficultyType.Hard) hard = b;
            if (b.difficultyType == GameDifficultyType.Normal) normal = b;
        }
        if (hard?.button == null)
        {
            ExtremeState.Log?.Warning("EXTREME: HARD ボタンが見つからないので追加できません");
            return;
        }

        var src = hard.button.gameObject;
        var parent = src.transform.parent;
        var clone = Object.Instantiate(src, parent);
        clone.name = "Difficulty_Extreme (RusK)";
        clone.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);

        // 並べ方: 親にレイアウトがあれば任せる。なければ NORMAL→HARD と同じ間隔で HARD の隣に置く
        bool hasLayout = parent != null && parent.GetComponent<LayoutGroup>() != null;
        var srcRt = src.GetComponent<RectTransform>();
        var cloneRt = clone.GetComponent<RectTransform>();
        if (!hasLayout && srcRt != null && cloneRt != null && normal?.button != null)
        {
            var normalRt = normal.button.GetComponent<RectTransform>();
            if (normalRt != null)
                cloneRt.anchoredPosition = srcRt.anchoredPosition + (srcRt.anchoredPosition - normalRt.anchoredPosition);
        }

        // 複製側の翻訳部品を外す (ボタンの文字を HARD に戻されないように)
        foreach (var c in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (c == null) continue;
            var name = c.GetIl2CppType().Name;
            if (name.Contains("Locale") || name.Contains("Localiz"))
            {
                c.enabled = false;
                Object.Destroy(c);
            }
        }

        var manager = clone.GetComponent<ButtonManager>();
        if (manager == null)
        {
            ExtremeState.Log?.Warning("EXTREME: 複製したボタンに ButtonManager がありません");
            Object.Destroy(clone);
            return;
        }
        manager.buttonText = Title;
        manager.SetText(Title);

        // 元の HARD ボタンのクリック処理 (インスペクタで設定されたもの) は止め、EXTREME 用を足す
        var onClick = manager.onClick;
        for (int i = 0; i < onClick.GetPersistentEventCount(); i++)
            onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        var w = window;
        onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(
            new Action(() => w.ChangeDifficultyView(GameDifficultyType.Extreme))));

        list.Add(new DifficultyButton { button = manager, difficultyType = GameDifficultyType.Extreme });
        LogLayout(parent, hasLayout, srcRt, cloneRt);
    }

    /// <summary>ボタン式の画面の表示を EXTREME に差し替える (画像は HARD のものを赤く染めて使う)</summary>
    public static void ApplyExtremeView(WindowDifficultyChoose window)
    {
        try
        {
            if (window.m_difficultyText != null) window.m_difficultyText.text = Title;
            if (window.m_difficultyDesc != null) window.m_difficultyDesc.text = Description + "\n" + ExtremeState.BoostSummary;
            var img = window.m_difficultyImage;
            if (img != null)
            {
                if (!OriginalImageColor.ContainsKey(img.Pointer)) OriginalImageColor[img.Pointer] = img.color;
                img.color = ExtremeTint;
            }
            if (!Windows.Contains(window)) Windows.Add(window);
        }
        catch (Exception e)
        {
            ExtremeState.Log?.Warning($"EXTREME view failed: {e.Message}");
        }
    }

    public static void RestoreView(WindowDifficultyChoose window)
    {
        try
        {
            var img = window.m_difficultyImage;
            if (img != null && OriginalImageColor.TryGetValue(img.Pointer, out var c)) img.color = c;
        }
        catch { }
    }

    public static void RestoreViews()
    {
        foreach (var w in Windows)
            if (w != null) RestoreView(w);
        Windows.Clear();
    }

    private static void LogLayout(Transform parent, bool hasLayout, RectTransform src, RectTransform clone)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"EXTREME ボタンを追加: 親 '{parent?.name}' レイアウト={(hasLayout ? "あり" : "なし")}");
            if (src != null) sb.Append($" HARD pos={src.anchoredPosition} size={src.sizeDelta}");
            if (clone != null) sb.Append($" EXTREME pos={clone.anchoredPosition}");
            ExtremeState.Log?.Info(sb.ToString());
        }
        catch { }
    }
}

// ==================================================================== 設定画面 (WindowPlayerSetting) の「難易度 ← →」

// bool IsDifficultyOptionAvailable(GameDifficultyType)  … EXTREME も選べることにする
[HarmonyPatch(typeof(WindowPlayerSetting), nameof(WindowPlayerSetting.IsDifficultyOptionAvailable))]
internal static class SettingAvailablePatch
{
    private static void Postfix(GameDifficultyType difficultyType, ref bool __result)
    {
        if (difficultyType == GameDifficultyType.Extreme && ExtremeState.Unlocked) __result = true;
    }
}

// GameDifficultyType[] GetDifficultyOptions()  … 選択肢に EXTREME が無ければ最後に足す
[HarmonyPatch(typeof(WindowPlayerSetting), nameof(WindowPlayerSetting.GetDifficultyOptions))]
internal static class SettingOptionsPatch
{
    private static void Postfix(ref Il2CppStructArray<GameDifficultyType> __result)
    {
        if (!ExtremeState.Unlocked || __result == null) return;
        for (int i = 0; i < __result.Length; i++)
            if (__result[i] == GameDifficultyType.Extreme) return;

        var extended = new Il2CppStructArray<GameDifficultyType>(__result.Length + 1);
        for (int i = 0; i < __result.Length; i++) extended[i] = __result[i];
        extended[__result.Length] = GameDifficultyType.Extreme;
        __result = extended;
    }
}

// GameDifficultyType GetAvailableDifficultyType(GameDifficultyType)  … 保存されている EXTREME を HARD に戻さない
[HarmonyPatch(typeof(WindowPlayerSetting), nameof(WindowPlayerSetting.GetAvailableDifficultyType))]
internal static class SettingAvailableTypePatch
{
    private static void Postfix(GameDifficultyType difficultyType, ref GameDifficultyType __result)
    {
        if (difficultyType == GameDifficultyType.Extreme && ExtremeState.Unlocked) __result = GameDifficultyType.Extreme;
    }
}

// string GetDifficultyLocaleKey(GameDifficultyType)  … ゲームに EXTREME の表示名が無ければ RusK のキーにする
[HarmonyPatch(typeof(WindowPlayerSetting), nameof(WindowPlayerSetting.GetDifficultyLocaleKey))]
internal static class SettingLocaleKeyPatch
{
    private static void Postfix(GameDifficultyType difficultyType, ref string __result)
    {
        if (difficultyType != GameDifficultyType.Extreme || !ExtremeState.Unlocked) return;
        if (!DifficultyUi.HasLocale(__result))
        {
            ExtremeState.Log?.Info($"EXTREME: ゲームの表示名 ('{__result}') が無いので「{DifficultyUi.Name}」を使います");
            __result = DifficultyUi.NameKey;
        }
    }
}

// void RefreshDifficultySelectorOptions()  … 選択肢を作り直した直後に確認する。
// EXTREME が無ければ足し (GetDifficultyOptions 以外から一覧を作っている場合の保険)、対応をログに出す
[HarmonyPatch(typeof(WindowPlayerSetting), nameof(WindowPlayerSetting.RefreshDifficultySelectorOptions))]
internal static class SettingRefreshPatch
{
    private static void Postfix(WindowPlayerSetting __instance)
    {
        if (!ExtremeState.Unlocked) return;
        try
        {
            var selector = __instance.m_difficultySelector;
            var items = selector?.m_selectItems;
            if (items == null) return;

            var map = new System.Text.StringBuilder();
            bool hasExtreme = false;
            for (int i = 0; i < items.Count; i++)
            {
                var type = __instance.GetDifficultyTypeByIndex(i);
                if (type == GameDifficultyType.Extreme) hasExtreme = true;
                map.Append($" [{i}]{type}:{items[i]?.localeKey}");
            }

            if (!hasExtreme)
            {
                items.Add(new SelectItem { localeKey = __instance.GetDifficultyLocaleKey(GameDifficultyType.Extreme) });
                int cur = __instance.GetDifficultySelectorIndex(ExtremeState.GameDifficulty);
                selector.RefreshSelectItems(items, cur < 0 ? 0 : cur, false);
                map.Append($" + [{items.Count - 1}]EXTREME (追加)");
            }
            ExtremeState.Log?.Info("難易度の選択肢:" + map);
        }
        catch (Exception e)
        {
            ExtremeState.Log?.Warning($"EXTREME selector refresh failed: {e.Message}");
        }
    }
}

// static string GameUtil.GetLocale(string key)  … RusK のキーに表示名を返す
[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.GetLocale))]
internal static class LocalePatch
{
    private static void Postfix(string key, ref string __result)
    {
        if (key == DifficultyUi.NameKey) __result = DifficultyUi.Name;
    }
}

// void SetDifficultyDesc(GameDifficultyType)  … EXTREME の説明文 (無ければ RusK の文章) と、RusK の強化内容を出す
[HarmonyPatch(typeof(WindowPlayerSetting), nameof(WindowPlayerSetting.SetDifficultyDesc))]
internal static class SettingDescPatch
{
    private static string _lastOther;

    private static void Postfix(WindowPlayerSetting __instance, GameDifficultyType difficultyType)
    {
        try
        {
            var desc = __instance.m_difficultyDesc;
            if (desc == null) return;
            if (difficultyType != GameDifficultyType.Extreme)
            {
                _lastOther = desc.text;
                return;
            }
            if (!ExtremeState.Unlocked) return;

            // ゲームに EXTREME の説明が無い (空・キーのまま・直前の難易度の説明が残っている) なら RusK の説明にする
            string text = desc.text;
            if (DifficultyUi.LooksMissing(text) || text == _lastOther) text = DifficultyUi.Description;
            desc.text = text + "\n<size=80%><color=#FF9A9A>" + ExtremeState.BoostSummary + "</color></size>";
        }
        catch (Exception e)
        {
            ExtremeState.Log?.Warning($"EXTREME desc failed: {e.Message}");
        }
    }
}

// ==================================================================== 初回の難易度選択画面 (WindowDifficultyChoose)

// Awake の直前に EXTREME ボタンを足しておくと、ゲームが Awake でボタンを配線するときに一緒に処理される
[HarmonyPatch(typeof(WindowDifficultyChoose), nameof(WindowDifficultyChoose.Awake))]
internal static class DifficultyAwakePatch
{
    private static void Prefix(WindowDifficultyChoose __instance)
    {
        if (!ExtremeState.Unlocked) return;
        try { DifficultyUi.Inject(__instance); }
        catch (Exception e) { ExtremeState.Log?.Error($"EXTREME inject failed: {e}"); }
    }
}

// 画面を開いたとき、EXTREME を選んでいれば EXTREME を表示しておく
[HarmonyPatch(typeof(WindowDifficultyChoose), nameof(WindowDifficultyChoose.InitialWindow))]
internal static class DifficultyInitialPatch
{
    private static void Postfix(WindowDifficultyChoose __instance)
    {
        try
        {
            if (ExtremeState.Unlocked && ExtremeState.GameDifficulty == GameDifficultyType.Extreme)
                __instance.ChangeDifficultyView(GameDifficultyType.Extreme);
        }
        catch (Exception e) { ExtremeState.Log?.Warning($"EXTREME initial view failed: {e.Message}"); }
    }
}

// void ChangeDifficultyView(GameDifficultyType)
// この画面は EXTREME 用の画像などを持っていないかもしれないので、HARD の表示を流用して文字と色を差し替える
[HarmonyPatch(typeof(WindowDifficultyChoose), nameof(WindowDifficultyChoose.ChangeDifficultyView))]
internal static class DifficultyViewPatch
{
    private static void Prefix(ref GameDifficultyType difficultyType)
    {
        ExtremeState.Pending = difficultyType == GameDifficultyType.Extreme;
        if (ExtremeState.Pending) difficultyType = GameDifficultyType.Hard;
    }

    private static void Postfix(WindowDifficultyChoose __instance)
    {
        if (ExtremeState.Pending) DifficultyUi.ApplyExtremeView(__instance);
        else DifficultyUi.RestoreView(__instance);
    }
}

// void SetGameDifficulty(GameDifficultyType, bool saveGame)
// 表示は HARD を流用しているので、EXTREME を表示中に決定されたら本来の EXTREME にする
[HarmonyPatch(typeof(WindowDifficultyChoose), nameof(WindowDifficultyChoose.SetGameDifficulty))]
internal static class DifficultySetPatch
{
    private static void Prefix(ref GameDifficultyType difficultyType, bool saveGame)
    {
        ExtremeState.Log?.Info($"SetGameDifficulty({difficultyType}, save={saveGame}) EXTREME 表示中={ExtremeState.Pending}");
        if (ExtremeState.Pending && difficultyType == GameDifficultyType.Hard) difficultyType = GameDifficultyType.Extreme;
    }
}

// 画面を閉じたら「表示中」の状態をリセット
[HarmonyPatch(typeof(WindowDifficultyChoose), nameof(WindowDifficultyChoose.OnDisable))]
internal static class DifficultyClosePatch
{
    private static void Postfix() => ExtremeState.Pending = false;
}
