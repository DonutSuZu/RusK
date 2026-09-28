using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using RusK.API;
using UnityEngine;
using Module = RusK.API.Module;
using Random = UnityEngine.Random;

namespace RusK.Mods.Ui;

/// <summary>
/// 敵が攻撃するときに「キラーン」と光る予兆エフェクト (＋効果音)。
/// タイミングは 2 種類:
///   AttackStart … 敵のモーションが攻撃モーションに切り替わった瞬間 (EnemyController.MotionChangeSet)
///   GameWarning … ゲーム自身が攻撃予兆を出す瞬間 (EnemyController.CreateWarning)
/// 位置はゲームの予兆表示と同じ EnemyController.GetWarningPosition を使う。
/// </summary>
public sealed class AttackGlintModule : Module
{
    internal static AttackGlintModule Instance;
    private static readonly string[] Timings = { "AttackStart", "GameWarning" };

    private readonly ModeSetting _timing;
    private readonly ColorSetting _color;
    private readonly FloatSetting _size;
    private readonly FloatSetting _duration;
    private readonly FloatSetting _height;
    private readonly BoolSetting _skipCombo;
    private readonly BoolSetting _sound;
    private readonly FloatSetting _volume;

    private sealed class Glint
    {
        public EnemyController Enemy;
        public Vector3 World;
        public float Start;
        public float Spin;
    }

    private readonly List<Glint> _glints = new();
    private readonly Dictionary<IntPtr, float> _lastGlint = new();
    private float _lastSound;
    private bool _patched;

    private AudioClip _clip;
    private AudioSource _audio;

    public AttackGlintModule() : base("AttackGlint", Categories.Visual, "敵の攻撃の瞬間にキラーンと光らせる")
    {
        Instance = this;
        _timing = AddSetting(new ModeSetting("Timing", Timings, 0,
            "AttackStart: 攻撃モーション開始時 / GameWarning: ゲームの攻撃予兆と同時"));
        _color = AddSetting(new ColorSetting("Color", "#FFD66B", "光の色"));
        _size = AddSetting(new FloatSetting("Size", 90f, 30f, 250f, 5f, "0", "光の大きさ"));
        _duration = AddSetting(new FloatSetting("Duration", 0.5f, 0.2f, 1.5f, 0.05f, "0.00s", "光っている時間"));
        _height = AddSetting(new FloatSetting("Height", 0f, -2f, 3f, 0.1f, "0.0", "光る位置の高さ補正"));
        _skipCombo = AddSetting(new BoolSetting("FirstHitOnly", true, "連続攻撃は最初の 1 撃だけ光らせる"));
        _sound = AddSetting(new BoolSetting("Sound", true, "キラーン音を鳴らす"));
        _volume = AddSetting(new FloatSetting("Volume", 0.6f, 0f, 1f, 0.05f, "0.00", "効果音の音量"));
    }

    public override void OnEnable()
    {
        if (_patched) return;
        Context.Harmony.PatchAll(typeof(GlintMotionPatch));
        Context.Harmony.PatchAll(typeof(GlintWarningPatch));
        _patched = true;
    }

    public override void OnDisable() => _glints.Clear();

    /// <summary>Harmony パッチから呼ばれる</summary>
    internal void OnEnemyAttack(EnemyController enemy, MotionState motion, bool fromWarning)
    {
        if (!Enabled || enemy == null) return;
        if (fromWarning != (_timing.Value == 1)) return;
        if (!fromWarning && _skipCombo.Value && motion != null && motion.comboIndex > 0) return;

        // 同じ敵が短い間に何度も光らないように
        float now = Time.unscaledTime;
        var key = enemy.Pointer;
        if (_lastGlint.TryGetValue(key, out var last) && now - last < 0.35f) return;
        _lastGlint[key] = now;
        if (_lastGlint.Count > 256)
            foreach (var old in _lastGlint.Where(p => now - p.Value > 5f).Select(p => p.Key).ToList())
                _lastGlint.Remove(old);

        _glints.Add(new Glint
        {
            Enemy = enemy,
            World = GlintPosition(enemy),
            Start = now,
            Spin = Random.value < 0.5f ? -1f : 1f,
        });

        if (_sound.Value && now - _lastSound > 0.08f)
        {
            _lastSound = now;
            PlaySound();
        }
    }

