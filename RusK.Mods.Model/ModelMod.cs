using System;
using System.IO;
using System.Linq;
using System.Text;
using RusK.API;
using RusK.Mods.Shared;
using UnityEngine;
using Module = RusK.API.Module;

namespace RusK.Mods.Model;

/// <summary>
/// Custom Model: キャラの見た目を VRM にする。RusK\models に置いた .vrm を、キャラごとに選べる。
/// ゲームのキャラ (骨格・アニメーション・当たり判定) はそのまま動かし、見た目だけを VRM にする。
/// Model Lab はデバッグ用 (モデルの作りの書き出し・キャラ同士の見た目の入れ替え・切り抜きの方式の比較)。
/// </summary>
[RuskMod("model", "Custom Model", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1872",
    Description = "キャラの見た目を VRM にする (RusK\\models に .vrm を置く)")]
public sealed class ModelMod : RuskMod
{
    protected override void OnLoad()
    {
        ModelLab.Ctx = Context;
        Vrm.VrmEnv.Ctx = Context;
        Vrm.VrmSwap.LoadAssignments();
        Context.Harmony.PatchAll(typeof(Vrm.VrmLatePatch));
        var main = new CustomModelWindow();
        Context.RegisterWindow(main);
        Context.RegisterModule(new CustomModelModule(main));

        var window = new ModelLabWindow();
        Context.RegisterWindow(window);
        Context.RegisterModule(new ModelLabModule(window));
        Context.RegisterModule(new ModelRuntimeModule());
        Context.RegisterAction("ModelReport", () => ModelLab.Report(), "今のキャラのモデルを調べて書き出す");
    }

    protected override void OnUnload() => Vrm.VrmSwap.RestoreAll();
}

public sealed class ModelLabModule : Module
{
    private readonly ModelLabWindow _window;

    public ModelLabModule(ModelLabWindow window) : base("ModelLab", "Model", "デバッグ用: モデルの作りの書き出し・見た目の入れ替え・切り抜きの方式の比較")
    {
        _window = window;
        _window.VisibleChanged += w => Enabled = w.Visible;
    }

    public override bool VisibleInArrayList => false;
    public override void OnEnable() => _window.Visible = true;
    public override void OnDisable() => _window.Visible = false;
}

/// <summary>入れ替えた見た目の材質を、元の体から毎フレーム同期する (常に ON)</summary>
public sealed class ModelRuntimeModule : Module
{
    public ModelRuntimeModule() : base("ModelRuntime", "Model", "入れ替えた見た目の更新 (常に ON にしておいてください)")
    {
        Enabled = true;
    }

    public override bool VisibleInArrayList => false;
    public override void OnUpdate() => ModelSwap.Tick();
}

