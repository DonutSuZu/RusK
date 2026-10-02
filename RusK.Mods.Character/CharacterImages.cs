using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Character;

/// <summary>
/// 新しいキャラの画像 (キャラの画面・出撃前の選択などで、ゲームが 〈種類〉_〈ID〉 の名前で読む絵)。
/// - お手本: ゲームが新しいキャラの絵を読もうとしたとき、土台のキャラの絵を PNG にして 〈キャラのフォルダ〉/images_template に書き出す
/// - 差し替え: 〈キャラのフォルダ〉/images/〈種類〉.png があれば、土台の絵の代わりに使う (大きさ・中心の位置は土台の絵に合わせる)
/// </summary>
internal static class CharacterImages
{
    private static readonly Dictionary<string, Object> Cache = new();
    private static readonly HashSet<string> Exported = new();

    /// <summary>種類 (rolechoose など) の差し替えの画像。無ければ null</summary>
    public static Object Load(CharacterDef def, string kind, Il2CppSystem.Type type, Object baseAsset)
    {
        var file = Path.Combine(def.Folder, "images", kind + ".png");
        string key = def.Id + "/" + kind + "/" + (type?.Name ?? "any");
        if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
        if (!File.Exists(file)) return null;
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = kind + "_" + def.Id };
            ImageConversion.LoadImage(tex, File.ReadAllBytes(file));
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Object result = tex;
            var baseSprite = baseAsset?.TryCast<Sprite>();
            bool wantSprite = baseSprite != null || type?.Name == nameof(Sprite);
            if (wantSprite)
            {
                // 土台の絵と同じ大きさ・中心で出るように (画像の解像度が違っても、ピクセル/単位を合わせて同じ大きさにする)
                var pivot = new Vector2(0.5f, 0.5f);
                float ppu = 100f;
                var border = Vector4.zero;
                if (baseSprite != null)
                {
                    tex = Pad(tex, baseSprite);
                    var r = baseSprite.rect;
                    pivot = new Vector2(baseSprite.pivot.x / r.width, baseSprite.pivot.y / r.height);
                    float scale = tex.width / Mathf.Max(1f, r.width);
                    ppu = baseSprite.pixelsPerUnit * scale;
                    border = baseSprite.border * scale;
                }
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu, 0, SpriteMeshType.FullRect, border);
                sprite.name = kind + "_" + def.Id;
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                result = sprite;
            }
            Cache[key] = result;
            CharacterMod.Ctx?.Log.Info($"Character: {def.Key} の画像 {kind}.png を使います ({tex.width}x{tex.height}" + (baseSprite != null ? $", 土台 rect={baseSprite.rect} textureRect={baseSprite.textureRect} ppu={baseSprite.pixelsPerUnit} border={baseSprite.border})" : ")"));
            return result;
        }
        catch (Exception e)
        {
            CharacterMod.Ctx?.Log.Warning($"Character: {file} を読めません: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 土台の絵は、周りの透明なところを切り詰めて保存されていることがある (表示の枠 rect > 中身 textureRect)。
    /// お手本 (と差し替えの絵) は中身の部分なので、枠の大きさに広げて、中身のあった位置に置く (そのままだと枠いっぱいに伸びる)
    /// </summary>
    private static Texture2D Pad(Texture2D tex, Sprite baseSprite)
    {
        var r = baseSprite.rect;
        var tr = baseSprite.textureRect;
        if (Mathf.Approximately(r.width, tr.width) && Mathf.Approximately(r.height, tr.height)) return tex;
        float scale = tex.width / Mathf.Max(1f, tr.width);
        int w = Mathf.RoundToInt(r.width * scale), h = Mathf.RoundToInt(r.height * scale);
        var off = baseSprite.textureRectOffset * scale;
        int ox = Mathf.Clamp(Mathf.RoundToInt(off.x), 0, Mathf.Max(0, w - tex.width));
        int oy = Mathf.Clamp(Mathf.RoundToInt(off.y), 0, Mathf.Max(0, h - tex.height));
        var padded = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = tex.name, hideFlags = HideFlags.DontUnloadUnusedAsset };
        padded.SetPixels32(new Color32[w * h]);
        padded.SetPixels(ox, oy, Mathf.Min(tex.width, w), Mathf.Min(tex.height, h), tex.GetPixels(0, 0, Mathf.Min(tex.width, w), Mathf.Min(tex.height, h)));
        padded.Apply();
        CharacterMod.Ctx?.Log.Info($"Character: {tex.name} は土台の枠 {r.width}x{r.height} (中身 {tr.width}x{tr.height} が {baseSprite.textureRectOffset}) に合わせて広げました");
        Object.Destroy(tex);
        return padded;
    }

    /// <summary>土台の絵をお手本として書き出す (まだ書き出していなければ)</summary>
    public static void ExportTemplate(CharacterDef def, string kind, Object baseAsset)
    {
        if (baseAsset == null || !Exported.Add(def.Id + "/" + kind)) return;
        try
        {
            var dir = Path.Combine(def.Folder, "images_template");
            var file = Path.Combine(dir, kind + ".png");
            if (File.Exists(file)) return;
            byte[] png = null;
            var sprite = baseAsset.TryCast<Sprite>();
            if (sprite != null) png = Png(sprite.texture, sprite.textureRect);
            else if (baseAsset.TryCast<Texture2D>() is Texture2D tex) png = Png(tex, new Rect(0, 0, tex.width, tex.height));
            if (png == null) return;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(file, png);
        }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: お手本の {kind} を書き出せません: {e.Message}"); }
    }

    /// <summary>テクスチャの一部を PNG にする (読み取り禁止のテクスチャも、描き写して読む)</summary>
    private static byte[] Png(Texture tex, Rect rect)
    {
        if (tex == null) return null;
        var prev = RenderTexture.active;
        RenderTexture rt = null;
        Texture2D copy = null;
        try
        {
            rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            int w = Mathf.Max(1, Mathf.RoundToInt(rect.width)), h = Mathf.Max(1, Mathf.RoundToInt(rect.height));
            copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            copy.Apply();
            return ImageConversion.EncodeToPNG(copy);
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (copy != null) Object.Destroy(copy);
        }
    }
}
