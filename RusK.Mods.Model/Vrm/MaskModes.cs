using System;
using System.Collections.Generic;
using UnityEngine;

namespace RusK.Mods.Model.Vrm;

/// <summary>透明部分を切り抜く材質 (VRM の alphaMode = MASK) の情報</summary>
internal sealed class MaskMaterial
{
    public Material Material;
    public Material Template;      // 元にしたゲームの材質
    public Texture2D Texture;      // 色のテクスチャ
    public Texture2D AlphaTexture; // 切り抜き用の白黒のテクスチャ (透明な所が白。白い所が消える)
    public Color Color;            // 線形の色
    public float Cutoff;
    public bool DoubleSided;
    /// <summary>
    /// 切り抜きの指定はあるが、メッシュが使う部分に透明な画素が無い (VRoid の顔の肌など)。
    /// 切り抜く必要がないので、ゲームのシェーダーのまま描く (ライトと色調がゲームのキャラと揃う)
    /// </summary>
    public bool Solid;

    // 読み込み中だけ使う: 色のテクスチャの透明度 (一番大きい画像、下の行から)
    internal byte[] AlphaBytes;
    internal int AlphaWidth, AlphaHeight;
    internal bool TransparentHit;
}

/// <summary>
/// 切り抜きの方式。ゲームのシェーダー (Custom/ToonLit_Crt) で VRM の透明部分をどう切り抜けるかがまだ分からないので、
/// Model Lab からゲーム中に切り替えて比べられるようにする。
/// </summary>
internal static class MaskModes
{
    public static readonly string[] Names =
    {
        "切り抜きなし",
        "_AlphaMap",
        "CharacterAlphaClipMap",
        "URP Unlit",
        "URP Simple Lit",
    };

    /// <summary>
    /// 今の方式。ゲームのシェーダーのキャラ用の切り抜き (CharacterAlphaClipMap) に決定: ゲームのキャラと同じ陰影・色調になり、
    /// URP Unlit で出ていた白っぽさ・首や脚の暗いしみ (SSAO) が無い
    /// </summary>
    public static int Current = 2;

    /// <summary>今の方式が URP のシェーダーか (明るさの調整はこのときだけ効く)</summary>
    public static bool IsUrp => UrpShaders[Current] != null;

    private static readonly string[] UrpShaders =
    {
        null, null, null,
        "Universal Render Pipeline/Unlit",
        "Universal Render Pipeline/Simple Lit",
    };

    /// <summary>
    /// URP の方式の明るさ (1 でテクスチャの色そのまま)。URP Unlit はライトの影響を受けないので、
    /// 明るいステージだと肌などが白く浮いて見える。VRM の見た目の画面から変えられる (data/model/brightness.txt)
    /// </summary>
    public static float Brightness = DefaultBrightness;
    public const float DefaultBrightness = 0.85f;

    /// <summary>ゲームのシェーダーで描く材質に落ちる影 (髪・帽子などの影) の濃さの倍率 (1 でゲームのキャラと同じ)</summary>
    public static float ShadowStrength = DefaultShadowStrength;
    public const float DefaultShadowStrength = 0.5f;
    private static string ShadowFile => System.IO.Path.Combine(VrmEnv.Ctx.DataDirectory, "shadow.txt");

    private static string ModeFile => System.IO.Path.Combine(VrmEnv.Ctx.DataDirectory, "mask_mode2.txt"); // 標準を変えたので、前の保存 (mask_mode.txt) は読まない
    private static string BrightnessFile => System.IO.Path.Combine(VrmEnv.Ctx.DataDirectory, "brightness.txt");

    public static void Load()
    {
        try
        {
            if (System.IO.File.Exists(ModeFile) && int.TryParse(System.IO.File.ReadAllText(ModeFile).Trim(), out var m)
                && m >= 0 && m < Names.Length)
                Current = m;
            if (System.IO.File.Exists(BrightnessFile)
                && float.TryParse(System.IO.File.ReadAllText(BrightnessFile).Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var b))
                Brightness = Mathf.Clamp(b, 0.3f, 1.5f);
            if (System.IO.File.Exists(ShadowFile)
                && float.TryParse(System.IO.File.ReadAllText(ShadowFile).Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var s))
                ShadowStrength = Mathf.Clamp01(s);
        }
        catch { }
    }

