using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Common;
using Common.Extensions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
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

[HarmonyPatch]
internal static class FileWindow_Patch
{
    private static Dictionary<SaveEnum, List<SaveMetadata?>> _saveInfos = [];
    private static SaveMetadata? _selected;
    private static MethodInfo? _refreshScrollMethod;
    private static MethodInfo? _onCloseMethod;

    [HarmonyReversePatch]
    [HarmonyPatch(typeof(WindowBase), nameof(WindowBase.OnAwake))]
    private static void Call_WindowBase_OnAwake(WindowBase instance) =>
        throw new NotImplementedException();

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FileWindow), nameof(FileWindow.OnAwake))]
    private static bool FileWindow_OnAwake_Prefix(FileWindow __instance)
    {
        var self = __instance;

        _refreshScrollMethod ??= AccessTools.Method(
            typeof(FileWindow),
            nameof(FileWindow.RefreshScroll)
        );
        _onCloseMethod ??= AccessTools.Method(typeof(FileWindow), nameof(FileWindow.OnClose));

        // Note: Don't use reflection to do this, it'd just call FileWindow.OnAwake instead
        Call_WindowBase_OnAwake(self);

        var goTable = self.transform.GetComponent<GoTable>();

        var folderButton = goTable.GetNode<UIButton>("Folder_UIButton");
        folderButton.SetLeftClickEvent(() =>
        {
            Application.OpenURL("file://" + SaveManager.Instance.SavePath);
        });

        var backButton = goTable.GetNode<UIButton>("Back_UIButton");
        backButton.SetLeftClickEvent(() =>
        {
            _onCloseMethod.Invoke(self, []);
        });

        var loadButton = goTable.GetNode<UIButton>("Load_UIButton");
        self.m_nextGo = loadButton.transform.parent.gameObject;
        loadButton.SetLeftClickEvent(() =>
        {
            var save = _selected?.ReadSaveData();
            if (save != null)
                SaveManager.Instance.Load(save);
        });

        var scrollView = goTable.GetNode<AillieoUtils.ScrollView>("ScrollView_ScrollView");
        self.m_scrollView = scrollView;
        scrollView.SetUpdateFunc(
            (index, rect) =>
            {
                var goTable = rect.GetComponent<GoTable>();
                var info = _saveInfos[self.m_saveEnum][index];
                var label = goTable.GetNode<Text>("Num_Text");

                if (self.m_saveEnum == SaveEnum.手动)
                {
                    label.transform.parent.gameObject.SetActive(value: true);
                    label.text = (index + 1).ToString();
                }
                else
                {
                    label.transform.parent.gameObject.SetActive(value: false);
                }

                var emptySlotGo = goTable.GetNode<RectTransform>("No_RectTransform").gameObject;
                var slotToggle = goTable.GetNode<UIToggle>("Have_UIToggle");

                if (info == null)
                {
                    slotToggle.gameObject.SetActive(value: false);
                    emptySlotGo.gameObject.SetActive(value: true);
                    return;
                }

                slotToggle.gameObject.SetActive(value: true);
                emptySlotGo.gameObject.SetActive(value: false);
                slotToggle.SetLeftClickEvent(
                    (isOn) =>
                    {
                        if (isOn)
                        {
                            _selected = info;
                        }
                    }
                );

                var renameButton = goTable.GetNode<UIButton>("Rename_UIButton");
                renameButton.SetLeftClickEvent(() =>
                {
                    UIUtlils.OpenRenameWindow(
                        LanguageUtils.GetTextDef(23),
                        info.Name,
                        (name) =>
                        {
                            var save = info.ReadSaveData();
                            if (save == null)
                                return false;

                            SaveManager.Instance.ChangeName(save, name);
                            _saveInfos[self.m_saveEnum][index] = save.ToMetadata();
                            _refreshScrollMethod.Invoke(self, []);

                            return true;
                        }
                    );
                });
                renameButton.gameObject.SetActive(self.m_saveEnum != SaveEnum.自动);

                var gameTimeLabel = goTable.GetNode<Text>("GameTime_Text");
                gameTimeLabel.text = $"{info.GameTime / 3600f:F1}h";

                var difficultyLabel = goTable.GetNode<Text>("Dif_Text");
                difficultyLabel.text = LanguageUtils.GetDifficultName(info.Difficulty);

                var taskLabel = goTable.GetNode<Text>("Task_Text");
                if (info.ShowMainId > 0)
                {
                    var taskData = DBLoad.Task.Get(info.ShowMainId);
                    taskLabel.text = LanguageUtils.GetStr(taskData.m_name);
                }
                else
                {
                    taskLabel.text = string.Empty;
                }

                var saveNameLabel = goTable.GetNode<Text>("Name_Text");
                saveNameLabel.text = info.Name;

                var saveTimeLabel = goTable.GetNode<Text>("SaveTime_Text");
                saveTimeLabel.text = new DateTime(info.SaveTime, DateTimeKind.Local).ToString();

                var sceneLabel = goTable.GetNode<Text>("Scene_Text");
                sceneLabel.text = LanguageUtils.GetStr(DBLoad.Scene.Get((int)info.Scene).m_name);

                var deleteSaveButton = goTable.GetNode<UIButton>("Del_UIButton");
                deleteSaveButton.SetLeftClickEvent(() =>
                {
                    UIUtlils.OpenTipsBox(
                        LanguageUtils.GetTextDef(24),
                        () =>
                        {
                            SaveManager.Instance.Del(info.ReadSaveData());

                            var saveInfos = _saveInfos[self.m_saveEnum];
                            if (self.m_saveEnum == SaveEnum.手动)
                            {
                                saveInfos[saveInfos.IndexOf(info)] = null;
                            }
                            else
                            {
                                saveInfos.Remove(info);
                            }

                            if (saveInfos.Count > 0)
                            {
                                if (_selected == info)
                                {
                                    _selected = saveInfos[0];
                                }

                                self.m_nextGo.SetActive(value: true);
                            }
                            else
                            {
                                _selected = null;
                                self.m_nextGo.SetActive(value: false);
                            }

                            self.m_scrollView.UpdateData();
                        }
                    );
                });
                deleteSaveButton.gameObject.SetActive(self.m_saveEnum != SaveEnum.自动);

                slotToggle.isOn = info == _selected;
            }
        );
        scrollView.SetItemCountFunc(() => _saveInfos[self.m_saveEnum].Count);

        var menuRect = goTable.GetNode<RectTransform>("Menus_RectTransform");
        var menuChildGo = menuRect.GetChild(0).gameObject;
        UIUtlils.SetContent(
            menuRect,
            menuChildGo,
            3,
            (transform, i) =>
            {
                UIToggle toggle = transform.GetComponent<UIToggle>();
                toggle.SetLeftClickEvent(
                    (isOn) =>
                    {
                        if (isOn)
                        {
                            self.m_saveEnum = (SaveEnum)i;
                            _refreshScrollMethod.Invoke(self, []);
                        }
                    }
                );
                toggle.isOn = self.m_saveEnum == (SaveEnum)i;
            }
        );

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FileWindow), nameof(FileWindow.RefreshScroll))]
    private static bool FileWindow_RefreshScroll_Prefix(FileWindow __instance)
    {
        var self = __instance;

        if (_saveInfos.Count == 0)
        {
            Debug.Log("READ METADATA FROM DISK");
            foreach (var kind in (SaveEnum[])Enum.GetValues(typeof(SaveEnum)))
            {
                _saveInfos[kind] =
                    kind == SaveEnum.手动
                        ? [.. SaveMetadata.ReadAllFixedSlots()]
                        : [.. SaveMetadata.ReadAll(kind)];
            }
        }

        if (_saveInfos[self.m_saveEnum].Count > 0)
        {
            _selected = _saveInfos[self.m_saveEnum][0];
            self.m_nextGo.SetActive(value: true);
        }
        else
        {
            self.m_nextGo.SetActive(value: false);
        }

        self.m_scrollView.UpdateData();
        if (_saveInfos[self.m_saveEnum].Count > 0)
        {
            self.m_scrollView.ScrollTo(0);
        }

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FileWindow), nameof(FileWindow.OnClose))]
    private static void FileWindow_OnClose_Postfix()
    {
        _selected = null;
        _saveInfos.Clear();
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
