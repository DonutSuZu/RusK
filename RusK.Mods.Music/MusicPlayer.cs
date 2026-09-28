using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RusK.API;
using UnityEngine;
using UnityEngine.Networking;
using Random = System.Random;

namespace RusK.Mods.Music;

internal sealed class Track
{
    public string Path;
    public string Name;   // music フォルダからの相対パス (拡張子なし)
    public bool IsBoss;   // "boss" という名前のフォルダの中にある曲
}

/// <summary>
/// 曲の読み込み・再生・フェード・ゲーム BGM のミュートを担当する。
/// 曲は UnityWebRequestMultimedia で読み込む (mp3 / ogg / wav)。再生は自前の AudioSource で、
/// 出力先をゲームの「音楽」ミキサーにするので、ゲーム内の BGM 音量設定がそのまま効く。
/// </summary>
internal sealed class MusicPlayer : IDisposable
{
    private static readonly string[] Extensions = { ".mp3", ".ogg", ".wav" };

    private readonly IModContext _ctx;
    private readonly Random _rng = new();
    private readonly List<Track> _history = new();

    private GameObject _go;
    private AudioSource _src;
    private AudioClip _clip;
    private UnityWebRequest _req;
    private Track _loading;
    private Track _requested;

    private float _gain;
    private bool _paused;
    private bool _started;
    private bool _mutedGame;
    private bool _lastBoss;
    private int _failures;

    public MusicPlayer(IModContext ctx, string folder)
    {
        _ctx = ctx;
        Folder = folder;
        Directory.CreateDirectory(folder);
        Scan();
    }

    public string Folder { get; }
    public List<Track> Tracks { get; private set; } = new();
    public Track Current { get; private set; }
    public bool Loading => _req != null;
    public bool Paused => _paused;
    public float Time => _src != null && _clip != null ? _src.time : 0f;
    public float Length => _clip != null ? _clip.length : 0f;

    // 設定 (MusicModule が毎フレーム入れる)
    public bool Shuffle = true;
    public float Volume = 0.8f;
    public float FadeSeconds = 1.5f;
    public bool UseBossFolder = true;
    public bool NotifyTrack = true;

