using System.Linq;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(Tweaks.Plugin), "Tweaks", "0.0.1", "nozwock")]

namespace Tweaks;

public class Plugin : MelonMod
{
    private HarmonyLib.Harmony? harmony;

    public override void OnInitializeMelon()
    {
        base.OnInitializeMelon();

        harmony = new("nozwock.tweaks");
        harmony.PatchAll(MelonAssembly.Assembly);

        // TODO: Main Menu Continue seems to not be using m_saveTime for getting the recent save file
        // TODO: Persist Load UI's last tab page opened (FileWindow)
        // TODO: Limit number of max Quicksaves from 60 to something else
        // TODO: (SaveManager.ChangeName) Make quicksave rename renames the filename as well so as to prevent
        // Quicksaving feature from ever overwriting the named quicksave
        // TODO: Don't show maxed out skills in Cultivation training menu

        MelonLogger.Msg($"Harmony patches applied: {harmony.GetPatchedMethods().Count()}");
        foreach (var m in harmony.GetPatchedMethods())
        {
            MelonLogger.Msg($"{m.DeclaringType.FullName}.{m.Name}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        base.OnDeinitializeMelon();

        harmony?.UnpatchSelf();
        harmony = null;

        MelonLogger.Msg("Harmony patches unapplied!");
    }

    [HarmonyPatch]
    private class Patches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveData), nameof(SaveData.JingLiPer), MethodType.Setter)]
        private static void SaveData_set_JingLiPer_Prefix(ref float value)
        {
            value = 1f;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveData), nameof(SaveData.TiLi), MethodType.Setter)]
        private static void SaveData_set_TiLi_Prefix(ref int value)
        {
            value = 999999; // Will get clamped to max value
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveData), nameof(SaveData.FullName), MethodType.Getter)]
        private static bool SaveData_get_FileName_Prefix(SaveData __instance, ref string __result)
        {
            var self = __instance;
            var separator = GameSetting.GetValue(SettingEnum.Language) == 2 ? " " : "";
            __result = self.m_leaderFamily + separator + self.m_leaderName;
            return false;
        }
    }
}
