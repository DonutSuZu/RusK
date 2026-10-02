using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace RusK.Mods.Effect;

/// <summary>
/// エフェクト (ほぼ全部 ParticleSystem) の見た目を変える。
/// ゲームのシェーダーは色のプロパティを持たず、色はパーティクルの startColor と colorOverLifetime で付いているので、そこを変える。
/// エフェクトは使い回される (ResourceManager のプール) ので、最初に見たときの元の値を覚えておき、毎回そこから計算し直す。
/// 同じ設定がもうかかっているものはそのまま (エフェクトが出るたびに作り直さない)
/// </summary>
internal static class EffectApplier
{
    /// <summary>グラデーションの元の値</summary>
    private sealed class Grad
    {
        public GradientMode Mode;
        public GradientColorKey[] Colors;
        public GradientAlphaKey[] Alphas;

        public static Grad From(Gradient g)
        {
            if (g == null) return null;
            var ck = g.colorKeys;
            var ak = g.alphaKeys;
            var colors = new GradientColorKey[ck.Length];
            for (int i = 0; i < colors.Length; i++) colors[i] = ck[i];
            var alphas = new GradientAlphaKey[ak.Length];
            for (int i = 0; i < alphas.Length; i++) alphas[i] = ak[i];
            return new Grad { Mode = g.mode, Colors = colors, Alphas = alphas };
        }

        public Gradient Build(EffectRule rule)
        {
            var colors = new Il2CppStructArray<GradientColorKey>(Colors.Length);
            for (int i = 0; i < Colors.Length; i++)
            {
                var c = rule == null ? Colors[i].color : rule.Apply(Colors[i].color);
                colors[i] = new GradientColorKey(new Color(c.r, c.g, c.b, 1f), Colors[i].time);
            }
            var alphas = new Il2CppStructArray<GradientAlphaKey>(Alphas.Length);
            for (int i = 0; i < Alphas.Length; i++)
                alphas[i] = new GradientAlphaKey(Mathf.Clamp01(Alphas[i].alpha * (rule?.Opacity ?? 1f)), Alphas[i].time);
            var g = new Gradient { mode = Mode };
            g.SetKeys(colors, alphas);
            return g;
        }
    }

    /// <summary>MinMaxGradient (startColor・colorOverLifetime) の元の値</summary>
    private sealed class MinMax
    {
        public ParticleSystemGradientMode Mode;
        public Color ColorMin, ColorMax;
        public Grad GradMin, GradMax;

        public static MinMax From(ParticleSystem.MinMaxGradient m) => new()
        {
            Mode = m.mode,
            ColorMin = m.colorMin,
            ColorMax = m.colorMax,
            GradMin = Grad.From(m.gradientMin),
            GradMax = Grad.From(m.gradientMax),
        };

        public ParticleSystem.MinMaxGradient Build(EffectRule rule)
        {
            Color C(Color c) => rule == null ? c : rule.Apply(c);
            // IL2CPP では色 1 つのコンストラクターしか無いので、作ってから残りを入れる
            var m = new ParticleSystem.MinMaxGradient(C(ColorMax));
            switch (Mode)
            {
                case ParticleSystemGradientMode.TwoColors:
                    m.colorMin = C(ColorMin);
                    m.colorMax = C(ColorMax);
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    if (GradMin != null) m.gradientMin = GradMin.Build(rule);
                    if (GradMax != null) m.gradientMax = GradMax.Build(rule);
                    break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:
                    if (GradMax != null) m.gradientMax = GradMax.Build(rule);
                    break;
            }
            m.mode = Mode;
            return m;
        }
    }

    /// <summary>1 つの ParticleSystem の元の値</summary>
    private sealed class Original
    {
        public MinMax StartColor;
        public bool LifetimeEnabled;
        public MinMax Lifetime;
        public bool Size3D;
        public float Size, SizeX, SizeY, SizeZ;
        public bool RendererEnabled;
    }

    // キーは Unity のインスタンス ID (セッション中は一意。ポインタは使い回されることがある)。
    // エフェクトはシーンをまたいで使い回されるので、元の値は捨てない (捨てると、変えた後の色を元の値として覚えてしまう)
    private static readonly Dictionary<int, Original> Originals = new();
    private static readonly Dictionary<int, string> Applied = new();

    /// <summary>エフェクト (の根の GameObject) に設定をかける。rule が null なら元に戻す</summary>
    public static void Apply(GameObject root, EffectRule rule)
    {
        if (root == null) return;
        var signature = rule == null || rule.IsIdentity ? "" : rule.Signature;
        int rootId = root.GetInstanceID();
        if (Applied.TryGetValue(rootId, out var prev) ? prev == signature : signature == "") return;

        foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (InReplacement(ps.transform, root.transform)) continue; // 差し替え先 (子) は別にかける
            try { ApplyOne(ps, signature == "" ? null : rule); }
            catch (Exception e) { EffectMod.Ctx?.Log.Warning($"Effect: {root.name}/{ps.name}: {e.Message}"); }
        }
        Applied[rootId] = signature;
    }

    /// <summary>root の下の、差し替え先のエフェクト (名前が "RusK:" で始まる子) の中か</summary>
    private static bool InReplacement(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name.StartsWith("RusK:")) return true;
        return false;
    }

    private static void ApplyOne(ParticleSystem ps, EffectRule rule)
    {
        int id = ps.GetInstanceID();
        var main = ps.main;
        var lifetime = ps.colorOverLifetime;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        if (!Originals.TryGetValue(id, out var o))
        {
            o = new Original
            {
                StartColor = MinMax.From(main.startColor),
                LifetimeEnabled = lifetime.enabled,
                Lifetime = lifetime.enabled ? MinMax.From(lifetime.color) : null,
                Size3D = main.startSize3D,
                Size = main.startSizeMultiplier,
                SizeX = main.startSizeXMultiplier,
                SizeY = main.startSizeYMultiplier,
                SizeZ = main.startSizeZMultiplier,
                RendererEnabled = renderer != null && renderer.enabled,
            };
            Originals[id] = o;
        }

        main.startColor = o.StartColor.Build(rule);
        if (o.Lifetime != null) lifetime.color = o.Lifetime.Build(rule);

        float size = rule?.Size ?? 1f;
        if (o.Size3D)
        {
            main.startSizeXMultiplier = o.SizeX * size;
            main.startSizeYMultiplier = o.SizeY * size;
            main.startSizeZMultiplier = o.SizeZ * size;
        }
        else
        {
            main.startSizeMultiplier = o.Size * size;
        }

        if (renderer != null) renderer.enabled = o.RendererEnabled && !(rule?.HidesOriginal ?? false);
    }
}