    public override void OnGUI()
    {
        if (!Render.IsRepaint || _glints.Count == 0) return;
        var cam = Camera.main;
        if (cam == null) return;

        float now = Time.unscaledTime;
        float duration = _duration.Value;

        for (int i = _glints.Count - 1; i >= 0; i--)
        {
            var g = _glints[i];
            float t = (now - g.Start) / duration;
            if (t >= 1f) { _glints.RemoveAt(i); continue; }

            // 敵が動いたら追従 (倒されて消えたら最後の位置のまま)
            if (g.Enemy != null) g.World = GlintPosition(g.Enemy);

            var screen = cam.WorldToScreenPoint(g.World);
            if (screen.z <= 0f) continue; // カメラの後ろ
            DrawGlint(screen.x, Screen.height - screen.y, t, g.Spin);
        }
    }

    /// <summary>
    /// キラーンの形: 十字の長い光芒 + 斜めの短い光芒 + 中心のにじみ + 広がるリング。
    /// 一瞬で大きくなり (オーバーシュート)、少し回転しながら細く伸びて消える。
    /// </summary>
    private void DrawGlint(float cx, float cy, float t, float spin)
    {
        float scale = Screen.height / 1080f;
        float size = _size.Value * scale;
        var color = _color.Value;

        float grow = EaseOutBack(Mathf.Clamp01(t / 0.16f));
        float alpha = t < 0.5f ? 1f : 1f - (t - 0.5f) / 0.5f;
        float len = size * grow * (1f + 0.35f * t);
        float thick = Mathf.Max(2f, size * 0.075f * (1f - 0.55f * t));
        float angle = spin * 30f * t;

        // にじみ (中心の丸い光)
        float glow = size * 0.32f * grow;
        Render.Rect(cx - glow, cy - glow, glow * 2, glow * 2, Render.WithAlpha(color, 0.30f * alpha), glow);

        // 広がるリング (最初だけ)
        if (t < 0.6f)
        {
            float r = size * (0.25f + 0.8f * t);
            float ra = (1f - t / 0.6f) * 0.6f * alpha;
            GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2, r * 2), Texture2D.whiteTexture, ScaleMode.StretchToFill,
                true, 0f, Render.WithAlpha(color, ra), Mathf.Max(1.5f, 2.5f * scale), r);
        }

        var saved = GUI.matrix;
        var pivot = new Vector2(cx, cy);

        // 十字の光芒
        GUIUtility.RotateAroundPivot(angle, pivot);
        Spike(cx, cy, len, thick, color, alpha);
        Spike(cx, cy, thick, len * 0.8f, color, alpha);
        GUI.matrix = saved;

        // 斜めの短い光芒
        GUIUtility.RotateAroundPivot(angle + 45f, pivot);
        Spike(cx, cy, len * 0.42f, thick * 0.7f, color, alpha * 0.8f);
        Spike(cx, cy, thick * 0.7f, len * 0.42f, color, alpha * 0.8f);
        GUI.matrix = saved;

        // 芯 (白く強い点)
        float core = Mathf.Max(3f, size * 0.07f * grow);
        Render.Rect(cx - core, cy - core, core * 2, core * 2, new Color(1f, 1f, 1f, alpha), core);
    }

    /// <summary>
    /// 光芒 1 本。カプセルを 3 枚重ねて、先が細く中心が白い形に見せる。
    /// w/h の大きい方が長さ方向。
    /// </summary>
    private static void Spike(float cx, float cy, float w, float h, Color color, float alpha)
    {
        Layer(cx, cy, w, h, Render.WithAlpha(color, 0.45f * alpha));
        Layer(cx, cy, w * (w > h ? 0.72f : 0.55f), h * (h > w ? 0.72f : 0.55f), Render.WithAlpha(color, 0.9f * alpha));
        Layer(cx, cy, w * (w > h ? 0.42f : 0.3f), h * (h > w ? 0.42f : 0.3f), new Color(1f, 1f, 1f, alpha));
    }

    private static void Layer(float cx, float cy, float w, float h, Color c) =>
        Render.Rect(cx - w * 0.5f, cy - h * 0.5f, w, h, c, Mathf.Min(w, h) * 0.5f);

    private Vector3 GlintPosition(EnemyController enemy)
    {
        try { return enemy.GetWarningPosition(_height.Value); }
        catch { return enemy.transform.position + Vector3.up * (1.8f + _height.Value); }
    }

    private void PlaySound()
    {
        try
        {
            EnsureAudio();
            _audio.PlayOneShot(_clip, _volume.Value);
        }
        catch (Exception e)
        {
            Context.Log.Warning($"Glint sound failed, disabling sound: {e.Message}");
            _sound.Value = false;
        }
    }

    /// <summary>
    /// 効果音をその場で合成する (音声ファイルを同梱しなくてよい)。
    /// 高い音程へ一瞬しゃくり上がる倍音付きのサイン波 + キラキラした揺れ + 減衰。
    /// </summary>
    private void EnsureAudio()
    {
        if (_clip == null)
        {
            const int rate = 44100;
            const float seconds = 0.7f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = t < 0.004f ? t / 0.004f : Mathf.Exp(-t * 6.5f);
                float freq = 2400f + 1100f * (1f - Mathf.Exp(-t * 35f)); // キ→ラーン と上がる
                phase += 2 * Math.PI * freq / rate;
                float s = (float)(Math.Sin(phase) * 0.6 + Math.Sin(phase * 2.01) * 0.25 + Math.Sin(phase * 3.02) * 0.1);
                float shimmer = 1f + 0.18f * Mathf.Sin(2f * Mathf.PI * 32f * t);
                data[i] = s * env * shimmer * 0.5f;
            }

            _clip = AudioClip.Create("RusK_Glint", n, 1, rate, false);
            _clip.SetData(new Il2CppStructArray<float>(data), 0);
            _clip.hideFlags = HideFlags.HideAndDontSave;
        }

        if (_audio == null)
        {
            var go = new GameObject("RusK_GlintAudio");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            _audio = go.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f; // 2D (距離で小さくならない)
        }
    }

    /// <summary>Mod のアンロード時に、作った音と GameObject を片付ける</summary>
    internal void Cleanup()
    {
        _glints.Clear();
        if (_audio != null) UnityEngine.Object.Destroy(_audio.gameObject);
        if (_clip != null) UnityEngine.Object.Destroy(_clip);
        _audio = null;
        _clip = null;
    }

    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float u = t - 1f;
        return 1f + c3 * u * u * u + c1 * u * u;
    }
}

// void EnemyController.MotionChangeSet(MotionState motion, MotionState motionPre)
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.MotionChangeSet))]
internal static class GlintMotionPatch
{
    private static void Postfix(EnemyController __instance, MotionState motion)
    {
        try
        {
            var m = AttackGlintModule.Instance;
            if (m == null || !m.Enabled || motion == null || !motion.isAttack) return;
            m.OnEnemyAttack(__instance, motion, fromWarning: false);
        }
        catch (Exception e)
        {
            AttackGlintModule.Instance?.Context?.Log.Error(e);
        }
    }
}

// void EnemyController.CreateWarning(...) の 3 つのオーバーロードすべて
[HarmonyPatch]
internal static class GlintWarningPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(EnemyController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(EnemyController.CreateWarning));

    private static void Postfix(EnemyController __instance)
    {
        try
        {
            var m = AttackGlintModule.Instance;
            if (m == null || !m.Enabled) return;
            m.OnEnemyAttack(__instance, null, fromWarning: true);
        }
        catch (Exception e)
        {
            AttackGlintModule.Instance?.Context?.Log.Error(e);
        }
    }
}
