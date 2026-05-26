using System;
using System.Linq;
using System.Reflection;
using BepInEx;

namespace DifficultyOverhaul;

[BepInAutoPlugin(id: "nozwock.DifficultyOverhaul")]
public partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;

    private HarmonyLib.Harmony? _harmony;

    private void Awake()
    {
        Instance = this;

        _ = ModConfig.Instance;

        _harmony = new(Id);
        try
        {
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        Logger.LogInfo($"Harmony patches applied: {_harmony.GetPatchedMethods().Count()}");
        foreach (var m in _harmony.GetPatchedMethods())
        {
            Logger.LogInfo($"Patched {m.DeclaringType.FullName}.{m.Name}");
        }
    }

    private void OnDestroy()

    {
        _harmony?.UnpatchSelf();
        _harmony = null;
    }
}