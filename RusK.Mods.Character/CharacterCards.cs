using System;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Character;

/// <summary>
/// キャラを並べる画面 (WindowCharacterShow: 拠点の「キャラクター」・出撃前の選択) のカードは、ゲームのキャラの数だけ最初から用意されていて、
/// どのキャラのカードかがカードに書き込まれている (CharacterShowCard.m_bindId)。新しいキャラにはカードが無いので、
/// 画面を作る直前に土台のキャラのカードを複製して、新しいキャラのカードとして足す
/// </summary>
internal static class CharacterCards
{
    public static void Add(Il2CppSystem.Collections.Generic.List<CharacterShowCard> cards)
    {
        if (cards == null || cards.Count == 0) return;
        foreach (var def in CharacterRegistry.Defs)
        {
            if (cards.ToArray().Any(c => c != null && (long)Math.Round(c.GetBindId()) == def.Id)) continue;
            var template = cards.ToArray().FirstOrDefault(c => c != null && (long)Math.Round(c.GetBindId()) == def.Base)
                           ?? cards.ToArray().LastOrDefault(c => c != null);
            if (template == null) continue;
            var go = Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = template.gameObject.name + "_" + def.Key;
            var card = go.GetComponent<CharacterShowCard>();
            card.SetBindId(def.Id);
            go.transform.SetAsLastSibling();
            cards.Add(card);
            CharacterMod.Ctx?.Log.Info($"Character: {def.Key} のカードを足しました (元 {template.gameObject.name})");
        }
    }
}

/// <summary>
/// キャラを並べる画面のカードは、スクロールの枠 (ScrollRect) の中に並んでいるが、並べる枠 (Content) の高さが決まっていて
/// カードの数に合わせて伸びない (8 枚目からはみ出して見えず、スクロールもできない)。表示中のカードの数に合わせて高さを伸ばす
/// </summary>
internal static class CardScroll
{
    private static float _next;

    private static RectTransform _content;
    public static WindowCharacterShow Window;

    /// <summary>十字キーで選んだカードが枠の外なら、見えるところまで縦に寄せる</summary>
    private static void Follow()
    {
        if (_content == null) return;
        var es = UnityEngine.EventSystems.EventSystem.current;
        var sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null || sel.transform.parent != _content) return;
        var view = _content.parent?.TryCast<RectTransform>();
        if (view == null) return;
        var card = sel.transform.TryCast<RectTransform>();
        // 並びの上端から見たカードの上下 (下向きが +)
        float top = -(card.anchoredPosition.y + card.rect.height * (1f - card.pivot.y)) - _content.anchoredPosition.y;
        float bottom = top + card.rect.height;
        float y = _content.anchoredPosition.y;
        if (top < 0f) y += top;
        else if (bottom > view.rect.height) y += bottom - view.rect.height;
        else return;
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, _content.rect.height - view.rect.height));
        _content.anchoredPosition = Vector2.Lerp(_content.anchoredPosition, new Vector2(_content.anchoredPosition.x, y), 0.25f);
    }

    public static void Tick()
    {
        try { Follow(); } catch { }
        if (UnityEngine.Time.unscaledTime < _next) return;
        _next = UnityEngine.Time.unscaledTime + 0.5f;
        // 画面は開いたとき (InitialWindow) に覚えておく (毎回探すと重い: 全部の部品を見る)
        try { if (Window != null && Window.gameObject.activeInHierarchy) Fit(Window); }
        catch { Window = null; }
    }

    public static void Fit(WindowCharacterShow w)
    {
        var cards = w?.m_crtCards;
        if (cards == null || cards.Count == 0 || cards[0] == null) return;
        var content = cards[0].transform.parent?.TryCast<RectTransform>();
        var grid = content?.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        if (content == null || grid == null) return;
        int shown = 0;
        for (int i = 0; i < content.childCount; i++)
            if (content.GetChild(i).gameObject.activeSelf) shown++;
        int perRow = grid.constraint == UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount ? Mathf.Max(1, grid.constraintCount) : 1;
        int rows = (shown + perRow - 1) / perRow;
        float h = grid.padding.top + grid.padding.bottom + rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
        // 十字キー・スティックの上下: 表示中のカードを上から順に、隣どうしでつなぐ (足したカードにも移れるように)
        UnityEngine.UI.Selectable prev = null;
        for (int i = 0; i < content.childCount; i++)
        {
            var c = content.GetChild(i);
            if (!c.gameObject.activeSelf) continue;
            var sel = c.GetComponent<UnityEngine.UI.Selectable>();
            if (sel == null) continue;
            var nav = sel.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.Explicit;
            nav.selectOnUp = prev;
            nav.selectOnDown = null;
            sel.navigation = nav;
            if (prev != null)
            {
                var pn = prev.navigation;
                pn.selectOnDown = sel;
                prev.navigation = pn;
            }
            prev = sel;
        }
        _content = content;

        if (h <= content.rect.height + 0.5f) return; // 足りていれば (ゲームのまま) 変えない
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        var scroll = content.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll != null) scroll.vertical = true;
        CharacterMod.Ctx?.Log.Info($"Character: キャラの画面のカード {shown} 枚に合わせて、並びの高さを {h:0} に (スクロールできるように)");
    }
}

[HarmonyPatch(typeof(WindowCharacterShow), nameof(WindowCharacterShow.InitialWindow))]
internal static class CharacterShowPatch
{
    private static void Prefix(WindowCharacterShow __instance)
    {
        CardScroll.Window = __instance;
        try { CharacterCards.Add(__instance.m_crtCards); }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: カードを足せません: {e.Message}"); }
    }
}
