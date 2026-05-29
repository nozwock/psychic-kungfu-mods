using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace VanillaPlus;

[BepInAutoPlugin(id: "nozwock.VanillaPlus")]
public partial class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance { get; private set; } = null!;

    private Harmony? harmony;

    private void Awake()
    {
        Instance = this;

        InitActions();

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

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
        harmony = null;

        Logger.LogInfo("Harmony patches unapplied!");
    }

    private void InitActions()
    {
        var quickload = InputManager.Instance.m_main.AddAction(
            $"{Id}.QuickLoad",
            type: InputActionType.Button,
            binding: "<Keyboard>/f9");
        quickload.performed += ctx =>
        {
            // TODO: Add rebinding support
            var save = SaveManager.Instance.GetSaves(SaveEnum.快速).FirstOrDefault();
            if (save != null)
            {
                SaveManager.Instance.Load(save);
                UIUtlils.RollUpTips($"Loaded {save.m_name}");
            }
        };
    }
}
