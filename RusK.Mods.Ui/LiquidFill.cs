using UnityEngine;

namespace RusK.Mods.Ui;

/// <summary>
/// ボタンの中に液体がたまっていくゲージ。液面は 2 つの波を重ねて揺らす (ゼンゼロ風)。
/// 毎フレーム小さなテクスチャ (96×96) に円の中の液体を描いて返す。IMGUI では円で切り抜けないので、
/// 円の外側は透明にして描く。
/// </summary>
internal sealed class LiquidFill
{
    private const int N = 96;
    private const float Radius = 0.9f; // ボタンの内側 (輪の内側) に収める

    private Texture2D _tex;
    private readonly Color32[] _px = new Color32[N * N];

    /// <param name="level">たまっている量 (0～1)</param>
    /// <param name="time">アニメーションの時間</param>
    /// <param name="deep">底の方の色</param>
    /// <param name="top">液面近くの色</param>
    public Texture Draw(float level, float time, Color deep, Color top)
    {
        if (_tex == null)
        {
            _tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        level = Mathf.Clamp01(level);
        // 空・満タンのときは波を小さくする
        float amp = Mathf.Clamp01(level * 8f) * Mathf.Clamp01((1f - level) * 8f);
        float baseLevel = -Radius + 2f * Radius * level;
        float edge = 2f / N; // ふちのぼかし (1 ピクセル分)

        for (int y = 0; y < N; y++)
        {
            float v = (y + 0.5f) / N * 2f - 1f; // 下が -1、上が +1 (テクスチャは下から)
            for (int x = 0; x < N; x++)
            {
                float u = (x + 0.5f) / N * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float inside = Mathf.Clamp01((Radius - d) / edge);
                if (inside <= 0f)
                {
                    _px[y * N + x] = default;
                    continue;
                }

                float wave = amp * (0.07f * Mathf.Sin(u * 3.1f + time * 2.4f) + 0.04f * Mathf.Sin(u * 6.3f - time * 3.7f + 1.3f));
                float surface = baseLevel + wave;
                float below = (surface - v) / edge; // 液面から下へ何ピクセルか
                float liquid = Mathf.Clamp01(below);
                if (liquid <= 0f)
                {
                    _px[y * N + x] = default;
                    continue;
                }

                // 底から液面へ色を変え、液面のすぐ下は明るい帯 (水面の光)
                float depth = Mathf.Clamp01((surface - v) / Mathf.Max(0.05f, surface + Radius));
                var c = Color.Lerp(top, deep, depth);
                float shine = Mathf.Clamp01(1f - (surface - v) / 0.09f);
                c = Color.Lerp(c, Color.white, shine * 0.45f);
                c.a = inside * liquid * 0.92f;
                _px[y * N + x] = c;
            }
        }

        _tex.SetPixels32(_px);
        _tex.Apply(false);
        return _tex;
    }

    public void Dispose()
    {
        if (_tex != null) Object.Destroy(_tex);
        _tex = null;
    }
}
