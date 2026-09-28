using System;
using System.Collections.Generic;
using Cinemachine;
using HarmonyLib;
using RusK.API;
using UnityEngine;
using UnityEngine.Rendering;
using Module = RusK.API.Module;

namespace RusK.Mods.Camera;

/// <summary>
/// 視点変更。ゲームのカメラ (Cinemachine の肩越しカメラ) の距離・肩の位置・高さ・FOV を上書きする。
/// CameraController.LateUpdate の直後に毎フレーム上書きするので、ゲームが値を戻しても効く。
/// パリィや QTE などの演出カメラには手を出さない (設定で全カメラにも効かせられる)。
/// </summary>
[RuskMod("camera", "Camera View", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1872",
    Description = "視点変更 (近い肩越し / 真後ろ / 一人称 / カスタム)")]
public sealed class CameraMod : RuskMod
{
    protected override void OnLoad()
    {
        CameraViewModule.Instance = new CameraViewModule();
        Context.RegisterModule(CameraViewModule.Instance);
        Context.RegisterAction("CycleView", () => CameraViewModule.Instance?.CycleView(), "視点を順に切り替える");
        Context.Harmony.PatchAll(typeof(CameraMod).Assembly);
    }

    protected override void OnUnload()
    {
        CameraOverride.RestoreAll();
        CameraViewModule.Instance = null;
    }
}

public sealed class CameraViewModule : Module
{
    internal static CameraViewModule Instance;

    public static readonly string[] Views = { "Shoulder", "Behind", "FirstPerson", "Custom" };

    public CameraViewModule() : base("CameraView", Categories.Visual, "視点を変える (近い肩越し / 真後ろ / 一人称 / カスタム)")
    {
        View = AddSetting(new ModeSetting("View", Views, 0,
            "Shoulder: 近い肩越し / Behind: 真後ろ / FirstPerson: 一人称 / Custom: 下の値を自由に"));
        Transition = AddSetting(new FloatSetting("Transition", 0.35f, 0f, 2f, 0.05f, "0.00s", "視点が切り替わるまでの時間"));
        GameplayOnly = AddSetting(new BoolSetting("GameplayCamsOnly", true,
            "通常のプレイ用カメラだけに効かせる (パリィ・QTE・ボス登場などの演出カメラはそのまま)"));
        EyeHeight = AddSetting(new FloatSetting("EyeHeight", 1.40f, -1f, 3f, 0.05f, "0.00",
            "一人称: 目の高さ (カメラの追従点からの高さ)。キャラごとに覚える"));
        PerCharacter = AddSetting(new BoolSetting("PerCharacterEyeHeight", true,
            "一人称の目の高さをキャラごとに覚える (調整したらそのキャラ用に保存し、キャラが替わったら切り替える)"));
        EyeHeight.Changed += OnEyeHeightChanged;
        HideBody = AddSetting(new BoolSetting("HideBodyInFirstPerson", true, "一人称: 自分の体を隠す (影は残す)"));
        FirstPersonFov = AddSetting(new FloatSetting("FirstPersonFov", 70f, 40f, 110f, 1f, "0", "一人称: 視野角 (FOV)"));

        Distance = AddSetting(new FloatSetting("CustomDistance", 3f, 0f, 12f, 0.1f, "0.0", "Custom: カメラの距離"));
        ShoulderX = AddSetting(new FloatSetting("CustomShoulderX", 0.5f, -2f, 2f, 0.05f, "0.00", "Custom: 肩の左右 (+ で右)"));
        ShoulderY = AddSetting(new FloatSetting("CustomShoulderY", 0.3f, -1f, 3f, 0.05f, "0.00", "Custom: 肩の高さ"));
        Arm = AddSetting(new FloatSetting("CustomHeight", 0.3f, -1f, 3f, 0.05f, "0.00", "Custom: 腕の長さ (上方向の持ち上げ)"));
        Fov = AddSetting(new FloatSetting("CustomFov", 50f, 20f, 110f, 1f, "0", "Custom: 視野角 (FOV)"));
    }

    public ModeSetting View { get; }
    public FloatSetting Transition { get; }
    public BoolSetting GameplayOnly { get; }
    public FloatSetting EyeHeight { get; }
    public BoolSetting PerCharacter { get; }
    public BoolSetting HideBody { get; }

    // ---- キャラごとの目の高さ (RusK/data/camera/eye_heights.txt に「キャラID=高さ」で保存)

    private readonly Dictionary<double, float> _eyeHeights = new();
    private bool _eyeLoaded;
    private bool _applyingEye;
    private double _eyeCharacter = double.NaN;

    /// <summary>キャラが替わったら、そのキャラ用に覚えた目の高さに切り替える (毎フレーム呼ばれる)</summary>
    internal void SyncEyeHeight(double characterId)
    {
        if (!PerCharacter.Value || characterId.Equals(_eyeCharacter)) return;
        _eyeCharacter = characterId;
        LoadEyeHeights();
        if (_eyeHeights.TryGetValue(characterId, out var h))
        {
            _applyingEye = true;
            EyeHeight.Value = h;
            _applyingEye = false;
        }
    }

