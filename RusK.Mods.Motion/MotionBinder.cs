using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RusK.Mods.Shared;
using UnityEngine;

namespace RusK.Mods.Motion;

/// <summary>
/// glb の動きを、ゲームの動作 (Idle・攻撃など) に割り当てて再生する。
/// 割り当ては「キャラの ID / ゲームの動作の名前 = glb のファイル#アニメーションの名前」(RusK/data/motion/bindings.txt)。
/// 操作キャラが割り当てのある動作をしている間だけ再生し (フェードで出入り)、
/// ループする動作は自分の時計で、1 回きりの動作はゲームの動作の進み具合に合わせる。
/// 攻撃の動作では「動きの当たる瞬間 (秒)」をゲームの攻撃判定の出る瞬間 (AttackBoxOn のイベント) に合わせる:
/// 当たる瞬間までは時間を伸び縮みさせ、その後は実際の速さで再生する。当たる瞬間は自動 (手が一番速く動く瞬間) か、
/// 画面で決めた秒 (bindings.txt の値の "@秒")
/// </summary>
internal static class MotionBinder
{
    /// <summary>"キャラの ID/動作の名前" → "ファイル#アニメーション"</summary>
    private static readonly Dictionary<string, string> Bindings = new();
    private static bool _loaded;
    private static float _nextWatch;
    private static long _stamp;
    private static readonly HashSet<string> Warned = new();

    // 再生中の割り当て (キャラの根元ごと)
    private sealed class Current
    {
        public string Key;
        public MotionPlayer Player;
        public float? GameHit;     // ゲームの攻撃判定の出る瞬間 (進み具合 0〜1)
        public float Hit;          // 動きの当たる瞬間 (秒)
        public float ClipSeconds;  // ゲームの動作の実際の長さ (秒)
    }
    private static readonly Dictionary<IntPtr, Current> Playing = new();

    private static string FilePath => Path.Combine(MotionMod.Ctx.DataDirectory, "bindings.txt");

    public static string Key(long id, string motion) => $"{id}/{motion}";

    public static string Get(long id, string motion)
    {
        Load();
        return Bindings.TryGetValue(Key(id, motion), out var v) ? v : null;
    }

    public static void Set(long id, string motion, GltfMotion glb)
    {
        Load();
        var key = Key(id, motion);
        if (glb == null) Bindings.Remove(key);
        else Bindings[key] = MotionLibrary.KeyOf(glb);
        Save();
    }