public sealed class ModelLabWindow : RuskWindow
{
    private static void OpenFolder(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception e) { ModelLab.Ctx?.Log.Warning($"フォルダを開けません: {e.Message}"); }
    }

    public ModelLabWindow() : base("lab", "Model Lab (デバッグ)", 440f, 520f) { }

    public override void Draw(WindowGui gui)
    {
        var p = PlayerRef.Current;
        gui.Label("今のキャラのモデルの作りを調べて、RusK\\data\\model に書き出します。",
            RuskStyle.TextDim, small: true);
        gui.Header("今のキャラ", p != null ? CharacterNames.Get(p.GetPlayerId()) : "(なし)");
        if (gui.Button("調べて書き出す", enabled: p != null, accent: true)) ModelLab.Report();
        if (ModelLab.LastFile != null)
            gui.Label($"書き出し: {Path.GetFileName(ModelLab.LastFile)}", RuskStyle.TextDim, small: true);

        // D-2 / D-3: VRM
        gui.Space(6f);
        var assigned = Vrm.VrmSwap.AssignedFile(p);
        gui.Header("VRM (試験)", assigned != null ? $"このキャラ: {Path.GetFileNameWithoutExtension(assigned)}" : null);
        gui.Label("RusK\\models に置いた .vrm を、今のキャラの見た目にします。", RuskStyle.TextDim, small: true);
        gui.BeginRow(1f, 1f);
        if (gui.Button("元に戻す", enabled: assigned != null)) Vrm.VrmSwap.Unassign(p);
        if (gui.Button("フォルダを開く")) OpenFolder(Vrm.VrmSwap.ModelsDir);

        // 切り抜きの方式 (ゲームのシェーダーで VRM の透明部分をどう扱うか、比べるための試験)
        int mode = Vrm.MaskModes.Current;
        int step = gui.Stepper("切り抜き", Vrm.MaskModes.Names[mode] + (Vrm.MaskModes.Available(mode) ? "" : " (使えません)"));
        if (step != 0)
        {
            mode = (mode + step + Vrm.MaskModes.Names.Length) % Vrm.MaskModes.Names.Length;
            Vrm.MaskModes.SetAll(mode, Vrm.VrmSwap.Models());
        }
        var files = Vrm.VrmSwap.Files().ToList();
        if (files.Count == 0) gui.Label("(.vrm がありません)", RuskStyle.TextDim, small: true);
        foreach (var f in files)
        {
            bool mine = assigned != null && Path.GetFileName(assigned) == Path.GetFileName(f);
            if (gui.Selectable(Path.GetFileNameWithoutExtension(f), mine, $"{new FileInfo(f).Length / 1024 / 1024} MB") && p != null)
                Vrm.VrmSwap.Assign(p, f);
        }

        SkirtUi.Draw(gui);
        PassUi.Draw(gui);

        // D-1: キャラ同士の見た目の入れ替え (置き換えの仕組みの確認)
        gui.Space(6f);
        gui.Header("見た目の入れ替え (試験)", p != null && ModelSwap.IsSwapped(p) ? "入れ替え中" : null);
        gui.Label("動きは今のキャラのまま、体の見た目だけを選んだキャラにします。", RuskStyle.TextDim, small: true);
        if (gui.Button("元に戻す", enabled: p != null && ModelSwap.IsSwapped(p))) ModelSwap.Restore(p);
        foreach (var c in ModelSwap.Characters())
        {
            if (gui.Selectable(CharacterNames.Get(c), false, c.prefabPath.Split('/').Last()) && p != null)
                ModelSwap.Apply(p, c);
        }
    }
}

internal static class ModelLab
{
    public static IModContext Ctx;
    public static string LastFile;

    public static void Report()
    {
        var p = PlayerRef.Current;
        if (p == null) { Ctx?.Notify("プレイヤーがいません", NotifyLevel.Warning); return; }

        var sb = new StringBuilder();
        string name = "?";
        try { name = CharacterNames.Get(p.GetPlayerId()); } catch { }
        sb.AppendLine($"キャラ: {name} (id {p.GetPlayerId():0}) オブジェクト '{p.gameObject.name}'");
        sb.AppendLine($"Unity {Application.unityVersion} / 色空間 {QualitySettings.activeColorSpace}");
        sb.AppendLine();

        Section(sb, "Animator", () => Animators(sb, p));
        Section(sb, "今のアニメーション", () => CurrentClip(sb, p));
        Section(sb, "SkinnedMeshRenderer (体のメッシュ)", () => Skinned(sb, p));
        Section(sb, "その他の Renderer (武器など)", () => OtherRenderers(sb, p));
        Section(sb, "武器・装備品 (置き換え Mod の下調べ)", () => Props(sb, p));
        Section(sb, "シーン内の武器 (WeaponController)", () => Weapons(sb));
        Section(sb, "骨格の階層 (上から 4 段)", () => Hierarchy(sb, p));
        Section(sb, "切り抜きを使うゲームの材質 (ToonLit)", () => ClipMaterials(sb));
        Section(sb, "VRM の切り抜きの材質 (比べる用)", () => VrmClipMaterials(sb));

        try
        {
            Directory.CreateDirectory(Ctx.DataDirectory);
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            LastFile = Path.Combine(Ctx.DataDirectory, $"report_{p.GetPlayerId():0}_{safe}.txt");
            File.WriteAllText(LastFile, sb.ToString(), new UTF8Encoding(false));
            Ctx.Notify($"{name} のモデルを書き出しました", NotifyLevel.Success);
            Ctx.Log.Info($"Model Lab: {LastFile}");
        }
        catch (Exception e)
        {
            Ctx.Log.Error($"Model Lab: 書き出しに失敗: {e}");
        }
    }

