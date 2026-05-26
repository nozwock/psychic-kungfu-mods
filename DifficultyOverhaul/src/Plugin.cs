using System;
using System.Linq;
using MelonLoader;

[assembly: MelonInfo(typeof(DifficultyOverhaul.Plugin), "Difficulty Overhaul", "0.0.1", "nozwock")]

namespace DifficultyOverhaul;

public class Plugin : MelonMod
{
    public static readonly string Id = "nozwock.DifficultyOverhaul";

    private HarmonyLib.Harmony? harmony;

    public override void OnInitializeMelon()
    {
        _ = Config.Instance;

        harmony = new(Id);
        try
        {
            harmony.PatchAll(MelonAssembly.Assembly);
        }
        catch (Exception e)
        {
            MelonLogger.Msg(e);
        }

        MelonLogger.Msg($"Harmony patches applied: {harmony.GetPatchedMethods().Count()}");
        foreach (var m in harmony.GetPatchedMethods())
        {
            MelonLogger.Msg($"Patched {m.DeclaringType.FullName}.{m.Name}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        harmony?.UnpatchSelf();
        harmony = null;
    }
}