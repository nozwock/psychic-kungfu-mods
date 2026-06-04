using System.Collections.Generic;
using System.IO;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Common;
using Common.Extensions;
using HarmonyLib;
using UnityEngine;
using VanillaPlus.Save;

namespace VanillaPlus.Patches;

[HarmonyPatch]
internal static class SaveMetadata_Patches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save))]
    private static void SaveManager_Save_Postfix(
        SaveManager __instance,
        string parent,
        string path,
        ref bool __result
    )
    {
        if (!__result)
            return;

        // path param doesn't contain .bytes suffix
        var self = __instance;
        var metafile = Path.Combine(parent, path) + ".meta.json";
        self.SaveData.ToMetadata().Write(metafile);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.ChangeName))]
    private static void SaveManager_ChangeName_Postfix(SaveData data, ref bool __result)
    {
        if (!__result)
            return;

        var metafile = data.m_path.RemoveSuffix(".bytes") + ".meta.json";
        data.ToMetadata().Write(metafile);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Del))]
    private static void SaveManager_Del_Postfix(SaveData data)
    {
        var metafile = data.m_path.RemoveSuffix(".bytes") + ".meta.json";
        File.Delete(metafile);
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Recent), MethodType.Getter)]
internal static class SaveManager_Recent_Patch
{
    private static bool Prefix(SaveManager __instance, ref SaveData __result)
    {
        // null-forgiving because .Recent already returns null with the non-nullable type
        __result = __instance.ReadRecentSaveData()!;
        return false;
    }
}

[HarmonyPatch(typeof(SaveData), nameof(SaveData.FullName), MethodType.Getter)]
internal class SaveData_get_FileName_FixPlayerName_Patch
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
internal class SaveManager_Load_PrioritizeQuicksaveFilename_Patch
{
    private static readonly Regex managedQuicksaveRegex = new("^(?:Quick\\(\\d+\\))$");

    private static SaveData UpdateQuickaveName(SaveData saveData, string parent, string path)
    {
        var filename = Path.GetFileNameWithoutExtension(path);
        if (
            saveData.m_name == null
            || (
                saveData.m_name != filename
                && PathUtils.Equals(parent, SaveManager.Instance.SavePath)
                && !managedQuicksaveRegex.IsMatch(filename)
            )
        )
        {
            saveData.m_name = filename;
            saveData.Write(Path.Combine(parent, path));
        }

        return saveData;
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var saveDataFromJsonMethod = AccessTools
            .Method(typeof(JsonUtility), nameof(JsonUtility.FromJson), [typeof(string)])
            .MakeGenericMethod(typeof(SaveData));

        var updateSaveNameMethod = AccessTools.Method(
            typeof(SaveManager_Load_PrioritizeQuicksaveFilename_Patch),
            nameof(UpdateQuickaveName)
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