    private static readonly string[] ClipProps =
    {
        "_UseAlphaClipping", "_Cutoff", "_ClipInterVal", "_AlphaMap", "_UseCharacterAlphaClipMap", "_CharacterAlphaClipMap",
        "_CharacterAlphaClipCutoff", "_UseMainTexAlpha", "_MainTex", "_RenderType", "_GroupTransparent", "_Cull", "_Ref",
        "_SrcBlend", "_DstBlend", "_ZWrite", "_IsFace",
    };

    private static string Describe(Material m)
    {
        var sb = new StringBuilder();
        sb.Append($"'{m.name}' シェーダー '{m.shader?.name}' 描画順 {m.renderQueue} キーワード [{string.Join(" ", m.shaderKeywords)}]");
        foreach (var prop in ClipProps)
        {
            if (!m.HasProperty(prop)) continue;
            try
            {
                var tex = m.GetTexture(prop);
                if (tex != null) { sb.Append($" {prop}={tex.name}({tex.width}x{tex.height})"); continue; }
            }
            catch { }
            try { sb.Append($" {prop}={m.GetFloat(prop):0.###}"); } catch { }
        }
        try
        {
            var st = m.GetTextureScale("_AlphaMap");
            var of = m.GetTextureOffset("_AlphaMap");
            sb.Append($" _AlphaMap_ST=({st.x:0.##},{st.y:0.##},{of.x:0.##},{of.y:0.##})");
        }
        catch { }
        return sb.ToString();
    }

