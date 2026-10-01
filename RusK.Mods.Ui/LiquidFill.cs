using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Ui;

/// <summary>
/// ボタンの中に液体がたまっていくゲージ。液面は 2 つの波を重ねて揺らす (ゼンゼロ風)。
/// 小さなテクスチャ (既定 96×96) に円の中の液体を描いて返す。IMGUI では円で切り抜けないので、円の外側は透明にして描く。
/// Party Op.2 でもソースごと共有している (Compile Include)。
///
/// 軽くするための工夫 (Party Op.2 で 9 個描いたら FPS が 30 を切った):
///   - ピクセルはバイト配列に書き、LoadRawTextureData でメモリをそのまま一度で渡す
///     (SetPixels32 に C# の配列を渡すと、IL2CPP の配列への変換が毎回起きて重い)
///   - 円の形 (中心からの距離・ふちのぼかし) は最初に一度だけ計算する。波の高さは列ごとに 1 回だけ計算する
///   - 描き直しは 1 秒に 30 回まで。水位・色が変わらず波も止まっている (空か満タン) ときは描き直さない
/// </summary>
internal sealed class LiquidFill
{
    private const float Radius = 0.9f; // ボタンの内側 (輪の内側) に収める
    private const float MaxFps = 30f;

    private readonly int _n;
    private Texture2D _tex;
    private readonly byte[] _raw;
    private readonly float[] _v;      // 行ごとの縦の位置 (-1〜1)
    private readonly float[] _inside; // 円の内側か (ふちはぼかす、0〜1)
    private readonly float[] _wave;   // 列ごとの波の高さ

    private float _lastAt = -999f, _lastLevel = -1f;
    private Color _lastDeep, _lastTop;

    public LiquidFill(int size = 96)
    {
        _n = Mathf.Clamp(size, 16, 256);
        _raw = new byte[_n * _n * 4];
        _v = new float[_n];
        _inside = new float[_n * _n];
        _wave = new float[_n];
        float edge = 2f / _n;
        for (int y = 0; y < _n; y++)
        {
            float v = (y + 0.5f) / _n * 2f - 1f; // 下が -1、上が +1 (テクスチャは下から)
            _v[y] = v;
            for (int x = 0; x < _n; x++)
            {
                float u = (x + 0.5f) / _n * 2f - 1f;
                _inside[y * _n + x] = Mathf.Clamp01((Radius - Mathf.Sqrt(u * u + v * v)) / edge);
            }
        }
    }

    /// <param name="level">たまっている量 (0～1)</param>
    /// <param name="time">アニメーションの時間</param>
    /// <param name="deep">底の方の色</param>
    /// <param name="top">液面近くの色</param>
    public Texture Draw(float level, float time, Color deep, Color top)
    {
        level = Mathf.Clamp01(level);
        // 空・満タンのときは波を小さくする (0 なら止まっている)
        float amp = Mathf.Clamp01(level * 8f) * Mathf.Clamp01((1f - level) * 8f);

        bool same = _tex != null && Mathf.Abs(level - _lastLevel) < 0.002f && deep == _lastDeep && top == _lastTop;
        if (same && (amp <= 0f || Time.unscaledTime - _lastAt < 1f / MaxFps)) return _tex;
        _lastAt = Time.unscaledTime;
        _lastLevel = level;
        _lastDeep = deep;
        _lastTop = top;

        if (_tex == null)
        {
            _tex = new Texture2D(_n, _n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        float baseLevel = -Radius + 2f * Radius * level;
        float edge = 2f / _n;
        for (int x = 0; x < _n; x++)
        {
            float u = (x + 0.5f) / _n * 2f - 1f;
            _wave[x] = baseLevel + amp * (0.07f * Mathf.Sin(u * 3.1f + time * 2.4f) + 0.04f * Mathf.Sin(u * 6.3f - time * 3.7f + 1.3f));
        }

        int i = 0;
        for (int y = 0; y < _n; y++)
        {
            float v = _v[y];
            for (int x = 0; x < _n; x++, i += 4)
            {
                float inside = _inside[y * _n + x];
                float surface = _wave[x];
                float liquid = inside <= 0f ? 0f : Mathf.Clamp01((surface - v) / edge);
                if (liquid <= 0f)
                {
                    _raw[i] = _raw[i + 1] = _raw[i + 2] = _raw[i + 3] = 0;
                    continue;
                }
                // 底から液面へ色を変え、液面のすぐ下は明るい帯 (水面の光)
                float depth = Mathf.Clamp01((surface - v) / Mathf.Max(0.05f, surface + Radius));
                float shine = Mathf.Clamp01(1f - (surface - v) / 0.09f) * 0.45f;
                float r = top.r + (deep.r - top.r) * depth, g = top.g + (deep.g - top.g) * depth, b = top.b + (deep.b - top.b) * depth;
                r += (1f - r) * shine;
                g += (1f - g) * shine;
                b += (1f - b) * shine;
                _raw[i] = (byte)(r * 255f);
                _raw[i + 1] = (byte)(g * 255f);
                _raw[i + 2] = (byte)(b * 255f);
                _raw[i + 3] = (byte)(inside * liquid * 0.92f * 255f);
            }
        }

        unsafe
        {
            fixed (byte* p = _raw) _tex.LoadRawTextureData((IntPtr)p, _raw.Length);
        }
        _tex.Apply(false);
        return _tex;
    }

    public void Dispose()
    {
        if (_tex != null) Object.Destroy(_tex);
        _tex = null;
    }
}
