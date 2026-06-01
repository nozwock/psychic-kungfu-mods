using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using JinGuLib.UI;

namespace JinGuLib;

[BepInAutoPlugin(id: "nozwock.JinGuLib")]
public partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;
    internal new ManualLogSource Logger => base.Logger;

    private Harmony? _harmony;
    private RebindManager? _rebindHandler;

    private void Awake()
    {
        Instance = this;

        Logger.LogInfo($"Setting up {typeof(RebindManager).FullName}...");
        _rebindHandler = new();

        _harmony = new(Id);
        try
        {
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
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

        _harmony?.UnpatchSelf();
        _harmony = null;
    }
}