    /// <summary>影の濃さを変えて、読み込み済みの VRM にすぐ反映する</summary>
    public static void SetShadowStrength(float value, IEnumerable<VrmModel> models)
    {
        ShadowStrength = Mathf.Clamp01(value);
        try
        {
            System.IO.Directory.CreateDirectory(VrmEnv.Ctx.DataDirectory);
            System.IO.File.WriteAllText(ShadowFile, ShadowStrength.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
        foreach (var model in models)
        {
            if (model.ReceiveShadowBase < 0f) continue;
            foreach (var mat in model.Materials)
                if (mat != null) SetFloat(mat, "_ReceiveShadowMappingAmount", model.ReceiveShadowBase * ShadowStrength);
        }
    }

    /// <summary>明るさを変えて、読み込み済みの VRM にすぐ反映する</summary>
    public static void SetBrightness(float value, IEnumerable<VrmModel> models)
    {
        Brightness = Mathf.Clamp(value, 0.3f, 1.5f);
        try
        {
            System.IO.Directory.CreateDirectory(VrmEnv.Ctx.DataDirectory);
            System.IO.File.WriteAllText(BrightnessFile, Brightness.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
        foreach (var model in models)
            foreach (var m in model.Masks)
                if (UrpShaders[Current] != null && m.Material != null && !m.Solid)
                    SetColor(m.Material, "_BaseColor", Tint(m.Color));
    }

    /// <summary>材質の色 (線形) に明るさを掛けて、材質に入れるガンマの色にする。透明度はそのまま</summary>
    private static Color Tint(Color linear)
    {
        var g = linear.gamma;
        return new Color(g.r * Brightness, g.g * Brightness, g.b * Brightness, g.a);
    }

    private static void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(VrmEnv.Ctx.DataDirectory);
            System.IO.File.WriteAllText(ModeFile, Current.ToString());
        }
        catch { }
    }
    private static readonly Dictionary<string, Shader> Found = new();
    private static readonly Dictionary<string, Material> KeepAlive = new();

    /// <summary>その方式が使えるか (URP のシェーダーがゲームに入っているか)</summary>
    public static bool Available(int mode) => UrpShaders[mode] == null || FindShader(UrpShaders[mode]) != null;

    /// <summary>
    /// シェーダーを名前で探す。見つけたものは覚えておくが、ステージの移動でゲームが使っていない素材を片付けると
    /// 覚えていたシェーダーの実体が消えることがある (Unity の == null で null になる) ので、そのときは探し直す
    /// </summary>
    internal static Shader FindShader(string name)
    {
        if (Found.TryGetValue(name, out var s) && s != null) return s;
        bool first = !Found.ContainsKey(name);
        s = null;
        try { s = Shader.Find(name); } catch { }
        Found[name] = s;
        // このシェーダーを使う材質を 1 つ、片付けの対象から外して持っておく (シェーダーがメモリから消えないように)
        if (s != null && (!KeepAlive.TryGetValue(name, out var keep) || keep == null))
        {
            try
            {
                keep = new Material(s) { name = "RusK_KeepAlive_" + name, hideFlags = HideFlags.HideAndDontSave };
                KeepAlive[name] = keep;
            }
            catch { }
        }
        if (first || s == null)
            VrmEnv.Ctx?.Log.Info($"Model: シェーダー '{name}' は{(s != null ? "使えます" : "見つかりません")}");
        return s;
    }

    public static void Apply(MaskMaterial m, int mode)
    {
        var mat = m.Material;
        if (mat == null) return;
        try
        {
            if (UrpShaders[mode] != null)
            {
                var shader = FindShader(UrpShaders[mode]);
                if (shader == null) mode = 0;
                else
                {
                    ApplyUrp(m, shader);
                    return;
                }
            }

            // ゲームのシェーダーに戻して、元の材質の設定をまるごと写してから、テクスチャと色を入れ直す
            mat.shader = m.Template.shader;
            mat.CopyPropertiesFromMaterial(m.Template);
            mat.shaderKeywords = m.Template.shaderKeywords;
            mat.renderQueue = m.Template.renderQueue;
            VrmLoader.RemoveTemplateMaps(mat);
            VrmLoader.SetEmission(mat, Color.black, null);
            if (m.Template.HasProperty("_ReceiveShadowMappingAmount"))
                SetFloat(mat, "_ReceiveShadowMappingAmount", m.Template.GetFloat("_ReceiveShadowMappingAmount") * ShadowStrength);
            Set(mat, "_MainTex", m.Texture);
            SetColor(mat, "_BaseColor", m.Color.gamma);
            SetFloat(mat, "_GroupOutline", 0f);
            SetFloat(mat, "_Cull", 2f); // 両面表示は、裏向きの面をメッシュに足してある

            switch (mode)
            {
                case 1: // ゲームのシェーダーの切り抜き (_AlphaMap の値で切り抜く)
                    Set(mat, "_AlphaMap", m.AlphaTexture);
                    SetFloat(mat, "_UseAlphaClipping", 1f);
                    SetFloat(mat, "_Cutoff", m.Cutoff);
                    mat.EnableKeyword("_USEALPHACLIPPING_ON");
                    break;
                case 2: // キャラ用の切り抜き (_CharacterAlphaClipMap)
                    // ゲームのキャラ (PinkLight_Body_Story) と同じ設定: 切り抜きのキーワード _USEALPHACLIPPING_ON の中で、
                    // _UseCharacterAlphaClipMap = 1 なら _CharacterAlphaClipMap で切り抜く。
                    // _AlphaMap はカメラが近いときにフェードさせる縞模様 (DitherStripes) 用なので空にしておく
                    Set(mat, "_CharacterAlphaClipMap", m.AlphaTexture);
                    SetFloat(mat, "_UseCharacterAlphaClipMap", 1f);
                    SetFloat(mat, "_CharacterAlphaClipCutoff", 1f - m.Cutoff); // 反転したテクスチャなので閾値も反転
                    SetFloat(mat, "_UseAlphaClipping", 0f);
                    SetFloat(mat, "_Cutoff", 0.5f);
                    SetFloat(mat, "_ClipInterVal", 1f);
                    if (mat.HasProperty("_AlphaMap")) mat.SetTexture("_AlphaMap", null);
                    mat.EnableKeyword("_USEALPHACLIPPING_ON");
                    mat.EnableKeyword("_UseAlphaClipping");
                    break;
            }
        }
        catch (Exception e)
        {
            VrmEnv.Ctx?.Log.Warning($"Model: 切り抜きの方式を変えられません ({mat.name}): {e.Message}");
        }
    }

    /// <summary>Unity 標準 (URP) のシェーダーで描く。切り抜きは _AlphaClip と _ALPHATEST_ON</summary>
    private static void ApplyUrp(MaskMaterial m, Shader shader)
    {
        var mat = m.Material;
        mat.shader = shader;
        mat.shaderKeywords = Array.Empty<string>();
        Set(mat, "_BaseMap", m.Texture);
        Set(mat, "_MainTex", m.Texture);
        SetColor(mat, "_BaseColor", Tint(m.Color));
        SetFloat(mat, "_AlphaClip", 1f);
        SetFloat(mat, "_Cutoff", m.Cutoff);
        SetFloat(mat, "_Surface", 0f);
        SetFloat(mat, "_Cull", 2f); // 両面表示は、裏向きの面をメッシュに足してある
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.renderQueue = 2450; // AlphaTest
    }

    /// <summary>読み込み済みのすべての VRM の切り抜き材質を、指定の方式にする</summary>
    public static void SetAll(int mode, IEnumerable<VrmModel> models)
    {
        Current = mode;
        Save();
        foreach (var model in models)
            foreach (var m in model.Masks)
                if (!m.Solid) Apply(m, mode);
        VrmEnv.Ctx?.Log.Info($"Model: 切り抜きの方式 → {Names[mode]}");
    }

    private static void Set(Material m, string prop, Texture tex)
    {
        if (tex != null && m.HasProperty(prop)) m.SetTexture(prop, tex);
    }

    private static void SetColor(Material m, string prop, Color c)
    {
        if (m.HasProperty(prop)) m.SetColor(prop, c);
    }

    private static void SetFloat(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }
}
