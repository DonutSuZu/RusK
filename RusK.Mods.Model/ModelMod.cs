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
[RuskMod("model", "Custom VRM Loader", "1.2.6",
    Author = "you",
    GameVersion = "0.0.1873",
    Description = "キャラの見た目を VRM にする (RusK\\models に .vrm を置く)")]
public sealed class ModelMod : RuskMod
{
    protected override void OnLoad()
    {
        ModelLab.Ctx = Context;
        Vrm.VrmEnv.Ctx = Context;
        Vrm.VrmSwap.LoadAssignments();
        Context.Harmony.PatchAll(typeof(Vrm.VrmLatePatch));
        Context.Harmony.PatchAll(typeof(Vrm.VrmShowPatch));
        Context.Harmony.PatchAll(typeof(Vrm.VrmBrainPatch));
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
        if (gui.Button("画面のキャラを調べる (タイトル・キャラ画面など)")) ModelLab.ScanScene();
        MotionRecorder.Tick();
        if (gui.Button(MotionRecorder.Recording ? "記録中... (押すと止めて書き出す)" : "キャラの動きと表情を 10 秒記録する"))
        {
            if (MotionRecorder.Recording) MotionRecorder.Stop();
            else MotionRecorder.Start();
        }
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

    /// <summary>
    /// プレイヤー以外のキャラ (タイトル画面・キャラクター画面・装備画面の見せるためのモデル) を調べて書き出す。
    /// Custom Model をこれらにも付けるための下調べ
    /// </summary>
    public static void ScanScene()
    {
        var sb = new StringBuilder();
        string scenes = "";
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            scenes += UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name + " ";
        sb.AppendLine($"シーン: {scenes}/ プレイヤー: {(PlayerRef.Current != null ? PlayerRef.Current.gameObject.name : "なし")}");
        sb.AppendLine();

        Section(sb, "骨格 (Bip001) を持つ Animator", () =>
        {
            foreach (var a in Resources.FindObjectsOfTypeAll<Animator>())
            {
                if (a == null || a.gameObject.scene.name == null) continue;
                var bip = FindChild(a.transform, "Bip001", 3);
                if (bip == null) continue;
                var t = a.transform;
                string path = t.name;
                for (var c = t.parent; c != null; c = c.parent) path = c.name + "/" + path;
                bool player = a.GetComponentInParent<PlayerController>(true) != null;
                string comps = string.Join(", ", a.GetComponents<Component>().Select(c => c.GetIl2CppType().Name));
                string parentComps = t.parent != null
                    ? string.Join(", ", t.parent.GetComponents<Component>().Select(c => c.GetIl2CppType().Name)) : "-";
                sb.AppendLine($"'{path}' 表示={a.gameObject.activeInHierarchy} レイヤー={LayerMask.LayerToName(a.gameObject.layer)}({a.gameObject.layer}) " +
                              $"シーン='{a.gameObject.scene.name}' プレイヤー={player} avatar='{a.avatar?.name}' " +
                              $"controller='{a.runtimeAnimatorController?.name}'");
                sb.AppendLine($"    部品: {comps}");
                sb.AppendLine($"    親の部品: {parentComps}");
                foreach (var r in a.GetComponentsInChildren<SkinnedMeshRenderer>(true).Take(6))
                    sb.AppendLine($"    メッシュ '{r.name}' 表示={r.enabled && r.gameObject.activeInHierarchy} 材質=[" +
                                  string.Join(", ", r.sharedMaterials.Where(m => m != null).Select(m => $"{m.name}<{m.shader?.name}>")) + "]");
            }
        });

        Section(sb, "カメラ", () =>
        {
            foreach (var c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (c == null || c.gameObject.scene.name == null) continue;
                var layers = Enumerable.Range(0, 32).Where(l => (c.cullingMask & (1 << l)) != 0)
                    .Select(l => LayerMask.LayerToName(l)).Where(n => !string.IsNullOrEmpty(n));
                sb.AppendLine($"'{c.name}' 有効={c.enabled && c.gameObject.activeInHierarchy} 深さ={c.depth} " +
                              $"描き先={(c.targetTexture != null ? c.targetTexture.name : "画面")} 映すレイヤー=[{string.Join(" ", layers)}]");
            }
        });

        try
        {
            Directory.CreateDirectory(Ctx.DataDirectory);
            LastFile = Path.Combine(Ctx.DataDirectory, $"scene_{DateTime.Now:HHmmss}.txt");
            File.WriteAllText(LastFile, sb.ToString(), new UTF8Encoding(false));
            Ctx.Notify("画面のキャラを書き出しました", NotifyLevel.Success);
            Ctx.Log.Info($"Model Lab: {LastFile}");
        }
        catch (Exception e) { Ctx.Log.Error($"Model Lab: 書き出しに失敗: {e}"); }
    }

    private static Transform FindChild(Transform t, string name, int depth)
    {
        if (t.name == name) return t;
        if (depth <= 0) return null;
        for (int i = 0; i < t.childCount; i++)
        {
            var f = FindChild(t.GetChild(i), name, depth - 1);
            if (f != null) return f;
        }
        return null;
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

/// <summary>
/// 調査用: VRM を付けたキャラ (元のゲームのキャラ) の全部の骨の動きを 3 秒記録して、よく動く骨を書き出す。
/// 呼吸のような細かい動きが、どの骨の回転・位置・大きさで作られているか調べる
/// </summary>
internal static class MotionRecorder
{
    private sealed class Track
    {
        public Transform T;
        public string Path;
        public bool Mapped;
        public Quaternion Rot0;
        public Vector3 Pos0, Scale0;
        public float MaxAngle, MaxPos, MaxScale;
    }

    /// <summary>ブレンドシェイプ (顔の表情・口)。キャラを切り替えても「メッシュ名 / 名前」でまとめて集計する</summary>
    private sealed class Shape { public float Min = float.MaxValue, Max = float.MinValue; public int Samples; }

    private static System.Collections.Generic.List<Track> _tracks;       // VRM を付けたキャラの骨 (いれば)
    private static System.Collections.Generic.Dictionary<string, Shape> _shapes;
    private static System.Collections.Generic.List<Transform> _roots = new();
    private static Transform _root;
    private static float _end, _nextRoots;
    private static int _samples, _lastFrame = -1;

    public static bool Recording => _shapes != null;

    public static void Start()
    {
        _shapes = new System.Collections.Generic.Dictionary<string, Shape>();
        _tracks = null;
        _root = null;
        var target = Vrm.VrmSwap.RecordTarget();
        if (target != null)
        {
            var (root, mapped, _) = target.Value;
            _root = root;
            _tracks = new System.Collections.Generic.List<Track>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string path = t.name;
                for (var c = t.parent; c != null && c != root; c = c.parent) path = c.name + "/" + path;
                _tracks.Add(new Track
                {
                    T = t, Path = path, Mapped = mapped.Contains(t.Pointer),
                    Rot0 = t.localRotation, Pos0 = t.localPosition, Scale0 = t.localScale,
                });
            }
        }
        _end = Time.unscaledTime + 10f;
        _nextRoots = 0f;
        _samples = 0;
        ModelLab.Ctx?.Notify("キャラの動きと表情を 10 秒記録します", NotifyLevel.Info);
    }

    /// <summary>VrmSwap.LateTick から毎フレーム (アニメーションの後) 呼ばれる: VRM を付けたキャラの骨</summary>
    public static void Sample(Transform root)
    {
        if (_tracks == null || root == null || _root == null || root.Pointer != _root.Pointer) return;
        foreach (var k in _tracks)
        {
            if (k.T == null) continue;
            k.MaxAngle = Mathf.Max(k.MaxAngle, Quaternion.Angle(k.Rot0, k.T.localRotation));
            k.MaxPos = Mathf.Max(k.MaxPos, (k.T.localPosition - k.Pos0).magnitude);
            k.MaxScale = Mathf.Max(k.MaxScale, (k.T.localScale - k.Scale0).magnitude);
        }
    }

    /// <summary>
    /// Model Lab の画面から毎フレーム呼ばれる: 画面に出ているキャラ (見せるためのモデル・操作キャラ) の顔のブレンドシェイプ。
    /// VRM を付けていないキャラも記録する。時間が来たら書き出す
    /// </summary>
    public static void Tick()
    {
        if (_shapes == null || Time.frameCount == _lastFrame) return;
        _lastFrame = Time.frameCount;
        if (Time.unscaledTime >= _nextRoots)
        {
            _nextRoots = Time.unscaledTime + 0.5f;
            _roots.Clear();
            foreach (var s in Resources.FindObjectsOfTypeAll<CharacterShowController>())
                if (s != null && s.gameObject.scene.name != null && s.gameObject.activeInHierarchy) _roots.Add(s.transform);
            var p = PlayerRef.Current;
            if (p != null && p.gameObject.activeInHierarchy) _roots.Add(p.transform);
        }
        _samples++;
        foreach (var root in _roots)
        {
            if (root == null) continue;
            string who = root.name.Split('_')[0];
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = r.sharedMesh;
                if (mesh == null || mesh.blendShapeCount == 0) continue;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string key = $"{who} {r.name} / {mesh.GetBlendShapeName(i)}";
                    if (!_shapes.TryGetValue(key, out var sh)) _shapes[key] = sh = new Shape();
                    float w = r.GetBlendShapeWeight(i);
                    sh.Min = Mathf.Min(sh.Min, w);
                    sh.Max = Mathf.Max(sh.Max, w);
                    sh.Samples++;
                }
            }
        }
        if (Time.unscaledTime >= _end) Finish();
    }