    private void OnEyeHeightChanged()
    {
        // キャラ切り替えで値を入れたときは保存しない。ユーザーが調整したときだけ、今のキャラ用として保存
        if (_applyingEye || !PerCharacter.Value || double.IsNaN(_eyeCharacter)) return;
        LoadEyeHeights();
        _eyeHeights[_eyeCharacter] = EyeHeight.Value;
        SaveEyeHeights();
    }

    private string EyeFile => Context == null ? null : System.IO.Path.Combine(Context.DataDirectory, "eye_heights.txt");

    private void LoadEyeHeights()
    {
        if (_eyeLoaded || EyeFile == null) return;
        _eyeLoaded = true;
        try
        {
            if (!System.IO.File.Exists(EyeFile)) return;
            foreach (var line in System.IO.File.ReadAllLines(EyeFile))
            {
                var parts = line.Split('=');
                if (parts.Length == 2 &&
                    double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var id) &&
                    float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
                    _eyeHeights[id] = h;
            }
        }
        catch (Exception e)
        {
            Context?.Log.Warning($"CameraView: eye_heights の読み込みに失敗: {e.Message}");
        }
    }

    private void SaveEyeHeights()
    {
        if (EyeFile == null) return;
        try
        {
            var lines = new List<string>();
            foreach (var kv in _eyeHeights)
                lines.Add(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=" +
                          kv.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            System.IO.File.WriteAllLines(EyeFile, lines);
        }
        catch (Exception e)
        {
            Context?.Log.Warning($"CameraView: eye_heights の保存に失敗: {e.Message}");
        }
    }
    public FloatSetting FirstPersonFov { get; }
    public FloatSetting Distance { get; }
    public FloatSetting ShoulderX { get; }
    public FloatSetting ShoulderY { get; }
    public FloatSetting Arm { get; }
    public FloatSetting Fov { get; }

    public override string Suffix => View.Selected;

    public void CycleView()
    {
        if (!Enabled) Enabled = true;
        else View.Next();
        Context?.Notify(L.T("視点: {0}", View.Selected));
    }

    public override void OnDisable() => CameraOverride.RestoreAll();

    /// <summary>今の視点の目標値</summary>
    internal ViewValues Target => View.Value switch
    {
        0 => new ViewValues(2.2f, new Vector3(0.65f, 0.15f, 0f), 0.35f, 50f),  // 近い肩越し
        1 => new ViewValues(3.6f, new Vector3(0f, 0.3f, 0f), 0.45f, 55f),      // 真後ろ
        2 => new ViewValues(0f, new Vector3(0f, EyeHeight.Value, 0.12f), 0f, FirstPersonFov.Value), // 一人称
        _ => new ViewValues(Distance.Value, new Vector3(ShoulderX.Value, ShoulderY.Value, 0f), Arm.Value, Fov.Value),
    };

    internal bool FirstPerson => View.Value == 2;
}

internal struct ViewValues
{
    public float Distance;
    public Vector3 Shoulder;
    public float Arm;
    public float Fov;

    public ViewValues(float distance, Vector3 shoulder, float arm, float fov)
    {
        Distance = distance;
        Shoulder = shoulder;
        Arm = arm;
        Fov = fov;
    }

    public static ViewValues Lerp(ViewValues a, ViewValues b, float t) => new(
        Mathf.Lerp(a.Distance, b.Distance, t),
        Vector3.Lerp(a.Shoulder, b.Shoulder, t),
        Mathf.Lerp(a.Arm, b.Arm, t),
        Mathf.Lerp(a.Fov, b.Fov, t));
}

/// <summary>カメラの値の上書きと、元の値の保存・復元</summary>
internal static class CameraOverride
{
    // 通常のプレイ用カメラ (これ以外は演出カメラとして触らない)
    private static readonly HashSet<CamType> GameplayCams = new()
    {
        CamType.Basic, CamType.Far, CamType.Near, CamType.BossFight, CamType.Cam_NoDamp,
        CamType.Cam_IdleNear, CamType.CamNear_UpDown, CamType.EnemyNear, CamType.EnemyFar,
    };

    private sealed class Original
    {
        public Cinemachine3rdPersonFollow Body;
        public CinemachineVirtualCamera Vcam;
        public ViewValues Values;
        public float NearClip;
    }

    private static readonly Dictionary<IntPtr, Original> Originals = new();
    private static ViewValues _current;
    private static bool _hasCurrent;
    private static IntPtr _lastVcam;
    private static bool _warnedNoBody;

    // 一人称で隠した体 (Renderer → 元の影の設定)
    private static readonly Dictionary<IntPtr, (Renderer renderer, ShadowCastingMode mode)> HiddenRenderers = new();
    private static IntPtr _hiddenPlayer;
    private static float _nextBodyScan;

