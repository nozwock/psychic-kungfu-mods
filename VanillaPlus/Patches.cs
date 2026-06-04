using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using Common;
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
        data.ToMetadata().Write();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Del))]
    private static void SaveManager_Del_Postfix(SaveData data)
    {
        File.Delete(data.ToMetadata().Filepath);
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
    internal sealed class ScrollViewState
    {
        public SaveEnum SelectedTab { get; set; }
        public SaveMetadata? SelectedSlot { get; set; }
    };

    private static readonly Dictionary<SaveEnum, List<SaveMetadata?>> _saveMetasByKind = [];
    private static readonly ScrollViewState _scrollViewState = new();
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
            var save = _scrollViewState.SelectedSlot?.ReadSaveData();
            if (save != null)
                SaveManager.Instance.Load(save);
        });

        var scrollView = goTable.GetNode<AillieoUtils.ScrollView>("ScrollView_ScrollView");
        self.m_scrollView = scrollView;
        scrollView.SetUpdateFunc(
            (index, rect) =>
            {
                var goTable = rect.GetComponent<GoTable>();
                var saveMetas = _saveMetasByKind[_scrollViewState.SelectedTab];
                var meta = saveMetas[index];

                var emptySlotGo = goTable.GetNode<RectTransform>("No_RectTransform").gameObject;
                var slotToggle = goTable.GetNode<UIToggle>("Have_UIToggle");

                SetSaveSlotContent(
                    index,
                    rect,
                    self.m_scrollView,
                    self.m_nextGo,
                    saveMetas,
                    _scrollViewState,
                    () => _refreshScrollMethod.Invoke(self, [])
                );

                if (meta == null)
                {
                    slotToggle.gameObject.SetActive(false);
                    emptySlotGo.gameObject.SetActive(true);
                    return;
                }

                slotToggle.gameObject.SetActive(true);
                emptySlotGo.gameObject.SetActive(false);
                slotToggle.SetLeftClickEvent(
                    (isOn) =>
                    {
                        if (isOn)
                        {
                            _scrollViewState.SelectedSlot = meta;
                        }
                    }
                );
                slotToggle.isOn = meta == _scrollViewState.SelectedSlot;
            }
        );
        scrollView.SetItemCountFunc(() => _saveMetasByKind[_scrollViewState.SelectedTab].Count);

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
                            _scrollViewState.SelectedTab = (SaveEnum)i;
                            _refreshScrollMethod.Invoke(self, []);
                        }
                    }
                );
                toggle.isOn = _scrollViewState.SelectedTab == (SaveEnum)i;
            }
        );

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FileWindow), nameof(FileWindow.RefreshScroll))]
    private static bool FileWindow_RefreshScroll_Prefix(FileWindow __instance)
    {
        var self = __instance;

        if (_saveMetasByKind.Count == 0)
        {
            foreach (var kind in (SaveEnum[])Enum.GetValues(typeof(SaveEnum)))
            {
                _saveMetasByKind[kind] =
                    kind == SaveEnum.手动
                        ? [.. SaveMetadata.ReadAllFixedSlots()]
                        : [.. SaveMetadata.ReadAll(kind)];
            }
        }

        int index = 0;
        var saveMetas = _saveMetasByKind[_scrollViewState.SelectedTab];
        if (saveMetas.Count > 0)
        {
            var (meta, i) = saveMetas
                .Select((meta, i) => (meta, i))
                .FirstOrDefault(pair => pair.meta != null);

            if (meta != null)
                index = i;
            _scrollViewState.SelectedSlot = meta;
            self.m_nextGo.SetActive(_scrollViewState.SelectedSlot != null);
        }
        else
        {
            _scrollViewState.SelectedSlot = null;
            self.m_nextGo.SetActive(false);
        }

        self.m_scrollView.UpdateData();
        if (saveMetas.Count > 0)
        {
            self.m_scrollView.ScrollTo(index);
        }

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FileWindow), nameof(FileWindow.OnClose))]
    private static void FileWindow_OnClose_Postfix()
    {
        _scrollViewState.SelectedTab = default;
        _scrollViewState.SelectedSlot = default;
        _saveMetasByKind.Clear();
    }

    internal static void SetSaveSlotContent(
        int index,
        RectTransform slotRect,
        AillieoUtils.ScrollView scrollView,
        GameObject? loadButtonGo,
        List<SaveMetadata?> saveMetas,
        ScrollViewState state,
        Action refreshScroll
    )
    {
        var goTable = slotRect.GetComponent<GoTable>();
        var meta = saveMetas[index];

        var slotNumberLabel = goTable.GetNode<Text>("Num_Text");
        if (state.SelectedTab == SaveEnum.手动)
        {
            slotNumberLabel.transform.parent.gameObject.SetActive(true);
            slotNumberLabel.text = (index + 1).ToString();
        }
        else
        {
            slotNumberLabel.transform.parent.gameObject.SetActive(false);
        }

        if (meta == null)
            return;

        var renameButton = goTable.GetNode<UIButton>("Rename_UIButton");
        renameButton.SetLeftClickEvent(() =>
        {
            UIUtlils.OpenRenameWindow(
                LanguageUtils.GetTextDef(23),
                meta.Name,
                (name) =>
                {
                    var save = meta.ReadSaveData();
                    if (save == null)
                        return false;

                    if (SaveManager.Instance.ChangeName(save, name))
                    {
                        saveMetas[index] = save.ToMetadata();
                        refreshScroll();
                        return true;
                    }

                    return false;
                }
            );
        });
        renameButton.gameObject.SetActive(state.SelectedTab != SaveEnum.自动);

        var gameTimeLabel = goTable.GetNode<Text>("GameTime_Text");
        gameTimeLabel.text = $"{meta.GameTime / 3600f:F1}h";

        var difficultyLabel = goTable.GetNode<Text>("Dif_Text");
        difficultyLabel.text = LanguageUtils.GetDifficultName(meta.Difficulty);

        var taskLabel = goTable.GetNode<Text>("Task_Text");
        if (meta.ShowMainId > 0)
        {
            var taskData = DBLoad.Task.Get(meta.ShowMainId);
            taskLabel.text = LanguageUtils.GetStr(taskData.m_name);
        }
        else
        {
            taskLabel.text = string.Empty;
        }

        var saveNameLabel = goTable.GetNode<Text>("Name_Text");
        saveNameLabel.text = meta.Name;

        var saveTimeLabel = goTable.GetNode<Text>("SaveTime_Text");
        saveTimeLabel.text = new DateTime(meta.SaveTime, DateTimeKind.Local).ToString();

        var sceneLabel = goTable.GetNode<Text>("Scene_Text");
        sceneLabel.text = LanguageUtils.GetStr(DBLoad.Scene.Get((int)meta.Scene).m_name);

        var deleteSaveButton = goTable.GetNode<UIButton>("Del_UIButton");
        deleteSaveButton.SetLeftClickEvent(() =>
        {
            UIUtlils.OpenTipsBox(
                LanguageUtils.GetTextDef(24),
                () =>
                {
                    SaveManager.Instance.Del(meta.ReadSaveData());

                    if (state.SelectedTab == SaveEnum.手动)
                    {
                        saveMetas[saveMetas.IndexOf(meta)] = null;
                    }
                    else
                    {
                        saveMetas.Remove(meta);
                    }

                    if (saveMetas.Count > 0)
                    {
                        if (state.SelectedTab != SaveEnum.手动)
                        {
                            if (state.SelectedSlot == meta)
                                state.SelectedSlot = saveMetas[0];
                            loadButtonGo?.SetActive(true);
                        }
                        else
                        {
                            if (state.SelectedSlot == meta)
                            {
                                state.SelectedSlot = null;
                                loadButtonGo?.SetActive(false);
                            }
                        }
                    }
                    else
                    {
                        state.SelectedSlot = null;
                        loadButtonGo?.SetActive(false);
                    }

                    scrollView.UpdateData();
                }
            );
        });
        deleteSaveButton.gameObject.SetActive(state.SelectedTab != SaveEnum.自动);
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
