using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

[assembly: MelonInfo(typeof(Tweaks.Plugin), "Tweaks", "0.0.1", "nozwock")]

namespace Tweaks;

public class Plugin : MelonMod
{
    private HarmonyLib.Harmony? harmony;

    public override void OnInitializeMelon()
    {
        base.OnInitializeMelon();

        harmony = new("nozwock.tweaks");
        try
        {
            harmony.PatchAll(MelonAssembly.Assembly);
        }
        catch (Exception e)
        {
            MelonLogger.Msg(e);
        }

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

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Load), [typeof(string), typeof(string)])]
        private class Patch_PrioritizeSaveFilename
        {
            private static readonly Regex managedSaveRegex = new("^(?:Fixed(\\d+)|Quick(\\d+)|AutoSave\\d+)$");

            private static SaveData UpdateSaveName(SaveData saveData, string parent, string path)
            {
                var filename = Path.GetFileNameWithoutExtension(path);
                if (saveData.m_name != filename && !managedSaveRegex.IsMatch(saveData.m_name))
                {
                    var filepath = Path.Combine(parent, path);
                    saveData.m_path = filepath;
                    saveData.m_name = filename;

                    File.WriteAllBytes(
                        saveData.m_path,
                        MonoSingleton<SaveManager>.Instance.Encrypt(JsonUtility.ToJson(saveData)));
                }

                return saveData;
            }

            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var saveDataFromJsonMethod = AccessTools.Method(
                    typeof(JsonUtility),
                    nameof(JsonUtility.FromJson),
                    [typeof(string)]
                ).MakeGenericMethod(typeof(SaveData));

                var updateSaveNameMethod = AccessTools.Method(
                    typeof(Patch_PrioritizeSaveFilename),
                    nameof(UpdateSaveName)
                );

                foreach (var code in instructions)
                {
                    yield return code;

                    if (code.Calls(saveDataFromJsonMethod))
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        yield return new CodeInstruction(OpCodes.Ldarg_2);
                        yield return new CodeInstruction(OpCodes.Call, updateSaveNameMethod);
                    }
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ItemTips), nameof(ItemTips.OnOpen))]
        private static void ItemTips_OnOpen_Postfix(ItemTips __instance, object[] variables)
        {
            var self = __instance;
            var descText = self.m_goTable.GetNode<Text>("Desc_Text");
            if (!descText.text.StartsWith("ID: "))
            {
                var id = (int)variables[0];
                descText.text = $@"ID: {id}
Owned: {SaveManager.Instance.SaveData.m_itemDic.GetValueSafe(id)}
{descText.text}";

                // XXX Could inject our code before the first get_sizeDelta call instead of hardcoding the size update
                // logic here
                var rect = self.m_goTable.GetNode<RectTransform>("Tips_RectTransform");
                rect.sizeDelta = new(rect.sizeDelta.x, descText.preferredHeight + 112f);
                UIUtlils.SetPos(rect);
            }
        }
    }
}