    public static void Apply(CameraController cc)
    {
        var module = CameraViewModule.Instance;
        if (module is not { Enabled: true } || cc == null) return;

        var cam = cc.m_curCam;
        var vcam = cam?.cam;
        bool gameplay = cam != null && GameplayCams.Contains(cam.camType);
        if (vcam == null || (module.GameplayOnly.Value && !gameplay))
        {
            // 演出カメラ中は一人称の体隠しも解除 (演出でキャラが映るように)
            ShowBody();
            _hasCurrent = false;
            return;
        }

        // 一人称の目の高さをキャラに合わせる
        if (module.FirstPerson)
        {
            try
            {
                var player = GameUtil.Instance?.GetPlayer();
                if (player != null) module.SyncEyeHeight(player.GetPlayerId());
            }
            catch { }
        }

        var body = vcam.GetCinemachineComponent(CinemachineCore.Stage.Body)?.TryCast<Cinemachine3rdPersonFollow>();
        if (body == null)
        {
            if (!_warnedNoBody)
            {
                _warnedNoBody = true;
                module.Context?.Log.Warning($"CameraView: カメラ {cam.camType} は肩越しカメラ (3rdPersonFollow) ではないので変更できません");
            }
            return;
        }

        // 初めて触るカメラは元の値を覚えておく
        if (!Originals.ContainsKey(vcam.Pointer))
        {
            var lens0 = vcam.m_Lens;
            Originals[vcam.Pointer] = new Original
            {
                Body = body,
                Vcam = vcam,
                Values = new ViewValues(body.CameraDistance, body.ShoulderOffset, body.VerticalArmLength, lens0.FieldOfView),
                NearClip = lens0.NearClipPlane,
            };
        }

        // カメラが切り替わったら、そのカメラの今の値から目標へ移っていく
        if (!_hasCurrent || _lastVcam != vcam.Pointer)
        {
            _current = new ViewValues(body.CameraDistance, body.ShoulderOffset, body.VerticalArmLength, vcam.m_Lens.FieldOfView);
            _lastVcam = vcam.Pointer;
            _hasCurrent = true;
        }

        float duration = module.Transition.Value;
        float t = duration <= 0f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f / duration);
        _current = ViewValues.Lerp(_current, module.Target, t);

        body.CameraDistance = _current.Distance;
        body.ShoulderOffset = _current.Shoulder;
        body.VerticalArmLength = _current.Arm;

        var lens = vcam.m_Lens;
        lens.FieldOfView = _current.Fov;
        lens.NearClipPlane = module.FirstPerson ? 0.05f : Originals[vcam.Pointer].NearClip;
        vcam.m_Lens = lens;

        if (module.FirstPerson && module.HideBody.Value) HideBody();
        else ShowBody();
    }

    /// <summary>全カメラを元の値に戻し、体の表示も戻す</summary>
    public static void RestoreAll()
    {
        foreach (var o in Originals.Values)
        {
            try
            {
                if (o.Body == null || o.Vcam == null) continue;
                o.Body.CameraDistance = o.Values.Distance;
                o.Body.ShoulderOffset = o.Values.Shoulder;
                o.Body.VerticalArmLength = o.Values.Arm;
                var lens = o.Vcam.m_Lens;
                lens.FieldOfView = o.Values.Fov;
                lens.NearClipPlane = o.NearClip;
                o.Vcam.m_Lens = lens;
            }
            catch { }
        }
        Originals.Clear();
        _hasCurrent = false;
        ShowBody();
    }

    /// <summary>一人称: 自分の体を「影だけ」にする</summary>
    private static void HideBody()
    {
        PlayerController player = null;
        try { player = GameUtil.Instance?.GetPlayer(); } catch { }
        if (player == null) return;
        RuskShared.Set("camera.hideBody", true); // Custom Model が VRM も隠す

        // キャラが切り替わったら、前のキャラの体を戻してから隠し直す
        if (_hiddenPlayer != player.Pointer)
        {
            ShowBody();
            _hiddenPlayer = player.Pointer;
            _nextBodyScan = 0f;
        }

        // 体の部品を探すのは重いので、キャラが変わったときと 1 秒に 1 回 (武器の持ち替えなど) だけ
        if (Time.unscaledTime < _nextBodyScan) return;
        _nextBodyScan = Time.unscaledTime + 1f;

        foreach (var r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || HiddenRenderers.ContainsKey(r.Pointer)) continue;
            HiddenRenderers[r.Pointer] = (r, r.shadowCastingMode);
            r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        }
    }

    private static void ShowBody()
    {
        foreach (var (renderer, mode) in HiddenRenderers.Values)
        {
            try { if (renderer != null) renderer.shadowCastingMode = mode; }
            catch { }
        }
        HiddenRenderers.Clear();
        _hiddenPlayer = IntPtr.Zero;
        RuskShared.Set("camera.hideBody", false);
    }
}

// void CameraController.LateUpdate()  … ゲームがカメラを動かした直後に上書きする
[HarmonyPatch(typeof(CameraController), nameof(CameraController.LateUpdate))]
internal static class CameraLateUpdatePatch
{
    private static void Postfix(CameraController __instance)
    {
        try { CameraOverride.Apply(__instance); }
        catch (Exception e) { CameraViewModule.Instance?.Context?.Log.Warning($"CameraView: {e.Message}"); }
    }
}
