using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RusK.Mods.Character;

/// <summary>
/// 出撃前のキャラ選択 (WindowBattleCharacterChoose)。カードは横並び (CardRoot の HorizontalLayoutGroup) に 7 枚だけ用意されていて、
/// 画面の幅 (2560) にちょうど 7 枚入る。キャラが 8 人以上 (ゲームのキャラを全員解放 + 新しいキャラなど) になると、
/// カードが足りず、入らない。
/// - 画面を作る直前に、キャラの数までカードを複製して足す
/// - 表示中のカードが画面の幅を超えたら、並びを広げて横にスクロールできるようにする (ホイール・ドラッグ・選んだカードへ自動で寄せる)
/// </summary>
internal static class CharacterChoose
{
    private static float _next;

    public static void AddCards(WindowBattleCharacterChoose w)
    {
        var cards = w?.m_crtCards;
        if (cards == null || cards.Count == 0) return;
        // 解放済みの、操作できるキャラの数 (キャラの一覧には敵なども入っているので、解放済みだけ数える)
        int need = 0;
        try
        {
            foreach (var m in GameUtil.Instance.GetCharacterContainer().characters)
                if (m != null && GameUtil.Instance.IsCharacterUnlock(m.id)) need++;
        }
        catch { }
        var template = cards[cards.Count - 1];
        if (template == null) return;
        for (int i = cards.Count; i < need; i++)
        {
            var go = Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = template.gameObject.name + "_RusK" + i;
            var bm = go.GetComponent<Michsky.UI.Reach.ButtonManager>();
            if (bm == null) { Object.Destroy(go); break; }
            cards.Add(bm);
        }
        if (need > 7) CharacterMod.Ctx?.Log.Info($"Character: キャラ選択のカードを {cards.Count} 枚に (キャラ {need} 人)");
    }

    /// <summary>毎フレーム: 選択の画面が開いていれば、スクロールの用意と、選んだカードへ寄せる</summary>
    public static void Tick()
    {
        try
        {
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.5f;
                // 画面は開いたとき (InitialWindow) に覚えておく (毎回探すと重い)
                if (Window != null && Window.gameObject.activeInHierarchy) Fit(Window);
            }
            Follow();
        }
        catch { }
    }

    private static ScrollRect _scroll;
    public static WindowBattleCharacterChoose Window;

    private static void Fit(WindowBattleCharacterChoose w)
    {
        var root = w.m_cardRoot?.TryCast<RectTransform>();
        if (root == null) return;
        var view = w.transform.TryCast<RectTransform>();
        // カードの幅: 画面の幅に 7 枚 (ゲームの作り)。並べ直した後の幅を測ると、崩れたときに戻らないので決めておく
        float cardW = view.rect.width / 7f;
        int shown = 0;
        for (int i = 0; i < root.childCount; i++)
            if (root.GetChild(i).gameObject.activeSelf) shown++;
        if (shown == 0 || cardW <= 0f) return;
        var layout = root.GetComponent<HorizontalLayoutGroup>();
        float pad = layout != null ? layout.padding.left + layout.padding.right : 0f;
        float spacing = layout != null ? layout.spacing : 0f;
        float width = pad + shown * cardW + Mathf.Max(0, shown - 1) * spacing;
        if (width <= view.rect.width + 1f) return; // 入りきる (ゲームのまま)
        if (Mathf.Abs(root.rect.width - width) > 1f)
        {
            // 左端を基準に、決まった幅で並べる (カードの幅はそのまま)
            if (layout != null) { layout.childControlWidth = false; layout.childForceExpandWidth = false; }
            for (int i = 0; i < root.childCount; i++)
                root.GetChild(i).TryCast<RectTransform>()?.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cardW);
            root.anchorMin = new Vector2(0f, root.anchorMin.y);
            root.anchorMax = new Vector2(0f, root.anchorMax.y);
            root.pivot = new Vector2(0f, root.pivot.y);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            root.anchoredPosition = new Vector2(0f, root.anchoredPosition.y);
            CharacterMod.Ctx?.Log.Info($"Character: キャラ選択のカード {shown} 枚 (幅 {width:0}) を横にスクロールできるように");
        }
        // 十字キー・スティックの左右: 表示中のカードを左から順に、隣どうしでつなぐ (ゲームの設定は元の 7 枚の間だけ)
        Selectable prev = null;
        for (int i = 0; i < root.childCount; i++)
        {
            var c = root.GetChild(i);
            if (!c.gameObject.activeSelf) continue;
            var sel = c.GetComponent<Selectable>();
            if (sel == null) continue;
            var nav = sel.navigation;
            nav.mode = Navigation.Mode.Explicit;
            nav.selectOnLeft = prev;
            nav.selectOnRight = null;
            sel.navigation = nav;
            if (prev != null)
            {
                var pn = prev.navigation;
                pn.selectOnRight = sel;
                prev.navigation = pn;
            }
            prev = sel;
        }

        var scroll = w.GetComponent<ScrollRect>() ?? w.gameObject.AddComponent<ScrollRect>();
        scroll.content = root;
        scroll.viewport = view;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.scrollSensitivity = 60f;
        _scroll = scroll;
    }

    /// <summary>パッド・キーで選んだカードが画面の外なら、見えるところまで寄せる</summary>
    private static void Follow()
    {
        if (_scroll == null || _scroll.content == null) return;
        var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (sel == null || sel.transform.parent != _scroll.content) return;
        var card = sel.transform.TryCast<RectTransform>();
        var view = _scroll.viewport;
        var content = _scroll.content;
        float left = card.anchoredPosition.x - card.rect.width * card.pivot.x + content.anchoredPosition.x;
        float right = left + card.rect.width;
        float x = content.anchoredPosition.x;
        if (left < 0f) x -= left;
        else if (right > view.rect.width) x -= right - view.rect.width;
        else return;
        x = Mathf.Clamp(x, view.rect.width - content.rect.width, 0f);
        content.anchoredPosition = Vector2.Lerp(content.anchoredPosition, new Vector2(x, content.anchoredPosition.y), 0.25f);
    }
}

[HarmonyPatch(typeof(WindowBattleCharacterChoose), nameof(WindowBattleCharacterChoose.InitialWindow))]
internal static class CharacterChoosePatch
{
    private static void Prefix(WindowBattleCharacterChoose __instance)
    {
        CharacterChoose.Window = __instance;
        try { CharacterChoose.AddCards(__instance); }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: キャラ選択のカードを足せません: {e.Message}"); }
    }
}
