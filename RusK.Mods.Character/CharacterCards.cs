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

    /// <summary>(調査) 画面のカードの一覧</summary>
    public static void Dump(WindowCharacterShow w)
    {
        var sb = new StringBuilder($"Character: (調査) キャラの画面 cards={w.m_crtCards?.Count} unlock=[");
        if (w.m_unlockCrts != null) sb.Append(string.Join(",", w.m_unlockCrts.ToArray().Select(m => m == null ? "null" : $"{m.id}")));
        sb.Append($"] cur={w.m_curSelectCrtId}");
        if (w.m_crtCards != null)
            foreach (var c in w.m_crtCards)
                if (c != null) sb.Append($" | {c.gameObject.name} id={c.GetBindId()} active={c.gameObject.activeInHierarchy} button={(c.GetButton() != null ? c.GetButton().isInteractable.ToString() : "-")}");
        CharacterMod.Ctx?.Log.Info(sb.ToString());
    }
}

[HarmonyPatch(typeof(WindowCharacterShow), nameof(WindowCharacterShow.InitialWindow))]
internal static class CharacterShowPatch
{
    private static void Prefix(WindowCharacterShow __instance)
    {
        try { CharacterCards.Add(__instance.m_crtCards); }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: カードを足せません: {e.Message}"); }
    }

    private static void Postfix(WindowCharacterShow __instance)
    {
        try { CharacterCards.Dump(__instance); }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: (調査) 失敗: {e.Message}"); }
    }
}