    /// <summary>ゲームの ToonLit の材質のうち、切り抜き・透明を使っているもの (VRM の切り抜きの真似をするための下調べ)</summary>
    private static void ClipMaterials(StringBuilder sb)
    {
        var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Material>());
        int toon = 0, shown = 0;
        var keywordSets = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var o in all)
        {
            var m = o.TryCast<Material>();
            if (m == null || m.shader == null || !m.shader.name.Contains("ToonLit")) continue;
            if (m.name.StartsWith("RusK") || m.name.StartsWith("N00_") || m.name.Contains("(Instance)") && m.name.StartsWith("J_")) continue;
            toon++;
            var kw = string.Join(" ", m.shaderKeywords.OrderBy(k => k));
            keywordSets[kw] = keywordSets.TryGetValue(kw, out var c) ? c + 1 : 1;
            bool clip = false;
            foreach (var prop in new[] { "_UseAlphaClipping", "_UseCharacterAlphaClipMap", "_UseMainTexAlpha" })
                if (m.HasProperty(prop) && m.GetFloat(prop) > 0f) clip = true;
            if (m.shaderKeywords.Any(k => k.Contains("CLIP") || k.Contains("ALPHA"))) clip = true;
            if (m.renderQueue >= 2450 && m.renderQueue < 2500) clip = true;
            if (!clip || shown >= 40) continue;
            shown++;
            sb.AppendLine(Describe(m));
        }
        sb.AppendLine($"ToonLit の材質 {toon} 個、切り抜きなどを使うもの {shown} 個 (40 個まで表示)");
        sb.AppendLine("キーワードの組み合わせ:");
        foreach (var kv in keywordSets.OrderByDescending(k => k.Value)) sb.AppendLine($"  {kv.Value} 個: [{kv.Key}]");
    }

    private static void VrmClipMaterials(StringBuilder sb)
    {
        foreach (var model in Vrm.VrmSwap.Models())
            foreach (var mm in model.Masks)
                if (mm.Material != null)
                    sb.AppendLine((mm.Solid ? "[透明なし] " : "") + Describe(mm.Material));
    }

    private static void Section(StringBuilder sb, string title, Action body)
    {
        sb.AppendLine($"===== {title} =====");
        try { body(); }
        catch (Exception e) { sb.AppendLine($"(エラー: {e.Message})"); }
        sb.AppendLine();
    }

    private static void Animators(StringBuilder sb, PlayerController p)
    {
        foreach (var a in p.GetComponentsInChildren<Animator>(true))
        {
            var av = a.avatar;
            sb.AppendLine($"'{PathOf(a.transform, p.transform)}' enabled={a.enabled} avatar='{av?.name}' " +
                          $"人型={av != null && av.isHuman} 有効={av != null && av.isValid} " +
                          $"rootMotion={a.applyRootMotion} controller='{a.runtimeAnimatorController?.name}' " +
                          $"cullingMode={a.cullingMode}");
            if (av == null || !av.isHuman) continue;

            sb.AppendLine("  人型の骨 (HumanBodyBones → ゲームの骨の名前):");
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = (HumanBodyBones)i;
                Transform t = null;
                try { t = a.GetBoneTransform(bone); } catch { }
                if (t != null) sb.AppendLine($"    {bone,-24} {t.name}");
            }
        }
    }

    private static void CurrentClip(StringBuilder sb, PlayerController p)
    {
        var state = p.GetAnimController()?.m_curState;
        var clip = state?.Clip;
        if (clip == null) { sb.AppendLine("(再生中のクリップなし)"); return; }
        sb.AppendLine($"クリップ '{clip.name}' 長さ {clip.length:0.00}s 人型アニメ={clip.humanMotion} " +
                      $"legacy={clip.legacy} fps={clip.frameRate}");
    }

    private static void Skinned(StringBuilder sb, PlayerController p)
    {
        foreach (var r in p.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = r.sharedMesh;
            sb.AppendLine($"'{PathOf(r.transform, p.transform)}' 表示={r.enabled && r.gameObject.activeInHierarchy} " +
                          $"mesh='{mesh?.name}' 頂点={mesh?.vertexCount} サブメッシュ={mesh?.subMeshCount} " +
                          $"骨={r.bones?.Length} rootBone='{r.rootBone?.name}' blendShape={mesh?.blendShapeCount} " +
                          $"読み取り可={mesh != null && mesh.isReadable}");
            Materials(sb, r);
        }
    }

    private static void OtherRenderers(StringBuilder sb, PlayerController p)
    {
        foreach (var r in p.GetComponentsInChildren<Renderer>(true))
        {
            if (r.TryCast<SkinnedMeshRenderer>() != null) continue;
            sb.AppendLine($"'{PathOf(r.transform, p.transform)}' {r.GetIl2CppType().Name} 表示={r.enabled && r.gameObject.activeInHierarchy}");
            Materials(sb, r);
        }
    }

    private static void Materials(StringBuilder sb, Renderer r)
    {
        var mats = r.sharedMaterials;
        if (mats == null) return;
        foreach (var m in mats)
        {
            if (m == null) { sb.AppendLine("    材質: (なし)"); continue; }
            var sh = m.shader;
            sb.AppendLine($"    材質 '{m.name}' シェーダー '{sh?.name}' テクスチャ '{m.mainTexture?.name}' renderQueue={m.renderQueue}");
        }
    }

    private static void Hierarchy(StringBuilder sb, PlayerController p) => Walk(sb, p.transform, 0, 4);

    /// <summary>シーン全体の武器 (キャラの外に置かれている武器も含めて)</summary>
    private static void Weapons(StringBuilder sb)
    {
        foreach (var w in Resources.FindObjectsOfTypeAll<WeaponController>())
        {
            if (w == null || w.gameObject.scene.name == null) continue; // プレハブの元データは除く
            var t = w.transform;
            string path = t.name;
            for (var c = t.parent; c != null; c = c.parent) path = c.name + "/" + path;
            string equip = "?";
            try
            {
                var es = w.GetEquipSetting();
                equip = es == null ? "(なし)" : $"'{es.name}' id {es.equipId} 種類 {es.equipType} 枠 {es.holderIndex} プレハブ '{es.equipPrefabPath}'";
            }
            catch { }
            sb.AppendLine($"'{path}' 表示={w.gameObject.activeInHierarchy} 装備 {equip}");
            sb.AppendLine($"    位置 {t.localPosition} 回転 {t.localEulerAngles} 大きさ {t.localScale}");
            foreach (var r in w.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetIl2CppType().Name == "ParticleSystemRenderer") continue;
                var smr = r.TryCast<SkinnedMeshRenderer>();
                var mf = r.GetComponent<MeshFilter>();
                string mesh = smr != null ? $"skinned '{smr.sharedMesh?.name}' 頂点 {smr.sharedMesh?.vertexCount} 骨 {smr.bones?.Length}"
                    : $"mesh '{mf?.sharedMesh?.name}' 頂点 {mf?.sharedMesh?.vertexCount}";
                sb.AppendLine($"    描画 '{PathOf(r.transform, t)}' {r.GetIl2CppType().Name} {mesh} 表示={r.enabled && r.gameObject.activeInHierarchy}");
                Materials(sb, r);
            }
        }
    }

    /// <summary>武器 (手の骨の BN_Weapon の下) と装備品 (WeaponHolder_1～4 の下) の作り</summary>
    private static void Props(StringBuilder sb, PlayerController p)
    {
        foreach (var t in p.GetComponentsInChildren<Transform>(true))
        {
            bool weapon = t.name == "BN_Weapon";
            bool holder = t.name.StartsWith("WeaponHolder");
            if (!weapon && !holder) continue;

            sb.AppendLine($"[{(weapon ? "武器" : "装備品")}] '{PathOf(t, p.transform)}' (親の骨 '{t.parent?.name}')");
            var comps = t.GetComponents<Component>().Select(c => c?.GetIl2CppType().Name).Where(n => n != null && n != "Transform");
            sb.AppendLine($"    部品 [{string.Join(", ", comps)}]  位置 {t.localPosition} 回転 {t.localEulerAngles} 大きさ {t.localScale}");
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                var cc = c.GetComponents<Component>().Select(x => x?.GetIl2CppType().Name).Where(n => n != null && n != "Transform");
                sb.AppendLine($"    子 '{c.name}' 表示={c.gameObject.activeInHierarchy} [{string.Join(", ", cc)}] " +
                              $"位置 {c.localPosition} 回転 {c.localEulerAngles} 大きさ {c.localScale}");
            }
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetIl2CppType().Name == "ParticleSystemRenderer") continue; // エフェクトは除く
                string mesh = "?";
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr != null) mesh = $"skinned '{smr.sharedMesh?.name}' 頂点 {smr.sharedMesh?.vertexCount} 骨 {smr.bones?.Length}";
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    mesh = $"mesh '{mf?.sharedMesh?.name}' 頂点 {mf?.sharedMesh?.vertexCount}";
                }
                sb.AppendLine($"    描画 '{PathOf(r.transform, t)}' {r.GetIl2CppType().Name} {mesh} 表示={r.enabled && r.gameObject.activeInHierarchy}");
                Materials(sb, r);
            }
        }
    }

    private static void Walk(StringBuilder sb, Transform t, int depth, int max)
    {
        var comps = t.GetComponents<Component>().Select(c => c?.GetIl2CppType().Name)
            .Where(n => n != null && n != "Transform").ToArray();
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(comps.Length > 0 ? "  [" + string.Join(", ", comps) + "]" : "")}");
        if (depth >= max) { if (t.childCount > 0) sb.AppendLine($"{new string(' ', depth * 2 + 2)}… 子 {t.childCount} 個"); return; }
        for (int i = 0; i < t.childCount; i++) Walk(sb, t.GetChild(i), depth + 1, max);
    }

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
        parts.Reverse();
        return string.Join("/", parts);
    }
}

