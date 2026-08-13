using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
        var pathWithoutExt = Path.Combine(parent, path);
        self.SaveData.m_path = pathWithoutExt + ".bytes"; // Game doesn't update it, so we will
        self.SaveData.ToMetadata().Write(pathWithoutExt + ".meta.json");
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
internal static class RenameWindow_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(RenameWindow), nameof(RenameWindow.OnAwake))]
    private static void RenameWindow_OnAwake_Postfix(RenameWindow __instance)
    {
        var self = __instance;

        var goTable = self.GetComponent<GoTable>();
        var inputField = goTable.GetNode<InputField>("Input_InputField");
        // Remove whitespace and other restriction
        inputField.onValidateInput = null;
        // Remove character limit
        inputField.characterLimit = 0;
        // TODO: Extend InputField's width
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RenameWindow), nameof(RenameWindow.OnOpen))]
    private static void RenameWindow_OnOpen_Postfix(RenameWindow __instance)
    {
        var self = __instance;

        var goTable = self.GetComponent<GoTable>();
        var inputField = goTable.GetNode<InputField>("Input_InputField");
        // At least on 1.11, game's repeatedly adding new delegate to onValidateInput in OnOpen, so yeah...
        inputField.onValidateInput = null;
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
    internal static void Call_WindowBase_OnAwake(WindowBase instance) =>
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

                var deleteSaveButton = goTable.GetNode<UIButton>("Del_UIButton");
                deleteSaveButton.SetLeftClickEvent(() =>
                {
                    UIUtlils.OpenTipsBox(
                        LanguageUtils.GetTextDef(24),
                        () =>
                        {
                            SaveManager.Instance.Del(meta.ReadSaveData());

                            if (_scrollViewState.SelectedTab == SaveEnum.手动)
                            {
                                saveMetas[saveMetas.IndexOf(meta)] = null;
                            }
                            else
                            {
                                saveMetas.Remove(meta);
                            }

                            if (saveMetas.Count > 0)
                            {
                                if (_scrollViewState.SelectedTab != SaveEnum.手动)
                                {
                                    if (_scrollViewState.SelectedSlot == meta)
                                        _scrollViewState.SelectedSlot = saveMetas[0];
                                    self.m_nextGo.SetActive(true);
                                }
                                else
                                {
                                    if (_scrollViewState.SelectedSlot == meta)
                                    {
                                        _scrollViewState.SelectedSlot = null;
                                        self.m_nextGo.SetActive(false);
                                    }
                                }
                            }
                            else
                            {
                                _scrollViewState.SelectedSlot = null;
                                self.m_nextGo.SetActive(false);
                            }

                            self.m_scrollView.UpdateData();
                        }
                    );
                });
                deleteSaveButton.gameObject.SetActive(
                    _scrollViewState.SelectedTab != SaveEnum.自动
                );
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
                        ? [.. SaveMetadata.ReadAllFixedSlots(Plugin.FixedSaveSlots)]
                        : [.. SaveMetadata.ReadAll(kind)];
            }
        }

        // Keep ToggleGroup in sync with what's in ScrollView
        var goTable = self.transform.GetComponent<GoTable>();
        var menuRect = goTable.GetNode<RectTransform>("Menus_RectTransform");
        for (var i = menuRect.childCount - 1; i >= 0; i--)
        {
            var toggle = menuRect.GetChild(i).GetComponent<UIToggle>();
            toggle.isOn = _scrollViewState.SelectedTab == (SaveEnum)i;
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

                    if (state.SelectedTab == SaveEnum.快速)
                    {
                        UIUtlils.OpenTipsBox(
                            "Rename filename as well? The save is permanent if the filename is not 'Quick(1...).bytes'.",
                            () =>
                            {
                                if (SaveManager.Instance.ChangeName(save, name))
                                {
                                    saveMetas[index] = save.ToMetadata();
                                    refreshScroll();
                                }
                            }
                        );
                        return true;
                    }
                    else
                    {
                        if (SaveManager.Instance.ChangeName(save, name))
                        {
                            saveMetas[index] = save.ToMetadata();
                            refreshScroll();
                            return true;
                        }
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
    }
}

