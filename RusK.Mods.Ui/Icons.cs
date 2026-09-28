using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

using RusK.Mods.Shared;

namespace RusK.Mods.Ui;

/// <summary>
/// ボタン HUD の簡単なアイコンを、画像ファイルなしでその場で作る。
/// 図形を「距離関数」(点から図形の縁までの距離) で表し、それをピクセルに塗ると
/// ふちが滑らかな (アンチエイリアスのかかった) 白いアイコンになる。色は描画時に付ける。
/// 座標は -1～1、y は上向き。
/// </summary>
internal static class Icons
{
    private const int Size = 128;

    private static Texture2D _dodge, _attack, _extra, _skill, _guard;

    public static Texture2D Dodge => _dodge ??= Build(DodgeShape);
    public static Texture2D Attack => _attack ??= Build(AttackShape);
    public static Texture2D Extra => _extra ??= Build(ExtraShape);
    public static Texture2D Skill => _skill ??= Build(SkillShape);
    public static Texture2D Guard => _guard ??= Build(GuardShape);

    public static void Dispose()
    {
        foreach (var t in new[] { _dodge, _attack, _extra, _skill, _guard })
            if (t != null) UnityEngine.Object.Destroy(t);
        _dodge = _attack = _extra = _skill = _guard = null;
    }

    // ガード: 盾 (外枠 + 内側の小さな盾の二重)
    private static readonly Vector2[] ShieldOutline =
    {
        new(-0.66f, 0.70f), new(0f, 0.82f), new(0.66f, 0.70f), new(0.64f, 0.05f),
        new(0.40f, -0.48f), new(0f, -0.86f), new(-0.40f, -0.48f), new(-0.64f, 0.05f),
    };

    private static float GuardShape(Vector2 p)
    {
        float outer = Polygon(p, ShieldOutline);
        float inner = Polygon(p * 1.30f, ShieldOutline) / 1.30f; // 1/1.3 に縮めた盾
        float core = Polygon(p * 2.1f, ShieldOutline) / 2.1f;    // 真ん中の小さな盾
        return Mathf.Min(Mathf.Max(outer, -inner), core);
    }

    // 回避: 上向きの二重山形 (≫ を上に向けた形)
    private static float DodgeShape(Vector2 p) => Min(
        Polyline(p, 0.13f, new(-0.55f, 0.00f), new(0f, 0.52f), new(0.55f, 0.00f)),
        Polyline(p, 0.13f, new(-0.55f, -0.48f), new(0f, 0.04f), new(0.55f, -0.48f)));

    // 攻撃: 斜めの剣 (刃・鍔・柄・柄頭)
    private static float AttackShape(Vector2 p) => Min(
        Min(
            Polygon(p, new(-0.28f, -0.20f), new(0.58f, 0.66f), new(0.70f, 0.70f), new(0.66f, 0.58f), new(-0.20f, -0.28f)),
            Segment(p, new(-0.52f, -0.08f), new(-0.08f, -0.52f), 0.08f)),
        Min(
            Segment(p, new(-0.30f, -0.30f), new(-0.58f, -0.58f), 0.07f),
            Circle(p, new(-0.66f, -0.66f), 0.11f)));

    // 追加攻撃: 手裏剣 (4 枚刃の星、中心に穴)
    private static float ExtraShape(Vector2 p)
    {
        var star = Star(p, 4, 0.88f, 0.30f, 45f);
        return Mathf.Max(star, -Circle(p, Vector2.zero, 0.13f));
    }

    // スキル: きらめき (細い 4 芒星) と、切れ目のある輪
    private static float SkillShape(Vector2 p)
    {
        float sparkle = Star(p, 4, 0.60f, 0.14f, 0f);
        float ring = Mathf.Abs(p.magnitude - 0.82f) - 0.07f;
        float ang = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
        float a = ((ang % 180f) + 180f) % 180f;
        if (a > 20f && a < 58f) ring = 1f; // 斜めに 2 か所の切れ目で「回っている」感じに
        return Mathf.Min(sparkle, ring);
    }

