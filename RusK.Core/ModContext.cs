using System;
using System.IO;
using BepInEx.Logging;
using HarmonyLib;
using RusK.API;

namespace RusK.Core;

internal sealed class ModContext : IModContext, IDisposable
{
    private readonly ManualLogSource _logSource;
    private string _dataDirectory;

    public ModContext(RuskModAttribute info, string sourcePath)
    {
        Info = info;
        SourcePath = sourcePath;
        _logSource = Logger.CreateLogSource($"RusK:{info.Name}");
        Log = new RuskLogger(_logSource);
        Harmony = new Harmony($"rusk.mod.{info.Id}");
    }

    public RuskModAttribute Info { get; }
    public string SourcePath { get; }
    public IRuskMod Instance { get; set; }
    public IRuskLogger Log { get; }
    public Harmony Harmony { get; }

    public string DataDirectory
    {
        get
        {
            if (_dataDirectory == null)
            {
                _dataDirectory = Path.Combine(Rusk.DataDir, Info.Id);
                Directory.CreateDirectory(_dataDirectory);
            }
            return _dataDirectory;
        }
    }

    public void RegisterModule(Module module) => Rusk.Modules.Register(module, this);

    public ModAction RegisterAction(string name, Action callback, string description = "") =>
        Rusk.Actions.Register(new ModAction(name, callback, description), this);

    public void RegisterWindow(RuskWindow window) => Rusk.Ui.Windows.Register(window, this);

    public void Notify(string message, NotifyLevel level = NotifyLevel.Info) =>
        Rusk.Notifications.Push(message, level);

    public void Dispose() => Logger.Sources.Remove(_logSource);
}

internal sealed class RuskLogger : IRuskLogger
{
    private readonly ManualLogSource _source;

    public RuskLogger(ManualLogSource source) => _source = source;

    public void Debug(object message) => _source.LogDebug(message);
    public void Info(object message) => _source.LogInfo(message);
    public void Warning(object message) => _source.LogWarning(message);
    public void Error(object message) => _source.LogError(message);
}
