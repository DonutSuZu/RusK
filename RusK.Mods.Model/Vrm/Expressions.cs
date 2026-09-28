using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// VRM の表情 (VRM 0.x は BlendShapeProxy、1.0 は Expression)。
/// 表情ごとに「どのメッシュのどのブレンドシェイプをどれだけ動かすか」を持ち、0～1 の強さで当てる。
/// ゲームのキャラの顔 (口パク・表情・まばたき) を写す。ゲームの顔が無いときは自動でまばたきする。
/// 複数の表情が同じブレンドシェイプを動かすことがあるので、表情ごとの強さを足し合わせてから当てる
/// </summary>
internal sealed class Expressions
{
    private readonly Dictionary<string, List<(SkinnedMeshRenderer smr, int index, float weight)>> _binds = new();
    private readonly Dictionary<string, float> _values = new();

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

    /// <summary>表情の強さ (0～1) を決める。名前は VRM 0.x と 1.0 の両方を渡せる (無い方は無視)</summary>
    public void Set(string name, float value)
    {
        if (_binds.ContainsKey(name)) _values[name] = Mathf.Clamp01(value);
    }

    private void Set(string vrm0, string vrm1, float value)
    {
        Set(vrm0, value);
        Set(vrm1, value);
    }

    /// <summary>決めた表情の強さを足し合わせて、VRM のブレンドシェイプに当てる</summary>
    private void Apply()
    {
        var sum = new Dictionary<(System.IntPtr, int), (SkinnedMeshRenderer smr, int index, float w)>();
        foreach (var (name, value) in _values)
        {
            foreach (var (smr, index, weight) in _binds[name])
            {
                if (smr == null || smr.sharedMesh == null || index < 0 || index >= smr.sharedMesh.blendShapeCount) continue;
                var key = (smr.Pointer, index);
                float w = sum.TryGetValue(key, out var cur) ? cur.w : 0f;
                sum[key] = (smr, index, w + weight * value);
            }
        }
        foreach (var (smr, index, w) in sum.Values) smr.SetBlendShapeWeight(index, Mathf.Min(w, 100f));
    }

    // ---- ゲームのキャラの顔を写す

    /// <summary>
    /// ゲームの顔 (例: Whitelight_face) のブレンドシェイプの値 (0～100) を、VRM の表情に写す。
    /// ゲームの顔には母音の形が無く、口パクは口の開き (Mouth_Shout) で表しているので、それを「あ」にする
    /// </summary>
    public void FromGame(System.Func<string, float> game, float dt)
    {
        float G(params string[] names)
        {
            float v = 0f;
            foreach (var n in names) v = Mathf.Max(v, game(n));
            return Mathf.Clamp01(v / 100f);
        }

        Set("a", "aa", G("Mouth_Shout", "Mouth_Roar"));
        Set("i", "ih", G("Mouth_Grin", "Mouth_Grin02"));
        Set("u", "ou", G("Mouth_Shrinks", "Mouth_Pouting_L", "Mouth_Pouting_R"));
        Set("joy", "happy", G("Mouth_Laugh"));
        Set("fun", "relaxed", G("Mouth_Smile", "Mouth_Smile02"));
        Set("angry", "angry", G("Mouth_Anger", "Eyes_Frown"));
        Set("sorrow", "sad", G("sad", "Weeping"));
        Set("surprised", G("Pupil_Shock", "Eyes_Wide_Left", "Eyes_Wide_Right"));
        // まばたき: ゲームの顔がまばたきしていればそれを写す。しばらくまばたきしない顔 (動かさない場面) なら自動のまばたき
        float blink = G("Eyes_Closed");
        if (blink > 0.05f) _sinceGameBlink = 0f;
        else _sinceGameBlink += dt;
        if (_sinceGameBlink > 8f) blink = Mathf.Max(blink, AutoBlink(dt));
        Set("blink", "blink", blink);
        Set("blink_l", "blinkleft", G("Eyes_Wink_Light", "Eyes_Wink_Left"));
        Set("blink_r", "blinkright", G("Eyes_Wink_Right"));
        Set("lookup", "lookup", G("Pupil_Up"));
        Set("lookdown", "lookdown", G("Pupil_Down"));
        Set("lookleft", "lookleft", G("Pupil_Left"));
        Set("lookright", "lookright", G("Pupil_Right"));
        Apply();
    }

    // ---- 自動のまばたき (ゲームの顔が無いとき・ゲームの顔がまばたきしないとき)

    private float _sinceGameBlink;
    private float _nextBlink = 2f;
    private float _blinkTime = -1f;
    private const float BlinkDuration = 0.16f;

    public void Update(float dt)
    {
        if (!Has("blink")) return;
        Set("blink", AutoBlink(dt));
        Apply();
    }

    /// <summary>2～6 秒ごとに閉じて開く (三角の形)。今の閉じ具合 (0～1) を返す</summary>
    private float AutoBlink(float dt)
    {
        if (_blinkTime < 0f)
        {
            _nextBlink -= dt;
            if (_nextBlink <= 0f) _blinkTime = 0f;
            return 0f;
        }
        _blinkTime += dt;
        float half = BlinkDuration * 0.5f;
        float v = _blinkTime < half ? _blinkTime / half : 1f - (_blinkTime - half) / half;
        if (_blinkTime >= BlinkDuration)
        {
            _blinkTime = -1f;
            _nextBlink = Random.Range(2f, 6f);
            return 0f;
        }
        return Mathf.Clamp01(v);
    }
}
