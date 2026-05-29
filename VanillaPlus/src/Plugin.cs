using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VanillaPlus;

[BepInAutoPlugin(id: "nozwock.VanillaPlus")]
public partial class Plugin : BaseUnityPlugin
{
    private Harmony? harmony;
    private InputAction? quickloadAction;

    private void Awake()
    {
        harmony = new(Id);
        try
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }

        // TODO: Main Menu Continue seems to not be using m_saveTime for getting the recent save file
        // TODO: Persist Load UI's last tab page opened (FileWindow)
        // TODO: Limit number of max Quicksaves from 60 to something else
        // TODO: (SaveManager.ChangeName) Make quicksave rename renames the filename as well so as to prevent
        // Quicksaving feature from ever overwriting the named quicksave
        // TODO: Don't show maxed out skills in Cultivation training menu

        Logger.LogInfo($"Harmony patches applied: {harmony.GetPatchedMethods().Count()}");
        foreach (var m in harmony.GetPatchedMethods())
        {
            Logger.LogInfo($"{m.DeclaringType.FullName}.{m.Name}");
        }

        quickloadAction = new(name: "QuickLoad", type: InputActionType.Button, binding: "<Keyboard>/f9");
        quickloadAction.performed += ctx =>
        {
            // TODO: Add rebinding support
            var save = SaveManager.Instance.GetSaves(SaveEnum.快速).FirstOrDefault();
            if (save != null)
            {
                SaveManager.Instance.Load(save);
                UIUtlils.RollUpTips($"Loaded {save.m_name}");
            }
        };
        quickloadAction.Enable();
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        harmony = null;

        Logger.LogInfo("Harmony patches unapplied!");
    }

    [HarmonyPatch]
    private class Patches
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveData), nameof(SaveData.FullName), MethodType.Getter)]
        private static bool SaveData_get_FileName_FixPlayerName_Prefix(SaveData __instance, ref string __result)
        {
            var self = __instance;
            var separator = GameSetting.GetValue(SettingEnum.Language) == 2 ? " " : "";
            __result = self.m_leaderFamily + separator + self.m_leaderName;
            return false;
        }

        [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Load), [typeof(string), typeof(string)])]
        private class SaveManager_Load_PrioritizeSaveFilename_Patch
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
    }
}
