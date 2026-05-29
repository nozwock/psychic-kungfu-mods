using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace VanillaPlus.Patches;

[HarmonyPatch(typeof(SaveData), nameof(SaveData.FullName), MethodType.Getter)]
internal class SaveData_get_FileName_FixPlayerName
{
    private static bool Prefix(SaveData __instance, ref string __result)
    {
        var self = __instance;
        var separator = GameSetting.GetValue(SettingEnum.Language) == 2 ? " " : "";
        __result = self.m_leaderFamily + separator + self.m_leaderName;
        return false;
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Load), [typeof(string), typeof(string)])]
internal class SaveManager_Load_PrioritizeSaveFilename_Patch
{
    private static readonly Regex managedSaveRegex = new("^(?:Fixed(\\d+)|Quick(\\d+)|AutoSave\\d+)$");

    private static SaveData UpdateSaveName(SaveData saveData, string parent, string path)
    {
        var filename = Path.GetFileNameWithoutExtension(path);
        if (saveData.m_name == null
            || (saveData.m_name != filename && !managedSaveRegex.IsMatch(saveData.m_name)))
        {
            var filepath = Path.Combine(parent, path);
            saveData.m_path = filepath;
            saveData.m_name = filename;

            File.WriteAllBytes(
                saveData.m_path,
                SaveManager.Instance.Encrypt(JsonUtility.ToJson(saveData)));
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
            typeof(SaveManager_Load_PrioritizeSaveFilename_Patch),
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