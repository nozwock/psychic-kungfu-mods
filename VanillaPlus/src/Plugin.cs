using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using DBLoad;
using HarmonyLib;
using JinGuLib.UI;
using UnityEngine.InputSystem;

namespace VanillaPlus;

[BepInAutoPlugin(id: "nozwock.VanillaPlus")]
[BepInDependency(JinGuLib.Plugin.Id)]
public partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;

    private Harmony? harmony;

    private void Awake()
    {
        Instance = this;

        AppendDBLoadLanguage();

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
    }

    private void Start()
    {
        InitActions();
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        harmony = null;
        Logger.LogInfo("Harmony patches unapplied!");
    }

    private void InitActions()
    {
        var bindingsFilepath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            "bindings.json"
        );

        InputManager.Instance.m_asset.Disable();
        var quickload = InputManager.Instance.m_main.AddAction(
            $"{Id}.QuickLoad",
            type: InputActionType.Button,
            binding: "<Keyboard>/f9"
        );
        quickload.performed += ctx =>
        {
            var save = SaveManager.Instance.GetSaves(SaveEnum.快速).FirstOrDefault();
            if (save != null)
            {
                SaveManager.Instance.Load(save);
                UIUtlils.RollUpTips($"Loaded {save.m_name}");
            }
        };
        InputManager.Instance.m_asset.Enable();

        RebindManager.Instance.RegisterRebindableAction(
            _keyedLocalizedText["quickload"].Cn,
            quickload,
            position: RebindPosition.After("Save"),
            filepath: bindingsFilepath
        );
    }

    private void AppendDBLoadLanguage()
    {
        foreach (var (_, text) in _keyedLocalizedText)
        {
            // Only going to have simplified Chinese
            var id = text.Cn.GetHashCode();
            Language.Dic[id] = new(id, text.En, text.Cn);
        }
    }

    private readonly Dictionary<string, (string En, string Cn)> _keyedLocalizedText = new()
    {
        ["quickload"] = ("Quick Load", "快速读档"),
    };
}