/// <summary>スカートと脚のバランスの調整 (Custom Model と Model Lab の両方の画面に出す)</summary>
/// <summary>
/// 調査用: VRM の材質 (ゲームのシェーダー) のパスを 1 つずつ止めて、どのパスが何を描いているか確かめる
/// </summary>
internal static class PassUi
{
    private static readonly System.Collections.Generic.HashSet<string> Disabled = new();

    public static void Draw(WindowGui gui)
    {
        var mats = Vrm.VrmSwap.Models().SelectMany(m => m.Materials).Where(m => m != null && m.shader != null
            && m.shader.name.Contains("ToonLit")).ToList();
        if (mats.Count == 0) return;
        gui.Space(6f);
        gui.Header("シェーダーのパス (試験)", mats[0].shader.name);
        gui.Label("押すとそのパスを止めます (もう一度押すと戻る)。VRM の見た目の変化で、何を描くパスか調べます。", RuskStyle.TextDim, small: true);
        var shader = mats[0].shader;
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < mats[0].passCount; i++)
        {
            string name = mats[0].GetPassName(i);
            string mode = "";
            try { mode = shader.FindPassTagValue(i, new UnityEngine.Rendering.ShaderTagId("LightMode")).name; } catch { }
            string key = string.IsNullOrEmpty(mode) ? name : mode;
            if (!seen.Add(key)) continue;
            bool off = Disabled.Contains(key);
            if (gui.Button($"{(off ? "[停止中] " : "")}{name} (LightMode={mode})"))
            {
                if (off) Disabled.Remove(key); else Disabled.Add(key);
                foreach (var m in mats) m.SetShaderPassEnabled(key, off);
                ModelLab.Ctx?.Log.Info($"Model Lab: パス '{name}' (LightMode={mode}) を{(off ? "戻しました" : "止めました")}");
            }
        }
    }
}

