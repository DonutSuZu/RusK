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

[HarmonyPatch(typeof(WindowCharacterShow), nameof(WindowCharacterShow.InitialWindow))]
internal static class CharacterShowPatch
{
    private static void Prefix(WindowCharacterShow __instance)
    {
        try { CharacterCards.Add(__instance.m_crtCards); }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: カードを足せません: {e.Message}"); }
    }
}
