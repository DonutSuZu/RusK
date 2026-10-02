using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RusK.API;
using UnityEngine;
using UnityEngine.Networking;

namespace RusK.Mods.Voice;

/// <summary>
/// RusK\voices の音声ファイル (ogg / wav / mp3) を読み込んで、ゲームの音声の名前で引けるようにしておく。
/// - ファイル名 (拡張子なし) = 置き換えるゲームの音声の名前。サブフォルダは自由 (キャラごとに分けるなど)
/// - "名前#1.ogg"・"名前#2.ogg" のように # の後ろを変えて複数置くと、鳴るたびにランダムに選ぶ
/// ゲームの音は鳴らす瞬間に差し替えるので、起動時に全部読み込んでおく (1 つずつ順番に)
/// </summary>
internal sealed class VoiceBank
{
    private static readonly string[] Extensions = { ".ogg", ".wav", ".mp3" };

    private readonly IModContext _ctx;
    private readonly Dictionary<string, List<AudioClip>> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _pending = new();
    private readonly System.Random _random = new();
    private UnityWebRequest _req;
    private string _loading;
    private int _total, _failed;

    public VoiceBank(IModContext ctx, string folder)
    {
        _ctx = ctx;
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>読み込み済みの、置き換える音声の名前の数</summary>
    public int Count => _clips.Count;

    public bool Loading => _req != null || _pending.Count > 0;

    /// <summary>フォルダを探し直して、全部読み込み直す</summary>
    public void Scan()
    {
        Clear();
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var path in Directory.EnumerateFiles(Folder, "*.*", SearchOption.AllDirectories)
                         .Where(p => Extensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                _pending.Enqueue(path);
        }
        catch (Exception e)
        {
            _ctx.Log.Warning($"Voice: フォルダを読めません: {e.Message}");
        }
        _total = _pending.Count;
        _failed = 0;
    }

    /// <summary>毎フレーム呼ぶ。読み込みを 1 つずつ進める</summary>
    public void Tick()
    {
        if (_req != null)
        {
            if (!_req.isDone) return;
            Finish();
        }
        if (_pending.Count == 0) return;

        _loading = _pending.Dequeue();
        try
        {
            // ゲーム側にも AudioType という型があるので Unity のものを明示する
            var type = Path.GetExtension(_loading).ToLowerInvariant() switch
            {
                ".ogg" => UnityEngine.AudioType.OGGVORBIS,
                ".wav" => UnityEngine.AudioType.WAV,
                _ => UnityEngine.AudioType.MPEG,
            };
            _req = UnityWebRequestMultimedia.GetAudioClip(new Uri(_loading).AbsoluteUri, type);
            _req.SendWebRequest();
        }
        catch (Exception e)
        {
            Fail(e.Message);
            _req = null;
        }
    }

    private void Finish()
    {
        try
        {
            if (_req.result != UnityWebRequest.Result.Success) { Fail(_req.error); return; }
            var clip = DownloadHandlerAudioClip.GetContent(_req);
            if (clip == null) { Fail("clip is null"); return; }

            var key = KeyOf(_loading);
            clip.name = "RusK:" + Path.GetFileNameWithoutExtension(_loading);
            // シーンの切り替えで「使われていないアセット」として消されないようにする
            clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (!_clips.TryGetValue(key, out var list)) _clips[key] = list = new List<AudioClip>();
            list.Add(clip);
        }
        catch (Exception e)
        {
            Fail(e.Message);
        }
        finally
        {
            _req.Dispose();
            _req = null;
            if (_pending.Count == 0 && _total > 0)
            {
                var msg = L.T("ボイスを {0} 個読み込みました", _total - _failed);
                if (_failed > 0) msg += L.T(" ({0} 個は読めませんでした)", _failed);
                _ctx.Notify(msg);
                _ctx.Log.Info("Voice: " + msg);
            }
        }
    }

    private void Fail(string error)
    {
        _failed++;
        _ctx.Log.Warning($"Voice: {Path.GetFileName(_loading)} を読めません: {error}");
    }

    /// <summary>ゲームの音声の名前に対応する、置き換え用の音声 (無ければ null)</summary>
    public AudioClip Find(string gameClipName)
    {
        if (string.IsNullOrEmpty(gameClipName) || !_clips.TryGetValue(gameClipName, out var list) || list.Count == 0) return null;
        var clip = list.Count == 1 ? list[0] : list[_random.Next(list.Count)];
        return clip != null ? clip : null; // Unity 側で壊れていたら null
    }

    /// <summary>"名前#2.ogg" → "名前"</summary>
    private static string KeyOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        int hash = name.IndexOf('#');
        return hash > 0 ? name.Substring(0, hash) : name;
    }

    public void Clear()
    {
        _req?.Dispose();
        _req = null;
        _pending.Clear();
        foreach (var list in _clips.Values)
        foreach (var clip in list)
            if (clip != null) UnityEngine.Object.Destroy(clip);
        _clips.Clear();
    }
}