    public void Scan()
    {
        var list = new List<Track>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(Folder, "*.*", SearchOption.AllDirectories))
            {
                if (!Extensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant())) continue;
                var rel = System.IO.Path.GetRelativePath(Folder, path);
                var dirs = System.IO.Path.GetDirectoryName(rel) ?? "";
                list.Add(new Track
                {
                    Path = path,
                    Name = System.IO.Path.ChangeExtension(rel, null).Replace('\\', '/'),
                    IsBoss = dirs.Split('\\', '/').Any(d => d.Equals("boss", StringComparison.OrdinalIgnoreCase)),
                });
            }
        }
        catch (Exception e)
        {
            _ctx.Log.Error($"Music scan failed: {e.Message}");
        }

        Tracks = list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _failures = 0;
        _ctx.Log.Info($"Music: {Tracks.Count} track(s) in {Folder}");
    }

    /// <summary>毎フレーム呼ぶ。want = 自分の曲を流したい状態か、boss = ボス戦中か</summary>
    public void Tick(bool want, bool boss)
    {
        EnsureSource();
        PollLoading();

        var pool = Pool(boss);
        if (want && pool.Count > 0)
        {
            // ボス戦に入った / 抜けたら、そのプールの曲に切り替える
            bool bossPoolActive = boss && UseBossFolder && Tracks.Any(t => t.IsBoss);
            if (Current != null && _loading == null && Current.IsBoss != bossPoolActive && _lastBoss != boss)
                Start(PickNext(pool));

            if (Current == null && _loading == null && _failures < Tracks.Count)
                Start(PickNext(pool));

            // 曲が終わったら次へ
            if (_clip != null && _started && !_paused && !_src.isPlaying && _loading == null)
                Start(PickNext(pool));
        }
        _lastBoss = boss;

        // フェード
        float target = want && pool.Count > 0 ? 1f : 0f;
        float dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 0.1f);
        _gain = Mathf.MoveTowards(_gain, target, dt / Mathf.Max(0.05f, FadeSeconds));
        if (_src != null) _src.volume = _gain * Volume;

        if (target > 0f || _gain > 0f)
        {
            MuteGame(true);
        }
        else
        {
            // 戦闘が終わってフェードアウトしきったら止める (次の戦闘では別の曲から)
            MuteGame(false);
            if (Current != null || _loading != null) StopAndRelease();
        }
    }

    public void Next()
    {
        if (Current == null && _loading == null) return;
        Start(PickNext(Pool(_lastBoss)));
    }

    public void Previous()
    {
        if (Current == null) return;
        // 3 秒以上再生していたら頭から、そうでなければ 1 つ前の曲
        if (Time > 3f || _history.Count < 2)
        {
            _src.time = 0f;
            return;
        }
        _history.RemoveAt(_history.Count - 1);
        var prev = _history[^1];
        _history.RemoveAt(_history.Count - 1);
        Start(prev);
    }

    public void TogglePause()
    {
        if (_src == null || _clip == null) return;
        if (_paused) { _src.UnPause(); _paused = false; }
        else { _src.Pause(); _paused = true; }
    }

    /// <summary>曲一覧から選んだ曲を再生する。今鳴っていなければ次の戦闘の 1 曲目にする</summary>
    public void Play(Track track)
    {
        _requested = track;
        if (Current != null || _loading != null)
        {
            Start(track);
            _requested = null;
        }
        else
        {
            _ctx.Notify(L.T("次の戦闘で「{0}」から流します", track.Name));
        }
    }

    public void StopAndRelease()
    {
        CancelLoad();
        if (_src != null)
        {
            _src.Stop();
            _src.clip = null;
        }
        if (_clip != null) UnityEngine.Object.Destroy(_clip);
        _clip = null;
        Current = null;
        _started = false;
        _paused = false;
    }

    /// <summary>モジュールを OFF にしたとき: すぐ止めてゲームの BGM を戻す</summary>
    public void StopImmediate()
    {
        StopAndRelease();
        _gain = 0f;
        MuteGame(false);
    }

    public void Dispose()
    {
        StopImmediate();
        if (_go != null) UnityEngine.Object.Destroy(_go);
        _go = null;
        _src = null;
    }

    // ---------------------------------------------------------------------------------

    private List<Track> Pool(bool boss)
    {
        if (UseBossFolder)
        {
            if (boss)
            {
                var bossTracks = Tracks.Where(t => t.IsBoss).ToList();
                if (bossTracks.Count > 0) return bossTracks;
            }
            var normal = Tracks.Where(t => !t.IsBoss).ToList();
            if (normal.Count > 0) return normal;
        }
        return Tracks;
    }

    private Track PickNext(List<Track> pool)
    {
        if (_requested != null && Tracks.Contains(_requested))
        {
            var r = _requested;
            _requested = null;
            return r;
        }
        if (pool.Count == 1) return pool[0];

        if (Shuffle)
        {
            Track t;
            do t = pool[_rng.Next(pool.Count)];
            while (t == Current);
            return t;
        }

        int i = Current == null ? -1 : pool.IndexOf(Current);
        return pool[(i + 1) % pool.Count];
    }

    private void Start(Track track)
    {
        if (track == null) return;
        CancelLoad();
        try
        {
            // ゲーム側にも AudioType という型があるので Unity のものを明示する
            var type = System.IO.Path.GetExtension(track.Path).ToLowerInvariant() switch
            {
                ".ogg" => UnityEngine.AudioType.OGGVORBIS,
                ".wav" => UnityEngine.AudioType.WAV,
                _ => UnityEngine.AudioType.MPEG,
            };
            _req = UnityWebRequestMultimedia.GetAudioClip(new Uri(track.Path).AbsoluteUri, type);
            var handler = _req.downloadHandler?.TryCast<DownloadHandlerAudioClip>();
            if (handler != null) handler.streamAudio = true; // 全部読み込む前に再生を始める
            _req.SendWebRequest();
            _loading = track;
        }
        catch (Exception e)
        {
            Fail(track, e.Message);
        }
    }

    private void PollLoading()
    {
        if (_req == null || !_req.isDone) return;

        var track = _loading;
        try
        {
            if (_req.result == UnityWebRequest.Result.Success)
            {
                var clip = DownloadHandlerAudioClip.GetContent(_req);
                if (clip != null) Begin(track, clip);
                else Fail(track, "clip is null");
            }
            else
            {
                Fail(track, _req.error);
            }
        }
        catch (Exception e)
        {
            Fail(track, e.Message);
        }
        finally
        {
            _req?.Dispose();
            _req = null;
            _loading = null;
        }
    }

    private void Begin(Track track, AudioClip clip)
    {
        _src.Stop();
        if (_clip != null) UnityEngine.Object.Destroy(_clip);
        _clip = clip;
        _clip.name = track.Name;
        _clip.hideFlags = HideFlags.HideAndDontSave;
        _src.clip = _clip;
        _src.Play();

        Current = track;
        _started = true;
        _paused = false;
        _failures = 0;
        _history.Add(track);
        if (_history.Count > 50) _history.RemoveAt(0);

        if (NotifyTrack) _ctx.Notify($"♪ {track.Name}");
    }

    private void Fail(Track track, string reason)
    {
        _failures++;
        _ctx.Log.Warning($"Music: '{track?.Name}' を再生できません: {reason}");
        if (_failures >= Math.Max(1, Tracks.Count))
            _ctx.Notify(L.T("曲を再生できませんでした (ログを確認)"), NotifyLevel.Error);
        Current = null; // 次の Tick で別の曲を試す
    }

    private void CancelLoad()
    {
        if (_req == null) return;
        try
        {
            _req.Abort();
            _req.Dispose();
        }
        catch { }
        _req = null;
        _loading = null;
    }

    private void EnsureSource()
    {
        if (_src == null)
        {
            _go = new GameObject("RusK_Music");
            _go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_go);
            _src = _go.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = false;
            _src.spatialBlend = 0f;
            _src.priority = 0;
            _src.volume = 0f;
        }

        // ゲームの「音楽」ミキサーに流す (シーンが変わって AudioPlayer が作り直されても付け直す)
        if (_src.outputAudioMixerGroup == null)
        {
            try
            {
                var mixer = AudioPlayer.Instance?.m_musicMixer;
                if (mixer != null) _src.outputAudioMixerGroup = mixer;
            }
            catch { }
        }
    }

    private void MuteGame(bool mute)
    {
        try
        {
            var bgm = AudioPlayer.Instance?.m_player;
            if (bgm == null) return;
            if (mute)
            {
                bgm.mute = true; // ゲームが作り直しても毎フレーム付け直す
                _mutedGame = true;
            }
            else if (_mutedGame)
            {
                bgm.mute = false;
                _mutedGame = false;
            }
        }
        catch { }
    }
}
