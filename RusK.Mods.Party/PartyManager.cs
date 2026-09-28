using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RusK.API;
using RusK.Mods.Shared;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Party;

/// <summary>
/// アクティブ3人の中核。控えキャラの作成・切り替え・後処理。
///
/// 試作 (Party Lab) で分かったこと:
///   - 控えは MotionManager.prefabPath を ResourceManager.LoadResouceInactive で作り、SetData して非表示で待たせる
///   - 控えは部屋やステージの移動を越えて残り、HP・バフも保たれる
///   - 切り替えの後処理が必要: 待機の動作に戻す (しないとのけぞりのまま固まる)、カメラの付け替え、
///     HUD の作り直し (1 フレーム後)、敵の「気付き」の引き継ぎ (しないと攻撃されるまで攻撃してこない)
/// </summary>
internal static class PartyManager
{
    public const int MaxCompanions = 2; // 操作中のキャラ + 仲間 2 人 = 3 人

    public static IRuskLogger Log;
    public static IModContext Ctx;

    /// <summary>このランで管理しているキャラ (操作中のキャラと、作った控え)</summary>
    public static readonly List<PlayerController> Members = new();

    /// <summary>一緒に戦う仲間のキャラ ID (保存される)</summary>
    public static readonly List<double> Companions = new();

    public static bool SafeSwitch = true;
    public static float Cooldown = 3f;
    public static bool Verbose; // Party Lab を開いている間は詳しいログを出す

    private static float _lastSwitch = -999f;

    /// <summary>最後に切り替えた時刻 (Time.unscaledTime)</summary>
    public static float LastSwitchTime => _lastSwitch;

    /// <summary>最後に切り替えた時刻 (ゲーム内時間 Time.time。スロー演出の影響を受ける)</summary>
    public static float LastSwitchGameTime = -999f;

    /// <summary>最後に切り替えで出てきたキャラ</summary>
    public static IntPtr LastSwitchedIn;

    public static float CooldownRemaining => Mathf.Max(0f, Cooldown - (Time.unscaledTime - _lastSwitch));

    public static PlayerController Current
    {
        get
        {
            try { return GameUtil.Instance?.GetPlayer(); }
            catch { return null; }
        }
    }