    public static void Stop()
    {
        if (_shapes != null) Finish();
    }

    private static void Finish()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"記録したフレーム {_samples}");
        sb.AppendLine();
        sb.AppendLine("===== ブレンドシェイプ (顔の表情・口。記録中の最小～最大。動いたものから) =====");
        foreach (var kv in _shapes.OrderByDescending(kv => kv.Value.Max - kv.Value.Min).ThenBy(kv => kv.Key))
            sb.AppendLine($"   {kv.Value.Min,7:0.0} ～ {kv.Value.Max,7:0.0}  {kv.Key}");
        sb.AppendLine();

        if (_tracks != null)
        {
            sb.AppendLine($"===== VRM を付けたキャラ {(_root != null ? _root.name : "?")} の骨 (最大の変化。★ = VRM に動きを写している骨) =====");
            sb.AppendLine("--- 回転 (度)");
            foreach (var k in _tracks.Where(k => k.MaxAngle > 0.05f).OrderByDescending(k => k.MaxAngle).Take(60))
                sb.AppendLine($"{(k.Mapped ? "★" : "  ")} {k.MaxAngle,7:0.00}  {k.Path}");
            sb.AppendLine("--- 位置 (m)");
            foreach (var k in _tracks.Where(k => k.MaxPos > 0.0001f).OrderByDescending(k => k.MaxPos).Take(40))
                sb.AppendLine($"{(k.Mapped ? "★" : "  ")} {k.MaxPos,8:0.0000}  {k.Path}");
            sb.AppendLine("--- 大きさ");
            foreach (var k in _tracks.Where(k => k.MaxScale > 0.0001f).OrderByDescending(k => k.MaxScale).Take(40))
                sb.AppendLine($"{(k.Mapped ? "★" : "  ")} {k.MaxScale,8:0.0000}  {k.Path}");
        }
        try
        {
            Directory.CreateDirectory(ModelLab.Ctx.DataDirectory);
            var file = Path.Combine(ModelLab.Ctx.DataDirectory, $"motion_{DateTime.Now:HHmmss}.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            ModelLab.LastFile = file;
            ModelLab.Ctx.Notify("動きと表情を書き出しました", NotifyLevel.Success);
        }
        catch (Exception e) { ModelLab.Ctx?.Log.Error($"Model Lab: 書き出しに失敗: {e}"); }
        _tracks = null;
        _shapes = null;
    }
}