    private static Texture2D Build(Func<Vector2, float> shape)
    {
        var pixels = new Color32[Size * Size];
        float px = 2.2f / Size; // 1 ピクセルの大きさ (図形座標で)。少し余白を取る
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                var p = new Vector2((x + 0.5f) * px - 1.1f, (y + 0.5f) * px - 1.1f);
                float d = shape(p);
                byte alpha = (byte)(Mathf.Clamp01(0.5f - d / px) * 255f);
                pixels[y * Size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.SetPixels32(new Il2CppStructArray<Color32>(pixels));
        tex.Apply();
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    // ---- 距離関数 (負 = 図形の内側) ----

    private static float Min(float a, float b) => Mathf.Min(a, b);

    private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

    private static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
    {
        var pa = p - a;
        var ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - r;
    }

    private static float Polyline(Vector2 p, float r, params Vector2[] points)
    {
        float d = float.MaxValue;
        for (int i = 0; i < points.Length - 1; i++)
            d = Mathf.Min(d, Segment(p, points[i], points[i + 1], r));
        return d;
    }

    /// <summary>n 芒星。外側の半径 outer、くびれの半径 inner、回転 rotation 度</summary>
    private static float Star(Vector2 p, int n, float outer, float inner, float rotation)
    {
        var v = new Vector2[n * 2];
        for (int i = 0; i < n * 2; i++)
        {
            float ang = (rotation + 90f + i * 180f / n) * Mathf.Deg2Rad;
            float r = i % 2 == 0 ? outer : inner;
            v[i] = new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
        }
        return Polygon(p, v);
    }

    /// <summary>多角形までの符号付き距離 (Inigo Quilez の sdPolygon)</summary>
    private static float Polygon(Vector2 p, params Vector2[] v)
    {
        float d = Vector2.Dot(p - v[0], p - v[0]);
        float s = 1f;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            var e = v[j] - v[i];
            var w = p - v[i];
            var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
            d = Mathf.Min(d, Vector2.Dot(b, b));
            bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * Mathf.Sqrt(d);
    }
}

/// <summary>ゲームのバトル UI が出ているか。UIController.ActivateBattleUI を捕まえて記録する</summary>
internal static class BattleUi
{
    /// <summary>このセッションで ActivateBattleUI が一度でも呼ばれたか</summary>
    public static bool Observed;
    public static bool Active;
}

// void UIController.ActivateBattleUI(bool flag)
[HarmonyLib.HarmonyPatch(typeof(UIController), nameof(UIController.ActivateBattleUI))]
internal static class BattleUiPatch
{
    private static void Postfix(bool flag)
    {
        BattleUi.Observed = true;
        BattleUi.Active = flag;
    }
}

/// <summary>
/// HUD の出し入れのフェード。表示条件がそろってから少し待ってフェードインし、消えるときは素早くフェードアウトする。
/// (ロード明けの一瞬や画面の切り替わりでチラつかないように)
/// Update は 1 フレームに 1 回 (モジュールの OnUpdate で) 呼び、描画では Alpha を使う。
/// </summary>
internal sealed class HudFader
{
    private const float ShowDelay = 0.4f;
    private const float FadeIn = 0.3f;
    private const float FadeOut = 0.12f;

    private float _visibleFor;

    public float Alpha { get; private set; }

    public void Update(bool visible)
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        _visibleFor = visible ? _visibleFor + dt : 0f;
        bool show = visible && _visibleFor >= ShowDelay;
        Alpha = Mathf.MoveTowards(Alpha, show ? 1f : 0f, dt / (show ? FadeIn : FadeOut));
    }

    /// <summary>GUI.color の透明度に Alpha を掛けて draw を実行する (IMGUI の描画すべてに効く)</summary>
    public void Draw(Action draw)
    {
        if (Alpha <= 0.001f) return;
        var prev = GUI.color;
        GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * Alpha);
        try { draw(); }
        finally { GUI.color = prev; }
    }
}

/// <summary>
/// HUD を出してよい場面かの判定。1 フレームに 1 回だけ調べる。
///   OnField        … プレイヤーがいて、ウィンドウ (ショップ等) が開いておらず、ポーズ中でもない
///   GameHudVisible … ゲーム自身のプレイヤー HP バーが画面に出ている
/// </summary>
internal static class FieldState
{
    /// <summary>HUD の表示条件の選択肢 (設定の Show)</summary>
    public static readonly string[] ShowModes = { "GameHud", "Field", "Always" };
    public const string ShowModeDescription =
        "GameHud: ゲームの HP バーが出ているときだけ / Field: 戦闘フィールドにいるとき / Always: 常に";

    private static int _frame = -1;
    private static bool _onField, _hudVisible;

    public static bool OnField
    {
        get
        {
            Refresh();
            return _onField;
        }
    }

    public static bool GameHudVisible
    {
        get
        {
            Refresh();
            return _hudVisible;
        }
    }

    /// <summary>Show 設定 (ShowModes の番号) に従って、今 HUD を出すか</summary>
    public static bool ShouldShow(int mode) => mode switch
    {
        0 => GameHudVisible && OnField, // ショップなどが HP バーの上に重なる場合もあるので、ウィンドウ判定も併用
        1 => OnField,
        _ => true,
    };

    private static void Refresh()
    {
        if (_frame == Time.frameCount) return;
        _frame = Time.frameCount;
        _onField = !Loading() && CheckField();
        _hudVisible = _onField && CheckHud();
    }