    /// <summary>今が戦闘ステージか (控えを作ったり切り替えたりしてよい場面)</summary>
    public static bool InFight
    {
        get
        {
            try
            {
                var util = GameUtil.Instance;
                return util != null && (util.InFightScene() || util.IsInBossFight());
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>破棄されたキャラを外し、今のキャラを一覧に入れる</summary>
    public static void Refresh()
    {
        Members.RemoveAll(m => m == null);
        var cur = Current;
        if (cur != null && !Members.Any(m => m.Pointer == cur.Pointer)) Members.Insert(0, cur);
    }

    public static void Clear()
    {
        Members.Clear();
        Down.Clear();
        _pending = null;
        _rescueTo = null;
        JustSwitch.Reset();
    }

    // ------------------------------------------------------------------ 戦闘不能と自動交代
    // 操作中のキャラの HP が 0 になったら、ゲームオーバーにせず「戦闘不能」にして、生きている仲間に交代する。
    // 全員が戦闘不能になったときだけ、ゲーム本来の処理 (ゲームオーバー) に任せる。

    /// <summary>戦闘不能のキャラ (このランの間は交代できない)</summary>
    private static readonly HashSet<IntPtr> Down = new();

    public static bool IsDown(PlayerController p) => p != null && Down.Contains(p.Pointer);

    private static PlayerController _rescueTo;
    private static int _rescueFrame;

    /// <summary>
    /// 操作中のキャラが倒れそうなときに呼ぶ。交代できる仲間がいれば戦闘不能にして次のフレームで交代し、true を返す
    /// (呼び出し元はゲーム本来の死亡処理を止める)。いなければ false (ゲームオーバー)
    /// </summary>
    public static bool TryRescue(PlayerController dying)
    {
        if (dying == null || Current?.Pointer != dying.Pointer) return false;
        if (IsDown(dying)) return true; // 交代待ち
        Refresh();
        var next = NextAlive(dying, +1);
        if (next == null) return false;

        Down.Add(dying.Pointer);
        _rescueTo = next;
        _rescueFrame = Time.frameCount + 1; // 被弾処理の途中で消さないよう、次のフレームで交代
        Log?.Info($"Party: {Name(dying)} が戦闘不能 → {Name(next)} に交代");
        Ctx?.Notify($"{Name(dying)} が戦闘不能になりました", NotifyLevel.Warning);
        return true;
    }

    /// <summary>
    /// 控えのキャラの HP が 0 なら満タンにする (戦闘不能ではなく、まだ HP が初期化されていないだけのとき)。
    /// 最大 HP も未設定なら、装備込みの元の最大 HP → キャラの基本 HP の順に設定する。結果を文字列で返す (ログ用)
    /// </summary>
    public static string FillHpIfEmpty(PlayerController p, MotionManager mm = null)
    {
        try
        {
            float cur = p.GetCurHp(), max = p.GetMaxHp();
            if (cur > 0f) return $"HP {cur:0}/{max:0}";
            if (max <= 0f)
            {
                try { p.SetOrinMaxHp(); } catch { }
                max = p.GetMaxHp();
            }
            if (max <= 0f)
            {
                mm ??= FindCharacter(Id(p));
                if (mm != null && mm.hp > 0f) { p.SetMaxHp(mm.hp); max = mm.hp; }
            }
            if (max <= 0f) return $"HP {cur:0}/{max:0} (最大 HP が分からず回復できません)";
            p.SetCurrentHp(max);
            return $"HP {cur:0} → {p.GetCurHp():0}/{p.GetMaxHp():0} (満タンにした)";
        }
        catch (Exception e)
        {
            return $"(HP を設定できません: {e.Message})";
        }
    }

    /// <summary>from から dir 方向にたどって、最初の戦闘不能でないキャラ</summary>
    private static PlayerController NextAlive(PlayerController from, int dir)
    {
        int n = Members.Count;
        int i = Members.FindIndex(m => m != null && m.Pointer == from.Pointer);
        if (i < 0) i = 0;
        for (int k = 1; k < n; k++)
        {
            var m = Members[((i + dir * k) % n + n) % n];
            if (m == null || m.Pointer == from.Pointer || IsDown(m)) continue;
            float hp = 1f;
            try { hp = m.GetCurHp(); } catch { }
            if (hp > 0f) return m;
        }
        return null;
    }

    private static void TickRescue()
    {
        if (_rescueTo == null || Time.frameCount < _rescueFrame) return;
        var next = _rescueTo;
        if (next == null || Switch(next, ignoreCooldown: true, force: true)) { _rescueTo = null; return; }
        _rescueFrame = Time.frameCount + 1; // 失敗したら次のフレームにもう一度
    }

    // ---- 切り替えられなかった理由 (HUD に少しの間出す)
    public static string LastBlockReason;
    public static float LastBlockTime = -999f;

    // ------------------------------------------------------------------ 仲間の選択 (保存)

    private static string CompanionFile => Ctx == null ? null : Path.Combine(Ctx.DataDirectory, "party.txt");

    public static void LoadCompanions()
    {
        Companions.Clear();
        try
        {
            var path = CompanionFile;
            if (path == null || !File.Exists(path)) return;
            foreach (var part in File.ReadAllText(path).Split(','))
                if (double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var id))
                    Companions.Add(id);
        }
        catch (Exception e) { Log?.Warning($"Party: party.txt の読み込みに失敗: {e.Message}"); }
    }

    /// <summary>
    /// このランのリーダー。戦闘中は最初に操作していたキャラ (パーティの先頭)、戦闘外は今のキャラ
    /// (拠点で選んだキャラが次のランのリーダーになる)
    /// </summary>
    public static PlayerController Leader
    {
        get
        {
            if (InFight)
            {
                var first = Members.FirstOrDefault(m => m != null);
                if (first != null) return first;
            }
            return Current;
        }
    }

    private static float _nextSync;
    private static double _stableId = -1;
    private static float _stableSince;

    /// <summary>
    /// 戦闘外で、リーダー (今のキャラ) が仲間の設定に入っていたら外す (1 秒に 1 回)。
    /// リーダーと仲間が同じキャラだと、そのキャラは控えに作られず 2 人パーティになってしまうため
    /// </summary>
    public static void SyncLeader()
    {
        if (Time.unscaledTime < _nextSync) return;
        _nextSync = Time.unscaledTime + 1f;
        if (InFight) return;
        var cur = Current;
        if (cur == null) return;
        double id = Id(cur);

        // 起動直後やロード中は、ゲームが一瞬だけ別のキャラを操作中にすることがある。
        // 同じキャラが 3 秒続いて、ロード中でもなく、画面に出ているときだけ同期する
        bool loading = false;
        try { loading = GameUtil.Instance.GetInLoading() || !cur.gameObject.activeInHierarchy; } catch { loading = true; }
        if (loading || !Same(id, _stableId)) { _stableId = id; _stableSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _stableSince < 3f) return;

        if (!Companions.Any(c => Same(c, id))) return;
        Companions.RemoveAll(c => Same(c, id));
        SaveCompanions();
        Ctx?.Notify($"{Name(cur)} がリーダーなので、仲間から外しました", NotifyLevel.Info);
    }

    public static void SaveCompanions()
    {
        try
        {
            var path = CompanionFile;
            if (path == null) return;
            File.WriteAllText(path, string.Join(",", Companions.Select(id =>
                id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        catch (Exception e) { Log?.Warning($"Party: party.txt の保存に失敗: {e.Message}"); }
    }

    /// <summary>仲間に入れる / 外す。外したキャラが控えにいれば片付ける</summary>
    public static void ToggleCompanion(double id)
    {
        if (InFight)
        {
            Ctx?.Notify("戦闘中は編成を変更できません", NotifyLevel.Warning);
            return;
        }
        if (Companions.Any(c => Same(c, id)))
        {
            Companions.RemoveAll(c => Same(c, id));
            var m = Members.FirstOrDefault(p => p != null && Same(Id(p), id));
            if (m != null && m.Pointer != Current?.Pointer) Despawn(m);
        }
        else if (Companions.Count < MaxCompanions)
        {
            Companions.Add(id);
        }
        else
        {
            Ctx?.Notify($"仲間は {MaxCompanions} 人までです", NotifyLevel.Warning);
            return;
        }
        SaveCompanions();
        _nextAutoSpawn = 0f;
    }

    // ------------------------------------------------------------------ 控えの自動作成

    private static float _nextAutoSpawn;

    /// <summary>戦闘ステージにいる間、選んだ仲間がまだ控えにいなければ作る (1 秒に 1 回確認)</summary>
    public static void AutoSpawn()
    {
        if (Time.unscaledTime < _nextAutoSpawn) return;
        _nextAutoSpawn = Time.unscaledTime + 1f;
        if (!InFight) return;
        if (_firstFightSeen < 0f) _firstFightSeen = Time.unscaledTime;

        var cur = Current;
        if (cur == null) return;
        Refresh();

        foreach (var id in Companions.ToArray())
        {
            if (Members.Any(m => m != null && Same(Id(m), id))) continue;
            var character = FindCharacter(id);
            if (character == null) continue;
            Spawn(character);
        }

        // 控えの HP が 0 のまま (一度も表に出ていないキャラは、ゲームが HP を初期化していない) なら満タンにする
        foreach (var m in Members)
            if (m != null && m.Pointer != cur.Pointer && !IsDown(m)) FillHpIfEmpty(m);

        // 調査用のキャラ一覧は、戦闘の画像が読み込まれた後に一度だけ出す
        if (!_loggedCharacters && Time.unscaledTime - _firstFightSeen > 3f)
        {
            try { LogCharactersOnce(GameUtil.Instance.GetCharacterContainer().characters); } catch { }
        }
    }

    /// <summary>キャラのプレハブを非表示で作り、控えに置く</summary>
    public static PlayerController Spawn(MotionManager character)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cur = Current;
            var go = ResourceManager.Instance.LoadResouceInactive(character.prefabPath);
            if (go == null)
            {
                Log?.Warning($"Party: {character.name} のプレハブを読み込めません ('{character.prefabPath}')");
                return null;
            }

            var player = go.GetComponent<PlayerController>() ?? go.GetComponentInChildren<PlayerController>(true);
            if (player == null)
            {
                Log?.Warning($"Party: '{go.name}' に PlayerController がありません");
                Object.Destroy(go);
                return null;
            }

            if (cur != null) go.transform.SetPositionAndRotation(cur.transform.position, cur.transform.rotation);
            player.SetData(character.name, character.id);
            go.SetActive(false);
            Members.Add(player);
            string hp = FillHpIfEmpty(player, character);
            Log?.Info($"Party: {character.name} を控えに作成 ({sw.ElapsedMilliseconds}ms) {hp}");
            return player;
        }
        catch (Exception e)
        {
            Log?.Error($"Party: {character.name} の作成に失敗: {e}");
            return null;
        }
    }

    public static void Despawn(PlayerController p)
    {
        try
        {
            if (p == null || p.Pointer == Current?.Pointer) return;
            Members.Remove(p);
            Object.Destroy(p.gameObject);
            Log?.Info($"Party: {Name(p)} を片付けました");
        }
        catch (Exception e) { Log?.Error($"Party: 片付けに失敗: {e}"); }
    }

    // ------------------------------------------------------------------ 切り替え

    /// <summary>パーティの中で、今のキャラから dir 番目 (+1: 次, -1: 前) に切り替える</summary>
    public static bool Cycle(int dir)
    {
        Refresh();
        var cur = Current;
        if (cur == null || Members.Count < 2) return false;
        var next = NextAlive(cur, dir);
        if (next == null)
        {
            LastBlockReason = "交代できる仲間がいません";
            LastBlockTime = Time.unscaledTime;
            return false;
        }
        bool just = JustSwitch.IsJustTiming;
        return Switch(next, ignoreCooldown: just && JustSwitch.IgnoreCooldown, just: just);
    }

    /// <summary>今切り替えてはいけない理由 (切り替えてよいなら null)</summary>
    public static string BlockReason(PlayerController cur, bool ignoreCooldown)
    {
        if (!ignoreCooldown && CooldownRemaining > 0f) return $"クールタイム中 (あと {CooldownRemaining:0.0} 秒)";
        if (!SafeSwitch) return null;
        try
        {
            if (Time.timeScale < 0.99f) return "スロー演出中";
            if (cur.IsDashing()) return "回避中";
            if (cur.IsInPerfectDash() || cur.IsPerfectDashTimeSlow()) return "ジャスト回避中";
            if (cur.IsInPerfectDefence() || cur.GetPerfectDefenceTimeSlow()) return "パリィ中";
            if (cur.GetCurHp() <= 0f) return "戦闘不能";
        }
        catch { }
        return null;
    }

    /// <summary>今のキャラの位置に移して表示 → ChangePlayer → 元のキャラを非表示。後処理は次のフレームに</summary>
    public static bool Switch(PlayerController next, bool ignoreCooldown, bool just = false, bool force = false)
    {
        var cur = Current;
        if (next == null || cur == null || next.Pointer == cur.Pointer) return false;

        var blocked = force ? null : BlockReason(cur, ignoreCooldown);
        if (blocked != null)
        {
            LastBlockReason = blocked;
            LastBlockTime = Time.unscaledTime;
            if (Verbose) Log?.Info($"Party: 切り替え保留 ({blocked})");
            return false;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            RememberObservingEnemies();
            CleanupOutgoing(cur);

            next.transform.SetPositionAndRotation(cur.transform.position, cur.transform.rotation);
            next.gameObject.SetActive(true);
            GameUtil.Instance.ChangePlayer(next);
            cur.gameObject.SetActive(false);

            RebindCamera(next);
            RestoreObservingEnemies();

            _pending = next;
            _pendingFrame = Time.frameCount + 1;
            _lastSwitch = Time.unscaledTime;
            LastSwitchGameTime = Time.time;
            LastSwitchedIn = next.Pointer;
            JustSwitch.OnSwitched(next, just);
            if (Verbose) Log?.Info($"Party: {Name(cur)} → {Name(next)} ({sw.ElapsedMilliseconds}ms)");
            return true;
        }
        catch (Exception e)
        {
            Log?.Error($"Party: 切り替えに失敗: {e}");
            return false;
        }
    }

    // ---- 後処理 (新しいキャラの初期化は表示した次のフレームに走るので、その後に整える)

    private static PlayerController _pending;
    private static int _pendingFrame;

    /// <summary>毎フレーム呼ぶ</summary>
    public static void Tick()
    {
        TickRescue();
        if (_pending == null || Time.frameCount < _pendingFrame) return;
        var p = _pending;
        _pending = null;
        try
        {
            if (p == null || Current?.Pointer != p.Pointer) return;
            ResetToIdle(p);
            RebindCamera(p);
            RefreshHud(p);
            RestoreObservingEnemies();
            Observing.Clear();
            if (Verbose) Log?.Info($"Party: 後処理 OK {Name(p)} HP {p.GetCurHp():0}/{p.GetMaxHp():0}");
        }
        catch (Exception e)
        {
            Log?.Error($"Party: 後処理に失敗: {e}");
        }
    }

    /// <summary>
    /// 控えに回るキャラの一時的な状態を解除し、待機の動作に戻す
    /// (のけぞりやガードの途中で非表示にすると、戻したときにその動作のまま固まるため)
    /// </summary>
    private static void CleanupOutgoing(PlayerController p)
    {
        TryDo("時間スロー解除 (回避)", () => p.SetPerfectDashTimeSlow(false));
        TryDo("時間スロー解除 (パリィ)", () => p.SetPerfectDefenceTimeSlow(false));
        TryDo("ガード解除", () => p.SetInDefence(false));
        ResetToIdle(p);
    }

    private static void ResetToIdle(PlayerController p)
    {
        var idle = FindIdleMotion(p);
        if (idle == null) return;
        TryDo("コンボのリセット", () => p.GetMotionController()?.ResetCombo());
        TryDo("待機に戻す", () => p.ChangeMotion(idle, true, 0.1f, default));
    }

    private static readonly Dictionary<IntPtr, string> IdleNames = new();

    /// <summary>そのキャラの待機の動作名 ("Idle" を含み、特殊な待機でない、いちばん素朴な名前)</summary>
    private static string FindIdleMotion(PlayerController p)
    {
        if (IdleNames.TryGetValue(p.Pointer, out var cached)) return cached;
        string best = null;
        try
        {
            var list = p.GetMotionList();
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                var name = list[i]?.name;
                if (string.IsNullOrEmpty(name) || !name.Contains("Idle")) continue;
                if (name.Contains("Long") || name.Contains("Show") || name.Contains("Talk") || name.Contains("Near")) continue;
                if (best == null || name.Length < best.Length) best = name;
            }
        }
        catch { }
        if (best != null) IdleNames[p.Pointer] = best;
        return best;
    }

    /// <summary>カメラの追従を新しいキャラへ付け替える (ChangePlayer だけでは付け替わらないことがある)</summary>
    private static void RebindCamera(PlayerController p)
    {
        var cc = CameraController.Instance;
        if (cc == null) return;
        TryDo("カメラ OnChangePlayer", () => cc.OnChangePlayer(p));
        TryDo("カメラ追従の付け替え", () => cc.BindCameraFollowToCurrentPlayer(false));
        TryDo("追従部品の付け替え", () => cc.m_camFollow?.OnChangePlayer(p));
    }

    /// <summary>HP 周りの HUD (キャラ特有のゲージ・スキル UI・HP・必殺技ゲージ) を作り直す</summary>
    private static void RefreshHud(PlayerController p)
    {
        var ui = UIController.Instance;
        if (ui == null) return;
        var mm = FindCharacter(Id(p));
        if (mm != null)
        {
            TryDo("キャラ特有の HUD", () => ui.InitialCharacterHud(mm));
            TryDo("スキル UI", () =>
            {
                ui.ClearSkillUI();
                try { ui.AddSkillUI(mm); }
                catch { p.InitialSkillUI(); }
            });
        }
        TryDo("HP", () => { ui.SetMaxHpHud(p.GetMaxHp()); ui.SetHpHud(p.GetCurHp()); });
        TryDo("必殺技ゲージ", () => { ui.InitialEnergy(p.GetMaxEnergy()); ui.SetEnergyHud(p.GetCurEnergy()); });
    }

    // ---- 敵の「気付き」の引き継ぎ (切り替えで見失った扱いにならないように)

    private static readonly List<EnemyController> Observing = new();

    private static void RememberObservingEnemies()
    {
        Observing.Clear();
        try
        {
            foreach (var e in Object.FindObjectsOfType<EnemyController>())
                if (e != null && e.gameObject.activeInHierarchy && e.GetObservePlayer()) Observing.Add(e);
        }
        catch { }
    }

    private static void RestoreObservingEnemies()
    {
        foreach (var e in Observing)
        {
            try
            {
                if (e != null && e.gameObject.activeInHierarchy && !e.GetObservePlayer()) e.SetObservePlayer(true);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ ユーティリティ

    public static double Id(PlayerController p)
    {
        try { return p.GetPlayerId(); }
        catch { return -1; }
    }

    public static bool Same(double a, double b) => Math.Abs(a - b) < 0.5;

    public static MotionManager FindCharacter(double id)
    {
        try
        {
            var list = GameUtil.Instance?.GetCharacterContainer()?.characters;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && Same(list[i].id, id)) return list[i];
        }
        catch { }
        return null;
    }

    /// <summary>選べるキャラ (解放済みで、戦闘用のプレハブがあるもの)</summary>
    public static List<MotionManager> Characters()
    {
        var result = new List<MotionManager>();
        try
        {
            var util = GameUtil.Instance;
            var list = util?.GetCharacterContainer()?.characters;
            if (list == null) return result;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m == null || string.IsNullOrEmpty(m.prefabPath)) continue;
                bool unlocked = true;
                try { unlocked = util.IsCharacterUnlock(m.id); } catch { }
                if (unlocked) result.Add(m);
            }

        }
        catch { }
        return result;
    }

    private static bool _loggedCharacters;
    private static float _firstFightSeen = -1f;

    /// <summary>キャラ一覧を一度だけログに出す (同名キャラの見分けなどの調査用)</summary>
    private static void LogCharactersOnce(Il2CppSystem.Collections.Generic.List<MotionManager> list)
    {
        if (_loggedCharacters) return;
        _loggedCharacters = true;
        var sb = new StringBuilder();
        sb.AppendLine("Party: キャラ一覧");
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            if (m == null) continue;
            string unlock = "?";
            try { unlock = GameUtil.Instance.IsCharacterUnlock(m.id) ? "解放" : "未解放"; } catch { }
            sb.AppendLine($"  id {m.id:0} {m.name} / {DisplayName(m)} {unlock} isLock={m.isLock} type={m.crtType} prefab='{m.prefabPath}'");
            if (m.crtType == CharacterType.Character)
            {
                try
                {
                    var show = m.playerShow;
                    sb.AppendLine($"      画像: portrait='{show?.portrait?.name}' show2D='{show?.show2D?.name}' choose2D='{show?.choose2D?.name}' skillUI='{show?.skillUIPath}'");
                }
                catch { }
            }
        }
        LogSprites(sb);
        Log?.Info(sb.ToString());
    }

    /// <summary>調査用: 読み込まれている画像のうち、キャラの顔に関係しそうな名前を並べる (Kiki の顔を探す)</summary>
    private static void LogSprites(StringBuilder sb)
    {
        try
        {
            var names = new SortedSet<string>();
            foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                var n = sp?.name;
                if (string.IsNullOrEmpty(n)) continue;
                var l = n.ToLowerInvariant();
                if (l.Contains("light") || l.Contains("kiki") || l.Contains("portrait") || l.Contains("head") ||
                    l.Contains("avatar") || l.Contains("1002") || l.Contains("icon_c"))
                    names.Add(n);
            }
            sb.AppendLine($"  顔に関係しそうな画像 ({names.Count} 個): {string.Join(", ", names.Take(150))}");
        }
        catch (Exception e) { sb.AppendLine($"  (画像の一覧を取れません: {e.Message})"); }
    }

    public static string DisplayName(MotionManager m) => CharacterNames.Get(m);

    public static string Name(PlayerController p)
    {
        if (p == null) return "(なし)";
        var mm = FindCharacter(Id(p));
        return mm != null ? DisplayName(mm) : $"#{Id(p):0}";
    }

    private static void TryDo(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { if (Verbose) Log?.Warning($"Party: {what} に失敗: {e.Message}"); }
    }

    /// <summary>Party Lab 用: 今の状態をログに出す</summary>
    public static void LogState(string label)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Party ===== 状態: {label} =====");
        try
        {
            sb.AppendLine($"  操作中: {Name(Current)}  戦闘中={InFight}  クールタイム残り {CooldownRemaining:0.0}s");
            foreach (var m in Members)
            {
                if (m == null) continue;
                sb.AppendLine($"  - {Name(m)} active={m.gameObject.activeSelf} HP {m.GetCurHp():0}/{m.GetMaxHp():0}" +
                              $" 必殺 {m.GetCurEnergyPercent() * 100:0}% 動作 '{m.GetCurMotion()?.name}'");
            }
            sb.AppendLine($"  仲間の設定: {string.Join(", ", Companions.Select(id => DisplayName(FindCharacter(id))))}");
        }
        catch (Exception e)
        {
            sb.AppendLine($"  (記録中にエラー: {e.Message})");
        }
        Log?.Info(sb.ToString());
    }
}
