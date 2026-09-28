using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// VRM の表情 (VRM 0.x は BlendShapeProxy、1.0 は Expression)。
/// 表情ごとに「どのメッシュのどのブレンドシェイプをどれだけ動かすか」を持ち、0～1 の強さで当てる。
/// 今は自動のまばたきだけ動かす。
/// </summary>
internal sealed class Expressions
{
    private readonly Dictionary<string, List<(SkinnedMeshRenderer smr, int index, float weight)>> _binds = new();

    public int Count => _binds.Count;

    public static Expressions Read(JsonElement ext, VrmModel model)
    {
        var ex = new Expressions();
        if (model.Version == 0)
        {
            if (!ext.GetProperty("VRM").TryGetProperty("blendShapeMaster", out var master)) return ex;
            foreach (var g in master.GetProperty("blendShapeGroups").EnumerateArray())
            {
                string name = g.TryGetProperty("presetName", out var p) && p.GetString() != "unknown"
                    ? p.GetString() : g.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name) || !g.TryGetProperty("binds", out var binds)) continue;
                var list = ex.List(name.ToLowerInvariant());
                foreach (var b in binds.EnumerateArray())
                {
                    int mesh = b.GetProperty("mesh").GetInt32();
                    if (!model.MeshRenderers.TryGetValue(mesh, out var smrs)) continue;
                    foreach (var smr in smrs)
                        list.Add((smr, b.GetProperty("index").GetInt32(), b.GetProperty("weight").GetSingle())); // 0～100
                }
            }
        }
        else
        {
            if (!ext.GetProperty("VRMC_vrm").TryGetProperty("expressions", out var exps)) return ex;
            foreach (var kind in new[] { "preset", "custom" })
            {
                if (!exps.TryGetProperty(kind, out var set)) continue;
                foreach (var e in set.EnumerateObject())
                {
                    if (!e.Value.TryGetProperty("morphTargetBinds", out var binds)) continue;
                    var list = ex.List(e.Name.ToLowerInvariant());
                    foreach (var b in binds.EnumerateArray())
                    {
                        if (!model.NodeRenderers.TryGetValue(b.GetProperty("node").GetInt32(), out var smr)) continue;
                        list.Add((smr, b.GetProperty("index").GetInt32(), b.GetProperty("weight").GetSingle() * 100f)); // 0～1
                    }
                }
            }
        }
        return ex;
    }

    private List<(SkinnedMeshRenderer, int, float)> List(string name)
    {
        if (!_binds.TryGetValue(name, out var list)) _binds[name] = list = new();
        return list;
    }

    public bool Has(string name) => _binds.ContainsKey(name);

    /// <summary>表情の強さ (0～1) を当てる</summary>
    public void Set(string name, float value)
    {
        if (!_binds.TryGetValue(name, out var list)) return;
        foreach (var (smr, index, weight) in list)
        {
            if (smr == null || smr.sharedMesh == null || index < 0 || index >= smr.sharedMesh.blendShapeCount) continue;
            smr.SetBlendShapeWeight(index, weight * value);
        }
    }

    // ---- 自動のまばたき

    private float _nextBlink = 2f;
    private float _blinkTime = -1f;
    private const float BlinkDuration = 0.16f;

    public void Update(float dt)
    {
        if (!Has("blink")) return;
        if (_blinkTime < 0f)
        {
            _nextBlink -= dt;
            if (_nextBlink <= 0f) _blinkTime = 0f;
            return;
        }

        _blinkTime += dt;
        // 閉じて開く (三角の形)
        float half = BlinkDuration * 0.5f;
        float v = _blinkTime < half ? _blinkTime / half : 1f - (_blinkTime - half) / half;
        Set("blink", Mathf.Clamp01(v));
        if (_blinkTime >= BlinkDuration)
        {
            Set("blink", 0f);
            _blinkTime = -1f;
            _nextBlink = Random.Range(2f, 6f);
        }
    }
}
