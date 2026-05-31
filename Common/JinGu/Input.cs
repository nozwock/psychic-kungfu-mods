using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Common.Extensions;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Common.JinGu;

internal class InputRebindUIRegistry : IDisposable
{
    private readonly ConditionalWeakTable<GoTable, InputAction> _goTableActions = new();
    private readonly Dictionary<string, (InputAction Action, string DisplayName)> _actionsNameMap = [];
    private readonly List<IDetour> _detours = [];

    private readonly MethodInfo _method_OptionWindow_OnAwake_SetInput;
    private FieldInfo? _field_OptionWindow_OnAwake_goTable;
    private GameObject? _controlPrefab;

    public InputRebindUIRegistry()
    {
        // TODO: Persist bindings on disk.
        // Ref PlayerPrefs.SetString("KeySet", m_asset.SaveBindingOverridesAsJson())
        _method_OptionWindow_OnAwake_SetInput = typeof(OptionWindow).GetLocalMethod(
            $"<{nameof(OptionWindow.OnAwake)}>g__SetInput",
            [typeof(GoTable), typeof(InputAction), typeof(int)]);
        var method_OptionWindow_OnAwake_SetControl = typeof(OptionWindow).GetLocalMethod(
            $"<{nameof(OptionWindow.OnAwake)}>g__SetControl",
            []);

        // MonoMod is used instead of Harmony since we want to manage the hooks ourselves here in the class instance and
        // there's no way to skip Harmony patches from being included in PatchAll(Assembly.GetExecutingAssembly()).
        _detours.AddRange([
            new Hook(
                typeof(OptionWindow).GetMethod(nameof(OptionWindow.OnAwake)),
                Hook_OptionWindow_OnAwake),
            new Hook(method_OptionWindow_OnAwake_SetControl, Hook_OptionWindow_OnAwake_CreateControl)
        ]);
    }

    public void Dispose()
    {
        foreach (var detour in _detours)
            detour.Dispose();
        _detours.Clear();
    }

    /// <summary>
    /// <paramref name="id"/> must be unique among all registrations done via <see cref="InputRebindUIRegistry"/>. 
    /// <para/>
    /// If <paramref name="id"/> is null, <see cref="InputAction.name"/> will be used in its place.
    /// <para/>
    /// Call only from the Unity thread (default plugin thread).
    /// </summary>
    public void RegisterRebindableAction(string displayName, InputAction action, string? id = null)
    {
        // TODO: Allow setting sibling index or positioning before/after existing options
        Debug.Log($"Registering rebindable action: id=\"{id ?? action.name}\" displayName=\"{displayName}\"");
        _actionsNameMap[id ?? action.name] = new(action, displayName);
    }

    private void Hook_OptionWindow_OnAwake(Action<OptionWindow> orig, OptionWindow self)
    {
        try
        {
            var goTable = self.transform.GetComponent<GoTable>();
            var controlPageGoTable = goTable.GetNode<GoTable>("ControlPage_GoTable");
            var controlGoTable = controlPageGoTable.m_list
                .Select(it => it.obj)
                .OfType<GoTable>()
                .First();

            CreateControlGameObjects(controlGoTable.gameObject);
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }

        orig(self);
    }

    private void Hook_OptionWindow_OnAwake_CreateControl(Action<object> orig, object self)
    {
        orig(self);

        if (_field_OptionWindow_OnAwake_goTable == null)
            _field_OptionWindow_OnAwake_goTable = AccessTools.Field(self.GetType(), "goTable");

        var goTable = (GoTable)_field_OptionWindow_OnAwake_goTable.GetValue(self);
        var controlPageGoTable = goTable.GetNode<GoTable>("ControlPage_GoTable");
        var controlGoTable = controlPageGoTable.m_list
            .Select(it => it.obj)
            .OfType<GoTable>()
            .First();

        var controlsContainer = controlGoTable.transform.parent;
        for (var i = controlsContainer.childCount - 1; i >= 0; i--)
        {
            var childGoTable = controlsContainer.GetChild(i).GetComponent<GoTable>();
            if (_goTableActions.TryGetValue(childGoTable, out var action))
            {
                Debug.Log(
                    "Updating custom control: " +
                    $"id=\"{childGoTable.gameObject.name}\" goTableName=\"{childGoTable.name}\"");
                // XXX Could just have a hardcoded copy of this SetInput local method instead of calling it via
                // reflection.
                _method_OptionWindow_OnAwake_SetInput.Invoke(self, [childGoTable, action, 0]);
            }
        }
    }

    private void CreateControlGameObjects(GameObject prefab)
    {
        if (_controlPrefab == null)
        {
            _controlPrefab = UnityEngine.Object.Instantiate(prefab);
            _controlPrefab.SetActive(false);
            _controlPrefab.name = $"{nameof(InputRebindUIRegistry)}_Control_Prefab";
        }

        Debug.Log($"Pending control count: {_actionsNameMap.Count}");
        foreach (var kvp in _actionsNameMap)
        {
            Debug.Log($"Creating custom control GameObject: id=\"{kvp.Key}\"");

            var go = UnityEngine.Object.Instantiate(_controlPrefab, prefab.transform.parent);
            go.name = kvp.Key;

            var langKey = go.transform.GetComponentInChildren<LanguageKey>();
            langKey.GetComponent<Text>().text = kvp.Value.DisplayName;

            go.SetActive(true);

            _goTableActions.Add(go.GetComponent<GoTable>(), kvp.Value.Action);
        }
    }
}