    /// <summary>割り当ての値 → (動きのキー, 当たる瞬間の秒。null なら自動)</summary>
    public static (string key, float? hit) Parse(string value)
    {
        if (value == null) return (null, null);
        int at = value.LastIndexOf('@');
        if (at < 0) return (value, null);
        return (value.Substring(0, at),
            float.TryParse(value.Substring(at + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h) ? h : null);
    }

    /// <summary>当たる瞬間を決める (null なら自動に戻す)</summary>
    public static void SetHit(long id, string motion, float? hit)
    {
        Load();
        var key = Key(id, motion);
        if (!Bindings.TryGetValue(key, out var v)) return;
        var (k, _) = Parse(v);
        Bindings[key] = hit is float h ? $"{k}@{h.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}" : k;
        Save();
    }

    /// <summary>ゲームの動作の攻撃判定の出る瞬間 (最初の AttackBoxOn のイベントの進み具合)。攻撃でなければ null</summary>
    public static float? GameHit(MotionState motion)
    {
        if (motion?.animEvent == null) return null;
        float? best = null;
        foreach (var e in motion.animEvent)
        {
            if (e?.callBack == null) continue;
            foreach (var f in e.callBack)
                if (f != null && f.funcName != null && f.funcName.EndsWith("AttackBoxOn") && (best == null || e.playProcess < best))
                    best = e.playProcess;
        }
        return best;
    }

    /// <summary>ゲームの動作の実際の長さ (秒。クリップの長さ ÷ 速さ)</summary>
    public static float ClipSeconds(MotionState motion)
    {
        var clip = motion?.bindAnimClip?.Clip;
        if (clip == null) return 1f;
        return clip.length / Mathf.Max(0.05f, motion.playSpeed);
    }

    /// <summary>bindings.txt を次に使うときに読み直す (手で書き換えたとき用)</summary>
    public static void ReloadBindings() => _loaded = false;

    /// <summary>
    /// motions フォルダの動き・bindings.txt が書き換わったら (Blender で書き出し直したなど)、自動で読み込み直す (1 秒ごとに見る)
    /// </summary>
    private static void Watch()
    {
        if (Time.unscaledTime < _nextWatch) return;
        _nextWatch = Time.unscaledTime + 1f;
        long stamp = 0;
        try
        {
            if (File.Exists(FilePath)) stamp = File.GetLastWriteTimeUtc(FilePath).Ticks;
            if (Directory.Exists(MotionMod.Folder))
                foreach (var f in Directory.GetFiles(MotionMod.Folder, "*.*", SearchOption.AllDirectories))
                    stamp = stamp * 31 + File.GetLastWriteTimeUtc(f).Ticks + f.Length;
        }
        catch { return; }
        if (_stamp != 0 && stamp != _stamp)
        {
            MotionLibrary.Reload();
            _loaded = false;
            Warned.Clear();
            MotionMod.Ctx.Log.Info("Motion: motions フォルダか bindings.txt が変わったので読み込み直しました");
        }
        _stamp = stamp;
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        Bindings.Clear();
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) Bindings[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }
        catch (Exception e) { MotionMod.Ctx.Log.Warning($"Motion: bindings.txt を読めません: {e.Message}"); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(MotionMod.Ctx.DataDirectory);
            File.WriteAllLines(FilePath, Bindings.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"), new UTF8Encoding(false));
        }
        catch (Exception e) { MotionMod.Ctx.Log.Warning($"Motion: bindings.txt を保存できません: {e.Message}"); }
    }

    /// <summary>毎フレーム (動きを写す前に): 操作キャラの今の動作に合わせて、割り当てた動きを出し入れする</summary>
    public static void Update()
    {
        Watch();
        Load();
        if (Bindings.Count == 0 && Playing.Count == 0) return;
        var p = PlayerRef.Current;
        if (p == null) return;
        try
        {
            var root = p.transform;
            if (MotionPlayers.IsManual(root)) return; // 画面から試しに再生している間は割り当てを使わない
            long id = (long)Math.Round(p.GetPlayerId());
            var anim = p.m_animController;
            var motion = anim?.m_animMotion;
            string name = motion?.name;
            string want = name != null && Bindings.TryGetValue(Key(id, name), out var v) ? v : null;

            Playing.TryGetValue(root.Pointer, out var cur);
            string curKey = cur?.Player != null && cur.Player.Target > 0f ? cur.Key : null;
            string wantKey = want != null ? Key(id, name) + "=" + want : null;
            if (wantKey != curKey)
            {
                if (cur?.Player != null) cur.Player.Target = 0f;
                Playing.Remove(root.Pointer);
                var (glbKey, hit) = Parse(want);
                var glb = glbKey != null ? MotionLibrary.Find(glbKey) : null;
                if (glbKey != null && glb == null && Warned.Add(glbKey))
                    MotionMod.Ctx.Log.Warning($"Motion: 割り当て {Key(id, name)} の動き '{glbKey}' が見つかりません (ファイル名#アニメーションの名前 を確かめてください)");
                if (glb != null)
                {
                    var player = MotionPlayers.Play(root, glb, 0.15f);
                    var clip = motion.bindAnimClip?.Clip;
                    player.Loop = clip == null || clip.isLooping;
                    Playing[root.Pointer] = new Current
                    {
                        Key = wantKey, Player = player, GameHit = GameHit(motion),
                        Hit = hit ?? glb.AutoHit, ClipSeconds = ClipSeconds(motion),
                    };
                    cur = Playing[root.Pointer];
                }
            }

            // 1 回きりの動作は、ゲームの動作の進み具合に合わせる
            if (cur?.Player != null && !cur.Player.Loop && anim != null)
            {
                float nt = Mathf.Clamp01(anim.GetCurAnimNormalizedTime());
                float len = cur.Player.Motion.Length;
                float t;
                if (cur.GameHit is float gh && gh > 0.001f)
                {
                    // 当たる瞬間までは伸び縮み、その後は実際の速さ
                    t = nt <= gh ? nt / gh * cur.Hit : cur.Hit + (nt - gh) * cur.ClipSeconds;
                }
                else t = nt * len; // 攻撃判定が無い動作は、全体を合わせる
                cur.Player.Time = Mathf.Min(t, len) - Time.deltaTime; // Apply で deltaTime 進むので先に引いておく
            }
        }
        catch (Exception e) { MotionMod.Ctx.Log.Warning($"Motion: 割り当ての確認に失敗: {e.Message}"); }
    }
}

/// <summary>RusK/motions の glb の動き (読み込みは一度だけ。読み込み直しで作り直す)</summary>
internal static class MotionLibrary
{
    private static List<GltfMotion> _all;

    public static List<GltfMotion> All => _all ??= MotionMod.LoadAll();

    public static void Reload() => _all = MotionMod.LoadAll();

    /// <summary>"motions からの相対パス#アニメーションの名前"</summary>
    public static string KeyOf(GltfMotion m)
    {
        var rel = m.File;
        try { rel = Path.GetRelativePath(MotionMod.Folder, m.File); } catch { }
        return $"{rel}#{m.Name}";
    }

    public static GltfMotion Find(string key) => All.FirstOrDefault(m => KeyOf(m) == key);
}