internal static class SkirtUi
{
    public static void Draw(WindowGui gui)
    {
        gui.Space(6f);
        gui.Header("明るさ・影");
        gui.Label(Vrm.MaskModes.IsUrp
            ? "明るさ: 服や髪が白く浮いて見えるときは下げる。影の濃さ: 髪や帽子が顔に落とす影 (1.00 でゲームのキャラと同じ)。"
            : "影の濃さ: 髪や帽子が顔などに落とす影 (1.00 でゲームのキャラと同じ)。", RuskStyle.TextDim, small: true);
        float b = Vrm.MaskModes.Brightness;
        if (Vrm.MaskModes.IsUrp && Tune(gui, "明るさ", ref b, 0.05f, 0.3f, 1.5f)) Vrm.MaskModes.SetBrightness(b, Vrm.VrmSwap.Models());
        float sh = Vrm.MaskModes.ShadowStrength;
        if (Tune(gui, "影の濃さ", ref sh, 0.1f, 0f, 1f)) Vrm.MaskModes.SetShadowStrength(sh, Vrm.VrmSwap.Models());
        if (gui.Button("明るさ・影を初期値に戻す"))
        {
            Vrm.MaskModes.SetBrightness(Vrm.MaskModes.DefaultBrightness, Vrm.VrmSwap.Models());
            Vrm.MaskModes.SetShadowStrength(Vrm.MaskModes.DefaultShadowStrength, Vrm.VrmSwap.Models());
        }

        gui.Space(6f);
        gui.Header("スカートの調整");
        gui.Label("脚がスカートから出るのと、スカートのめくれ具合のバランスを調整します。", RuskStyle.TextDim, small: true);
        bool changed = false;
        changed |= Tune(gui, "前後の開き", ref Vrm.SkirtTuning.FrontBack, 0.1f, 0f, 1f);
        changed |= Tune(gui, "横の開き", ref Vrm.SkirtTuning.Side, 0.1f, 0f, 1f);
        changed |= Tune(gui, "脚の振り", ref Vrm.SkirtTuning.LegSwing, 0.05f, 0.5f, 1f);
        changed |= Tune(gui, "当たり判定の太さ", ref Vrm.SkirtTuning.Collider, 0.05f, 0.5f, 2f);
        if (changed) Vrm.SkirtTuning.Save();
        if (gui.Button("初期値に戻す")) Vrm.SkirtTuning.Reset();
    }

    private static bool Tune(WindowGui gui, string label, ref float value, float step, float min, float max)
    {
        int s = gui.Stepper(label, value.ToString("0.00"));
        if (s == 0) return false;
        value = Mathf.Clamp(Mathf.Round((value + s * step) * 100f) / 100f, min, max);
        return true;
    }
}
