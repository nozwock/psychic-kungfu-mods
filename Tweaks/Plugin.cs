using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using DBLoad;
using HarmonyLib;
using Sirenix.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Tweaks;

[BepInAutoPlugin(id: "nozwock.Tweaks")]
public partial class Plugin : BaseUnityPlugin
{
    private Harmony? harmony;
    private InputAction? quickloadAction;
    private SynchronizationContext? defaultContext;

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

        SpawnConsoleCommandReader();

        RemoveMartialArtMoralityCondition();
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        harmony = null;

        Logger.LogInfo("Harmony patches unapplied!");
    }

    private static void RemoveMartialArtMoralityCondition()
    {
        foreach (var orig in WuXue.Dic.Values)
        {
            var conditionList = orig.m_condition.ToList();
            var conditionValueList = orig.m_conditionValue.ToList();
            var conditionModified = false;

            for (var i = conditionValueList.Count - 1; i >= 0; i--)
            {
                if (conditionValueList[i][0] == (int)EffectId.善恶)
                {
                    conditionList.RemoveAt(i);
                    conditionValueList.RemoveAt(i);
                    conditionModified = true;
                }
            }

            if (conditionModified)
            {
                Unsafe.AsRef(in orig.m_condition) = [.. conditionList];
                Unsafe.AsRef(in orig.m_conditionValue) = [.. conditionValueList];
            }
        }
    }

    private void SpawnConsoleCommandReader()
    {
        // FIXME: Console.Readline no longer works in BepInEx console
        defaultContext = SynchronizationContext.Current;
        new Thread(() =>
        {
            while (true)
            {
                var line = Console.ReadLine();
                var splits = line.Split(' ');
                if (splits.Length > 0)
                {
                    var cmd = splits[0].ToLower();
                    if (cmd == "maxskill")
                    {
                        Logger.LogInfo("Maxing out All Martial Skills");
                        defaultContext?.Post(_ =>
                        {
                            foreach (var kvp in WuXue.Dic)
                            {
                                var id = kvp.Key;
                                var data = kvp.Value;
                                var maxExp = data.m_lvMax * data.m_exp;
                                if (SaveManager.Instance.SaveData.m_wuXueExpDic.TryGetValue(id, out var exp)
                                    && exp < maxExp)
                                {
                                    SaveManager.Instance.SaveData.AddWuXueExp(id, maxExp, true);
                                }
                            }
                        }, null);
                    }
                    else if (cmd == "lover")
                    {
                        if (!(splits.Length > 1 && int.TryParse(splits[1], out var id)))
                            return;

                        defaultContext?.Post(_ =>
                        {
                            if (SaveManager.Instance.SaveData.NpcDic.TryGetValue(id, out var npc))
                            {
                                Logger.LogInfo($"Assigning NPC \"{npc.Name}\" ({id}) to Lover (Crimson Veil) camp");
                                npc.m_camp = NpcCamp.情缘;
                            }
                            else
                            {
                                Logger.LogInfo($"NPC {id} not found");
                            }
                        }, null);
                    }
                    else if (int.TryParse(splits[0], out var id))
                    {
                        var count = 1;
                        if (splits.Length > 1 && int.TryParse(splits[1], out var n))
                            count = n;

                        if (Item.Get(id) != null)
                        {
                            Logger.LogInfo($"Added Item: id={id}, count={count}");
                            defaultContext?.Post(_ =>
                            {
                                SaveManager.Instance.SaveData.AddItems([id], [count]);
                            }, null);
                        }
                        else
                        {
                            Logger.LogInfo($"Invalid Item: id={id}");
                        }
                    }
                }
            }
        })
        {
            IsBackground = true,
        }.Start();
    }

    [HarmonyPatch]
    private class Patches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(SaveData), nameof(SaveData.GetEffectValue))]
        private static void NpcInfo_set_Favo_GiftBoost_Postfix(ref float __result, EffectId id)
        {
            if (id == EffectId.送礼好感度)
            {
                __result *= 5;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveData), nameof(SaveData.JingLiPer), MethodType.Setter)]
        private static void SaveData_set_JingLiPer_InfiniteEnergy_Prefix(ref float value)
        {
            value = 1f;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SaveData), nameof(SaveData.TiLi), MethodType.Setter)]
        private static void SaveData_set_TiLi_InfiniteStamina_Prefix(ref int value)
        {
            value = 999999; // Will get clamped to max value
        }

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
                if (saveData.m_name != filename && !managedSaveRegex.IsMatch(saveData.m_name))
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

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ItemTips), nameof(ItemTips.OnOpen))]
        private static void ItemTips_OnOpen_ShowItemId_Postfix(ItemTips __instance, object[] variables)
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

        [HarmonyPatch]
        private class FunctionWindow_SetFriend_Select_ShowTeamId_Patch
        {
            private static FieldInfo? goTableField;

            private static MethodBase TargetMethod()
            {
                var selectNpcMethodName = $"<{nameof(FunctionWindow.SetFriend)}>g__Select";
                return typeof(FunctionWindow)
                    .GetNestedTypes(BindingFlags.NonPublic)
                    .SelectMany(it => it.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
                    .First(m =>
                    {
                        if (!m.Name.Contains(selectNpcMethodName))
                            return false;
                        var p = m.GetParameters();
                        return p.Length == 1 && p[0].ParameterType == typeof(int);
                    });
            }

            private static void Postfix(object __instance, int npcId)
            {
                var self = __instance;

                if (goTableField == null)
                    goTableField = AccessTools.Field(self.GetType(), "goTable");
                var goTable = (GoTable)goTableField.GetValue(self);

                // Have to gate it since it's called more than once for some reason
                var descText = goTable.GetNode<Text>("Desc_Text");
                if (!descText.text.StartsWith("[ID: "))
                {
                    descText.text = $"[ID: {npcId}] {descText.text}";

                    var rect = goTable.GetNode<RectTransform>("Desc_RectTransform");
                    rect.sizeDelta = new(rect.sizeDelta.x, Mathf.Max(200f, descText.preferredHeight + 67f));
                    LayoutRebuilder.ForceRebuildLayoutImmediate(
                        goTable.GetNode<RectTransform>("Content_RectTransform"));
                }
            }
        }
    }
}
