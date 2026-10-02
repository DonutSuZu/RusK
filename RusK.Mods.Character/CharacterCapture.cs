using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Character;

/// <summary>
/// 新しいキャラの絵の素材を撮る: そのキャラを操作しているときに、付いているモデル (Custom VRM Loader の VRM / PMX) を、
/// 背景を透明にして撮影する (顔・バストアップ・全身)。〈キャラのフォルダ〉/captures に PNG で保存する。
/// まだ撮っていなければ自動で 1 回、メニューの Capture でいつでも撮り直せる
/// </summary>
internal static class CharacterCapture
{
    private static readonly HashSet<long> Tried = new();
    private static long _requested = -1;
    private static float _readyAt = -1f;

    public static void Request(long id)
    {
        _requested = id;
        _readyAt = -1f;
    }

    public static void Tick()
    {
        var p = RusK.Mods.Shared.PlayerRef.Current;
        if (p == null || !p.gameObject.activeInHierarchy) { _readyAt = -1f; return; }
        var def = CharacterRegistry.Find(p.GetPlayerId());
        if (def == null) { _readyAt = -1f; return; }
        bool auto = !Tried.Contains(def.Id) && !File.Exists(Path.Combine(def.Folder, "captures", "face.png"));
        if (!auto && _requested != def.Id) return;

        var model = FindModel(p.transform);
        if (model == null) return; // モデルが付くまで待つ
        // モデルが付いて落ち着くまで少し待つ (揺れ物など)
        if (_readyAt < 0f) { _readyAt = Time.unscaledTime + 1.5f; return; }
        if (Time.unscaledTime < _readyAt) return;

        Tried.Add(def.Id);
        _requested = -1;
        _readyAt = -1f;
        try { Capture(def, p.transform, model); }
        catch (Exception e) { CharacterMod.Ctx?.Log.Warning($"Character: 撮影に失敗: {e}"); }
    }

    /// <summary>キャラに付いているモデルの根元 (Custom VRM Loader が作る RusK_PMX_* / RusK_VRM_*。キャラと同じ場所にある)</summary>
    private static Transform FindModel(Transform character)
    {
        Transform best = null;
        float bestDist = 0.5f;
        foreach (var go in Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            var root = go.transform.root;
            if (root == null || !(root.name.StartsWith("RusK_PMX_") || root.name.StartsWith("RusK_VRM_")) || !root.gameObject.activeInHierarchy) continue;
            float d = (root.position - character.position).magnitude;
            if (d < bestDist) { bestDist = d; best = root; }
        }
        return best;
    }

    private static void Capture(CharacterDef def, Transform character, Transform model)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !r.forceRenderingOff).ToArray();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        // 撮る間だけ、モデルを空いているレイヤーに移す (同じレイヤーの仲間・敵・背景が写らないように)
        const int layer = 31;
        var layers = model.GetComponentsInChildren<Transform>(true).Select(t => (t, t.gameObject.layer)).ToList();
        foreach (var (t, _) in layers) t.gameObject.layer = layer;
        try { Capture(def, character, bounds, layer); }
        finally { foreach (var (t, l) in layers) if (t != null) t.gameObject.layer = l; }
    }

    private static void Capture(CharacterDef def, Transform character, Bounds bounds, int layer)
    {

        // 顔の位置: ゲームのキャラの頭の骨 (無ければモデルの上の方)
        Transform head = null;
        foreach (var t in character.GetComponentsInChildren<Transform>(true))
            if (t.name == "Bip001 Head") { head = t; break; }
        var headPos = head != null ? head.position + Vector3.up * 0.06f : new Vector3(bounds.center.x, bounds.max.y - 0.12f, bounds.center.z);
        var fwd = character.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        float height = bounds.size.y;

        var dir = Path.Combine(def.Folder, "captures");
        Directory.CreateDirectory(dir);
        // 顔 (正方形)、バストアップ (縦長)、全身 (縦長)
        Shot(Path.Combine(dir, "face.png"), layer, headPos, fwd, 0.55f, 1024, 1024);
        Shot(Path.Combine(dir, "bust.png"), layer, headPos - Vector3.up * height * 0.22f, fwd, height * 0.42f, 1440, 2160);
        Shot(Path.Combine(dir, "full.png"), layer, bounds.center, fwd, height * 0.56f, 1440, 2880);
        CharacterMod.Ctx?.Log.Info($"Character: {def.Key} の絵の素材を撮りました → {dir}");
    }

    /// <summary>center を中心に、前から (正投影で) 撮る。halfHeight は写す高さの半分</summary>
    private static void Shot(string file, int layer, Vector3 center, Vector3 fwd, float halfHeight, int w, int h)
    {
        var go = new GameObject("RusK_CharacterCapture");
        RenderTexture rt = null;
        Texture2D tex = null;
        var prev = RenderTexture.active;
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << layer;
            cam.orthographic = true;
            cam.orthographicSize = halfHeight;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 10f;
            cam.transform.position = center + fwd * 3f;
            cam.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, ImageConversion.EncodeToPNG(tex));
        }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            if (tex != null) Object.Destroy(tex);
            Object.Destroy(go);
        }
    }
}
