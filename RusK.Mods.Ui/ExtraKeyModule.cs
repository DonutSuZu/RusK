using System;
using RusK.API;
using UnityEngine;
using UnityEngine.InputSystem;
using Module = RusK.API.Module;

namespace RusK.Mods.Ui;

/// <summary>
/// ゲームの入力アクション (既定: 回避 = Dash) にキーを 1 つ追加する。
/// ゲームの Input System のアクションに実行中に割り当てを足すので、ゲームから見ると
/// 元のキー (右クリックなど) と同じ入力として届く。回避の判定はゲーム本来の処理のまま。
/// OFF にすると、追加した割り当てだけを無効にする (ゲームにもともとある割り当てには触らない)。
/// </summary>
public sealed class ExtraKeyModule : Module
{
    internal static ExtraKeyModule Instance;

    // 表示名と Input System のコントロールパス
    private static readonly (string label, string path)[] Keys =
    {
        ("Shift", "<Keyboard>/leftShift"),
        ("Space", "<Keyboard>/space"),
        ("Ctrl", "<Keyboard>/leftCtrl"),
        ("Alt", "<Keyboard>/leftAlt"),
        ("Q", "<Keyboard>/q"),
        ("E", "<Keyboard>/e"),
        ("F", "<Keyboard>/f"),
        ("R", "<Keyboard>/r"),
        ("C", "<Keyboard>/c"),
        ("V", "<Keyboard>/v"),
        ("X", "<Keyboard>/x"),
        ("Z", "<Keyboard>/z"),
        ("MMB", "<Mouse>/middleButton"),
        ("Mouse4", "<Mouse>/backButton"),
        ("Mouse5", "<Mouse>/forwardButton"),
    };

    // 追加する割り当てのグループ。Keyboard&Mouse はゲームの操作方法 (キーボード・マウス) のグループで、
    // RusK は自分で足した割り当てを見分けるための目印
    private const string OurGroup = "RusK";
    private const string Groups = "Keyboard&Mouse;" + OurGroup;

    private readonly ActionChoiceSetting _action;
    private readonly ModeSetting _key;

    // 自分で足した割り当て ("アクション名|パス" → 割り当て番号)。入れ物が作り直されたら空にする
    private readonly System.Collections.Generic.Dictionary<string, int> _ours = new();
    private IntPtr _asset;

    // 今有効にしている割り当て
    private InputAction _applied;
    private string _appliedKey;
    private int _appliedIndex = -1;
    private bool _addedByUs;
    private float _nextCheck;

    public ExtraKeyModule() : base("ExtraKey", Categories.Player, "ゲームの操作 (既定: 回避) にキーを 1 つ追加する")
    {
        Instance = this;
        _action = AddSetting(new ActionChoiceSetting("Action", "Dash", "キーを追加する操作 (Dash = 回避)"));
        _key = AddSetting(new ModeSetting("Key", Array.ConvertAll(Keys, k => k.label), 0,
            "追加するキー (Mouse4 / Mouse5 はマウスのサイドボタン)"));
        _action.Changed += Reapply;
        _key.Changed += Reapply;
    }

    public override bool VisibleInArrayList => true;
    public override string Suffix => KeyLabel;

    internal string ActionName => _action.Value;
    internal string KeyLabel => Keys[_key.Value].label;
    private string KeyPath => Keys[_key.Value].path;

    public override void OnEnable()
    {
        _nextCheck = 0f;
        Apply();
    }

    public override void OnDisable() => Remove();

    public override void OnUpdate()
    {
        // シーン切り替えで PlayerInput (アクションの入れ物) が作り直されるので、ときどき確認する
        if (Time.unscaledTime < _nextCheck) return;
        _nextCheck = Time.unscaledTime + 1f;
        Apply();
    }

    private void Reapply()
    {
        if (!Enabled) return;
        Remove();
        Apply();
    }

    private void Apply()
    {
        InputActionAsset asset = null;
        InputAction action = null;
        try
        {
            asset = InputController.Instance?.GetPlayerInput()?.actions;
            action = asset?.FindAction(ActionName, false);
        }
        catch { }
        if (asset == null || action == null) return;

        // 入れ物が作り直されていたら、以前の記録は無効
        if (asset.Pointer != _asset)
        {
            _asset = asset.Pointer;
            _ours.Clear();
            _applied = null;
        }

        string key = $"{ActionName}|{KeyPath}";
        if (_applied != null && _appliedKey == key) return; // 適用済み

        try
        {
            // 自分で足した割り当ては RusK グループの目印付きなので、それだけを探す。
            // (ゲーム本来の割り当てはパスで探すと、キー設定で上書きされていても元のパスで見つかってしまう。
            //  例: 回避の元の割り当ては leftShift で、キー設定で右クリックに変えてある)
            int index = _ours.TryGetValue(key, out var known)
                ? known
                : InputActionRebindingExtensions.GetBindingIndex(action, OurGroup, KeyPath);

            if (index >= 0)
            {
                // 以前に自分で足して無効にしていたものを戻す
                InputActionRebindingExtensions.RemoveBindingOverride(action, index);
            }
            else
            {
                // 有効なアクションには割り当てを足せないので、いったん止めて足す
                bool wasEnabled = action.enabled;
                if (wasEnabled) action.Disable();
                InputActionSetupExtensions.AddBinding(action, KeyPath, null, null, Groups);
                if (wasEnabled) action.Enable();
                index = InputActionRebindingExtensions.GetBindingIndex(action, OurGroup, KeyPath);
                Context.Log.Info($"ExtraKey: {KeyPath} -> {ActionName} (binding {index})");
            }

            if (index < 0) throw new InvalidOperationException("added binding not found");
            _ours[key] = index;
            _applied = action;
            _appliedKey = key;
            _appliedIndex = index;
            _addedByUs = true;
        }
        catch (Exception e)
        {
            Context.Log.Error($"ExtraKey apply failed: {e}");
            Context.Notify(L.T("キーの追加に失敗しました (ログを確認)"), NotifyLevel.Error);
            Enabled = false;
        }
    }

    /// <summary>自分で足した割り当てだけを無効にする (空のパスで上書き = 何のキーにも反応しない)</summary>
    private void Remove()
    {
        try
        {
            if (_applied != null && _addedByUs && _appliedIndex >= 0)
                InputActionRebindingExtensions.ApplyBindingOverride(_applied, _appliedIndex, "");
        }
        catch (Exception e)
        {
            Context.Log.Warning($"ExtraKey remove failed: {e.Message}");
        }
        _applied = null;
        _appliedKey = null;
    }
}