    /// <summary>
    /// ロード中か。ロード中もプレイヤーや HP バーは存在していて、ロード画面の後ろに隠れているだけなので、
    /// 別に調べる必要がある。ゲームのロード中フラグ・シーン読み込み中・ロード画面の表示のどれかで判定
    /// </summary>
    private static bool Loading()
    {
        try
        {
            var util = GameUtil.Instance;
            if (util != null && util.GetInLoading()) return true;

            var loader = SceneLoader.Instance;
            if (loader != null && loader.InSceneLoading()) return true;

            var loadingScreen = UIController.Instance?.m_loadingObject;
            if (loadingScreen != null && loadingScreen.activeInHierarchy) return true;
        }
        catch { }
        return false;
    }

    private static bool CheckField()
    {
        if (PlayerRef.Current == null) return false;
        try
        {
            var ui = UIController.Instance;
            if (ui != null && ui.AnyWindowOpen()) return false;
            var util = GameUtil.Instance;
            if (util != null && util.GetInPause()) return false;
        }
        catch
        {
            // 判定できないときは表示する
        }
        return true;
    }

    /// <summary>
    /// ゲームのプレイヤー HP バー (UIController.m_healthCon) が見えているか。
    /// ゲームが HUD を隠すやり方 (GameObject を消す / Canvas を無効にする / CanvasGroup でフェードする)
    /// のどれでも隠れていると判定する。HP バーが取れないときは表示する扱い。
    /// </summary>
    /// <summary>HUD を隠している理由 (変わったときだけログに出す。表示されない原因を調べるため)</summary>
    private static string _lastReason = "(未判定)"; // 最初の判定結果も必ずログに出す

    private static bool InFight()
    {
        try
        {
            var util = GameUtil.Instance;
            return util != null && (util.InFightScene() || util.IsInBossFight());
        }
        catch { return false; }
    }

    private static bool CheckHud()
    {
        var reason = HiddenReason();
        if (reason != _lastReason)
        {
            _lastReason = reason;
            UiMod.Log?.Info(reason == null ? "HUD: 表示" : $"HUD: 非表示 ({reason})");
        }
        return reason == null;
    }

    /// <summary>ゲームの HUD が隠れている理由。見えているなら null</summary>
    private static string HiddenReason()
    {
        // ゲームがバトル UI を出したか (ActivateBattleUI を捕まえている)
        if (BattleUi.Observed && !BattleUi.Active) return "ActivateBattleUI(false)";
        // まだ一度も呼ばれていない (起動直後のロード画面など) ときは、戦闘ステージにいるときだけ出す
        // (途中で RusK UI を読み込み直したときも、戦闘ステージならすぐに出る)
        if (!BattleUi.Observed && !InFight()) return "バトル UI 未確認 (戦闘ステージ外)";

        try
        {
            var hp = UIController.Instance?.m_healthCon;
            if (hp == null) return null;

            var go = hp.gameObject;
            if (!go.activeInHierarchy) return "HP バーが非アクティブ";

            var canvas = go.GetComponentInParent<Canvas>();
            if (canvas != null && !canvas.enabled) return "Canvas が無効";

            // 出てくる途中 (拡大しながら出る演出) は隠す。
            // Screen Space - Camera のキャンバスでは UI の lossyScale 自体が 0.01 前後と小さいので、キャンバスとの比で見る
            if (canvas != null)
            {
                float canvasScale = canvas.transform.lossyScale.x;
                if (canvasScale > 0f && go.transform.lossyScale.x / canvasScale < 0.05f) return "HP バーが縮んでいる";
            }

            // HP ゲージの画像自体のフェード (Graphic の色、CanvasRenderer の透明度) も見る
            var img = hp.m_healthImage;
            if (img != null)
            {
                if (!img.enabled) return "HP ゲージ画像が無効";
                if (img.color.a < 0.05f) return "HP ゲージ画像が透明 (color)";
                var cr = img.canvasRenderer;
                if (cr != null && cr.GetAlpha() < 0.05f) return "HP ゲージ画像が透明 (CanvasRenderer)";
            }

            // 親の CanvasGroup の透明度を掛け合わせる (ignoreParentGroups があればそこで止める)
            float alpha = 1f;
            var groups = go.GetComponentsInParent<CanvasGroup>();
            if (groups != null)
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    var g = groups[i];
                    if (g == null || !g.enabled) continue;
                    alpha *= g.alpha;
                    if (g.ignoreParentGroups) break;
                }
            }
            return alpha > 0.05f ? null : $"CanvasGroup で透明 (alpha {alpha:0.00})";
        }
        catch
        {
            return null; // 判定できないときは表示する
        }
    }
}
