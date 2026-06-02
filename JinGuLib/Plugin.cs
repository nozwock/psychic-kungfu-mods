using System;
using BepInEx;
using BepInEx.Logging;
using JinGuLib.Hooks;
using JinGuLib.UI;

namespace JinGuLib;

[BepInAutoPlugin(id: "nozwock.JinGuLib")]
public partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;
    internal new ManualLogSource Logger => base.Logger;

    private RebindUILifecycle? _rebindUI;

    private void Awake()
    {
        Instance = this;

        try
        {
            Logger.LogInfo($"Fixing LoadBindingOverridesFromJsonInternal...");
            Fix_LoadBindingOverridesFromJsonInternal.Apply();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        try
        {
            Logger.LogInfo($"Setting up {nameof(RebindRegistry)}...");
            _rebindUI = new();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    private void OnApplicationQuit() => Shutdown();

    private void OnDestroy() => Shutdown();

    private bool _shutdown;

    private void Shutdown()
    {
        if (_shutdown)
            return;
        _shutdown = true;

        _rebindUI?.Dispose();
        _rebindUI = null;
        RebindRegistry.SaveBindingOverrides();

        Fix_LoadBindingOverridesFromJsonInternal.Undo();
    }
}