internal static class SkirtUi
{
    public static void Draw(WindowGui gui)
    {
        gui.Space(6f);
        gui.Header(L.T("装飾品の表示"));
        gui.Label(L.T("VRM にしたキャラの装飾品を部位ごとに隠せます (頭の装飾品が髪に埋まる・頭にめり込むときなど)。"), RuskStyle.TextDim, small: true);
        foreach (var (slot, name) in Vrm.AccessoryVisibility.Slots)
        {
            bool shown = Vrm.AccessoryVisibility.IsShown(slot);
            bool next = gui.Toggle(L.T("{0}の装飾品を表示する", L.T(name)), shown);
            if (next != shown) Vrm.AccessoryVisibility.Set(slot, next);
        }

        gui.Space(6f);
        gui.Header(L.T("明るさ・影"));
        gui.Label(L.T(Vrm.MaskModes.IsUrp
            ? "明るさ: 服や髪が白く浮いて見えるときは下げる。影の濃さ: 髪や帽子が顔に落とす影 (1.00 でゲームのキャラと同じ)。"
            : "影の濃さ: 髪や帽子が顔などに落とす影 (1.00 でゲームのキャラと同じ)。"), RuskStyle.TextDim, small: true);
        float b = Vrm.MaskModes.Brightness;
        if (Vrm.MaskModes.IsUrp && Tune(gui, "明るさ", ref b, 0.05f, 0.3f, 1.5f)) Vrm.MaskModes.SetBrightness(b, Vrm.VrmSwap.Models());
        float sh = Vrm.MaskModes.ShadowStrength;
        if (Tune(gui, "影の濃さ", ref sh, 0.1f, 0f, 1f)) Vrm.MaskModes.SetShadowStrength(sh, Vrm.VrmSwap.Models());
        if (gui.Button(L.T("明るさ・影を初期値に戻す")))
        {
            Vrm.MaskModes.SetBrightness(Vrm.MaskModes.DefaultBrightness, Vrm.VrmSwap.Models());
            Vrm.MaskModes.SetShadowStrength(Vrm.MaskModes.DefaultShadowStrength, Vrm.VrmSwap.Models());
        }

        gui.Space(6f);
        gui.Header(L.T("スカートの調整"));
        gui.Label(L.T("脚がスカートから出るのと、スカートのめくれ具合のバランスを調整します。"), RuskStyle.TextDim, small: true);
        bool changed = false;
        changed |= Tune(gui, "前後の開き", ref Vrm.SkirtTuning.FrontBack, 0.1f, 0f, 1f);
        changed |= Tune(gui, "横の開き", ref Vrm.SkirtTuning.Side, 0.1f, 0f, 1f);
        changed |= Tune(gui, "脚の振り", ref Vrm.SkirtTuning.LegSwing, 0.05f, 0.5f, 1f);
        changed |= Tune(gui, "当たり判定の太さ", ref Vrm.SkirtTuning.Collider, 0.05f, 0.5f, 2f);
        if (changed) Vrm.SkirtTuning.Save();
        if (gui.Button(L.T("初期値に戻す"))) Vrm.SkirtTuning.Reset();
    }

    private static bool Tune(WindowGui gui, string label, ref float value, float step, float min, float max)
    {
        int s = gui.Stepper(L.T(label), value.ToString("0.00"));
        if (s == 0) return false;
        value = Mathf.Clamp(Mathf.Round((value + s * step) * 100f) / 100f, min, max);
        return true;
    }
}
