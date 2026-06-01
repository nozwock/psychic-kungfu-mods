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

    private RebindManager? _rebindHandler;

    private void Awake()
    {
        Instance = this;

        Logger.LogInfo($"Setting up {typeof(RebindManager).FullName}...");
        _rebindHandler = new();

        try
        {
            Logger.LogInfo($"Fixing LoadBindingOverridesFromJsonInternal...");
            Fix_LoadBindingOverridesFromJsonInternal.Apply();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    private void OnApplicationQuit() => Destroy();

    private void OnDestroy() => Destroy();

    private void Destroy()
    {
        _rebindHandler?.Dispose();
        _rebindHandler = null;

        Fix_LoadBindingOverridesFromJsonInternal.Undo();
    }
}