[HarmonyPatch]
internal static class SaveWindow_Patch
{
    private static readonly List<SaveMetadata?> _saveMetas = [];
    private static readonly FileWindow_Patch.ScrollViewState _scrollViewState = new()
    {
        SelectedTab = SaveEnum.手动,
    };
    private static MethodInfo? _refreshScrollMethod;
    private static MethodInfo? _onCloseMethod;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SaveWindow), nameof(SaveWindow.OnAwake))]
    private static bool SaveWindow_OnAwake_Prefix(SaveWindow __instance)
    {
        var self = __instance;

        _refreshScrollMethod ??= AccessTools.Method(
            typeof(SaveWindow),
            nameof(SaveWindow.RefreshScroll)
        );
        _onCloseMethod ??= AccessTools.Method(typeof(SaveWindow), nameof(SaveWindow.OnClose));

        FileWindow_Patch.Call_WindowBase_OnAwake(self);

        var goTable = self.transform.GetComponent<GoTable>();

        var backButton = goTable.GetNode<UIButton>("Back_UIButton");
        backButton.SetLeftClickEvent(() =>
        {
            _onCloseMethod.Invoke(self, []);
        });

        var scrollView = goTable.GetNode<AillieoUtils.ScrollView>("ScrollView_ScrollView");
        self.m_scrollView = scrollView;
        scrollView.SetItemCountFunc(() => Plugin.FixedSaveSlots);
        scrollView.SetUpdateFunc(
            (index, rect) =>
            {
                FileWindow_Patch.SetSaveSlotContent(
                    index,
                    rect,
                    _saveMetas,
                    _scrollViewState,
                    () => _refreshScrollMethod.Invoke(self, [])
                );

                var goTable = rect.GetComponent<GoTable>();
                var meta = _saveMetas[index];

                var slotButton = goTable.GetNode<UIButton>("Have_UIButton");
                var emptySlotButton = goTable.GetNode<UIButton>("No_UIButton");

                if (meta == null)
                {
                    emptySlotButton.gameObject.SetActive(true);
                    slotButton.gameObject.SetActive(false);
                    emptySlotButton.SetLeftClickEvent(() =>
                    {
                        UIUtlils.OpenRenameWindow(
                            LanguageUtils.GetTextDef(37),
                            "Save",
                            (name) =>
                            {
                                if (SaveManager.Instance.FiexedSave(name, index))
                                {
                                    _saveMetas[index] = SaveManager.Instance.SaveData.ToMetadata();
                                    _refreshScrollMethod.Invoke(self, []);
                                    return true;
                                }

                                return false;
                            }
                        );
                    });
                }
                else
                {
                    emptySlotButton.gameObject.SetActive(false);
                    slotButton.gameObject.SetActive(true);
                    slotButton.SetLeftClickEvent(() =>
                    {
                        UIUtlils.OpenTipsBox(
                            LanguageUtils.GetTextDef(222),
                            () =>
                            {
                                if (SaveManager.Instance.FiexedSave(meta.Name, index))
                                {
                                    _saveMetas[index] = SaveManager.Instance.SaveData.ToMetadata();
                                    _refreshScrollMethod.Invoke(self, []);
                                }
                            }
                        );
                    });

                    var deleteSaveButton = goTable.GetNode<UIButton>("Del_UIButton");
                    deleteSaveButton.SetLeftClickEvent(() =>
                    {
                        UIUtlils.OpenTipsBox(
                            LanguageUtils.GetTextDef(24),
                            () =>
                            {
                                SaveManager.Instance.Del(meta.ReadSaveData());
                                _saveMetas[_saveMetas.IndexOf(meta)] = null;
                                _refreshScrollMethod.Invoke(self, []);
                            }
                        );
                    });
                }
            }
        );

        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(SaveWindow), nameof(SaveWindow.RefreshScroll))]
    private static bool SaveWindow_RefreshScroll_Prefix(SaveWindow __instance)
    {
        var self = __instance;

        if (_saveMetas.Count == 0)
        {
            _saveMetas.AddRange(SaveMetadata.ReadAllFixedSlots(Plugin.FixedSaveSlots));
        }

        self.m_scrollView.UpdateData();

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SaveWindow), nameof(SaveWindow.OnClose))]
    private static void SaveWindow_OnClose_Postfix()
    {
        _saveMetas.Clear();
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

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.ChangeName))]
internal class SaveManager_ChangeName_Patch
{
    private static void Postfix(SaveData data, string name, bool __result)
    {
        // TODO: ChangeName updates m_saveTime... it shouldn't.
        if (!__result)
            return;
        // Part of SaveMetadata_Patches. It's here because we want to ensure it runs before our next stuff.
        var meta = data.ToMetadata();
        meta.Write();

        // Only rename for old-style named infinite quicksaves.
        var isQuicksavesDir = PathUtils.Equals(
            Path.GetDirectoryName(data.m_path),
            SaveManager.Instance.SavePath
        );
        if (!isQuicksavesDir)
            return;

        // TODO: The filename for save (*.bytes) should also be getting rid of invalid chars in original function
        var newFilename = string.Join("_", name.Split(Path.GetInvalidFileNameChars())) + ".bytes";
        var newPath = PathUtils.WithBaseName(data.m_path, newFilename);

        var metaPath = meta.Filepath;
        Debug.Log($"Rename '{data.m_path}' -> '{newPath}'");
        File.Move(data.m_path, newPath);
        data.m_path = newPath;

        var newMetaPath = data.ToMetadata().Filepath;
        Debug.Log($"Rename '{metaPath}' -> '{newMetaPath}'");
        File.Move(metaPath, newMetaPath);
    }
}
