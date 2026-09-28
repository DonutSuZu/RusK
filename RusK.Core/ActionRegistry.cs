using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;

namespace RusK.Core;

/// <summary>Mod が登録したアクション (1 回実行する処理) の一覧</summary>
internal sealed class ActionRegistry
{
    private readonly List<ModAction> _actions = new();

    public IReadOnlyList<ModAction> All => _actions;

    public ModAction Register(ModAction action, ModContext owner)
    {
        var id = $"{owner.Info.Id}:{action.Name}";
        if (_actions.Any(a => a.Id == id))
            throw new InvalidOperationException($"Action '{id}' is already registered");

        action.Id = id;
        action.Context = owner;
        _actions.Add(action);
        return action;
    }

    public void UnregisterOwner(ModContext owner) => _actions.RemoveAll(a => a.Context == owner);
}
