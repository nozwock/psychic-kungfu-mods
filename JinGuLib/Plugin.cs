using System;
using BepInEx;
using BepInEx.Logging;
using JinGuLib.UI;

namespace JinGuLib;

[BepInAutoPlugin(id: "nozwock.JinGuLib")]
public partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;
    internal new ManualLogSource Logger => base.Logger;

    private RebindManager? rebindHandler;

    private void Awake()
    {
        Instance = this;

        try
        {
            Logger.LogInfo($"Setting up {typeof(RebindManager).FullName}...");
            rebindHandler = new();
            RebindManager.Instance = rebindHandler;
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
        rebindHandler?.Dispose();
        rebindHandler = null;
        RebindManager.Instance = null;
    }
}
