using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Common.Extensions;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Common.JinGu;

/// <summary>
/// Type is not thread-safe. So, only call methods from the Unity thread (default plugin thread).
/// </summary>
internal class InputRebindUIRegistry : IDisposable
{
    private record class RebindableAction(
        InputAction Action,
        string DisplayName,
        RebindUIPosition Position,
        string? Filepath
    );

    private sealed record class BindingOverride(int Index, string Path);
    private sealed record class BindingOverrides(Dictionary<string, List<BindingOverride>> Bindings);

    public bool VerboseLogging { get; set; }

    private readonly ConditionalWeakTable<GoTable, InputAction> _goTableActions = new();
    private readonly Dictionary<string, RebindableAction> _actionsNameMap = [];
    private readonly HashSet<Action<InputManager>> _inputManagerAwakeListeners = [];
    private readonly Dictionary<string, BindingOverrides> _bindingOverridesByFile = [];
    private readonly List<IDetour> _detours = [];

    private readonly MethodInfo _method_OptionWindow_OnAwake_SetInput;
    private FieldInfo? _field_OptionWindow_OnAwake_goTable;
    private GameObject? _controlPrefab;

    /// <summary>
    /// Use this event to attach or register <see cref="InputAction"/> instances to the <see cref="InputManager"/>'s
    /// <see cref="InputActionMap"/>s.
    /// <para/>
    /// This is the only safe point to modify input setup. Accessing <see cref="InputManager.Instance"/> directly may
    /// occur before internal state (e.g. <see cref="InputManager.m_main"/>) and action maps are fully initialized,
    /// leading to errors.
    /// <para/>
    /// This event is invoked only once when the <see cref="InputManager"/> has completed initialization since it's a
    /// singleton.
    /// </summary>
    public event Action<InputManager> InputManagerAwake
    {
        add => _inputManagerAwakeListeners.Add(value);
        remove => _inputManagerAwakeListeners.Remove(value);
    }

    /// <inheritdoc cref="InputRebindUIRegistry"/>
    public InputRebindUIRegistry()
    {
#if DEBUG
        VerboseLogging = true;
#endif

        _method_OptionWindow_OnAwake_SetInput = typeof(OptionWindow).GetLocalMethod(
            $"<{nameof(OptionWindow.OnAwake)}>g__SetInput",
            [typeof(GoTable), typeof(InputAction), typeof(int)]);
        var method_OptionWindow_OnAwake_SetControl = typeof(OptionWindow).GetLocalMethod(
            $"<{nameof(OptionWindow.OnAwake)}>g__SetControl",
            []);

        // Default doesn't include NonPublic I think
        var instanceBindingAttr = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // MonoMod is used instead of Harmony since we want to manage the hooks ourselves here in the class instance and
        // there's no way to skip Harmony patches from being included in PatchAll(Assembly.GetExecutingAssembly()).
        _detours.AddRange([
            new Hook(
                typeof(InputManager).GetMethod(nameof(InputManager.Awake), instanceBindingAttr),
                Hook_InputManager_Awake),
            new Hook(
                typeof(OptionWindow).GetMethod(nameof(OptionWindow.OnAwake), instanceBindingAttr),
                Hook_OptionWindow_OnAwake),
            new Hook(method_OptionWindow_OnAwake_SetControl, Hook_OptionWindow_OnAwake_CreateControl)
        ]);
    }

    public void Dispose()
    {
        SaveBindingOverrides();

        foreach (var detour in _detours)
            detour.Dispose();
        _detours.Clear();
    }

    /// <summary>
    /// <paramref name="id"/> must be unique among all registrations done via <see cref="InputRebindUIRegistry"/>. 
    /// <para/>
    /// If <paramref name="id"/> is null, <see cref="InputAction.name"/> will be used in its place.
    /// <para/>
    /// </summary>
    /// <param name="filepath">
    /// If provided, the binding for the action is stored at this filepath.
    /// </param>
    public void RegisterRebindableAction(
        string displayName,
        InputAction action,
        string? id = null,
        RebindUIPosition? position = null,
        string? filepath = null)
    {
        try
        {
            if (filepath != null
                && !_bindingOverridesByFile.TryGetValue(filepath, out _)
                && File.Exists(filepath))
            {
                var text = File.ReadAllText(filepath);
                if (!string.IsNullOrEmpty(text))
                {
                    var obj = JsonConvert.DeserializeObject(text, typeof(BindingOverrides));
                    if (obj != null)
                        _bindingOverridesByFile[filepath] = (BindingOverrides)obj;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(ex);
        }

        id ??= action.name;
        Debug.Log($"Registering rebindable action: id=\"{id}\" displayName=\"{displayName}\"");
        _actionsNameMap[id] = new(action, displayName, position ?? RebindUIPosition.End(), filepath);

        if (filepath != null)
        {
            if (_bindingOverridesByFile.TryGetValue(filepath, out var bindingOverrides))
            {
                if (bindingOverrides.Bindings.TryGetValue(id, out var bindings))
                {
                    foreach (var binding in bindings)
                    {
                        if (VerboseLogging)
                            Debug.Log($"Overriding binding: id=\"{id}\" index={binding.Index} path=\"{binding.Path}\"");
                        if (!string.IsNullOrEmpty(binding.Path))
                            action.ApplyBindingOverride(binding.Index, binding.Path);
                    }
                }
            }
            else
            {
                _bindingOverridesByFile[filepath] = new([]);
            }
        }
    }

    private void SaveBindingOverrides()
    {
        foreach (var (filepath, bindingOverrides) in _bindingOverridesByFile)
        {
            // Populate bindings
            foreach (var (id, rebindable) in _actionsNameMap)
            {
                bindingOverrides.Bindings[id] = [.. rebindable.Action.bindings
                        .Select((b, i) => new BindingOverride(i,
                            string.IsNullOrEmpty(b.overridePath) ? b.path : b.overridePath))];
            }

            try
            {
                File.WriteAllText(filepath, JsonConvert.SerializeObject(bindingOverrides));
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }
    }

    private void Hook_InputManager_Awake(Action<InputManager> orig, InputManager self)
    {
        orig(self);

        self.m_asset.Disable(); // To allow adding to InputManager's InputActionMap
        foreach (var listener in _inputManagerAwakeListeners)
        {
            try
            {
                listener(self);
            }
            catch (Exception ex)
            {
                Debug.Log(ex);
            }
        }
        self.m_asset.Enable();

        _inputManagerAwakeListeners.Clear();
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

        try
        {
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
                    if (VerboseLogging)
                        Debug.Log(
                            "Updating custom control: " +
                            $"id=\"{childGoTable.gameObject.name}\" goTableName=\"{childGoTable.name}\"");
                    // XXX Could just have a hardcoded copy of this SetInput local method instead of calling it via
                    // reflection.
                    _method_OptionWindow_OnAwake_SetInput.Invoke(self, [childGoTable, action, 0]);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError(ex);
        }
    }

    private void CreateControlGameObjects(GameObject prefab)
    {
        static int GetSiblingIndexByName(Transform parent, string? childGoName)
        {
            if (childGoName == null) return parent.childCount;
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child.name == childGoName)
                    return child.GetSiblingIndex();
            }
            return parent.childCount;
        }

        if (_controlPrefab == null)
        {
            _controlPrefab = UnityEngine.Object.Instantiate(prefab);
            _controlPrefab.SetActive(false);
            _controlPrefab.name = $"{nameof(InputRebindUIRegistry)}_Control_Prefab";
        }

        if (VerboseLogging)
            Debug.Log($"Pending control count: {_actionsNameMap.Count}");
        foreach (var (id, rebindable) in _actionsNameMap)
        {
            if (VerboseLogging)
                Debug.Log($"Creating custom control GameObject: id=\"{id}\"");

            var parent = prefab.transform.parent;
            var go = UnityEngine.Object.Instantiate(_controlPrefab, parent);
            go.name = id;

            var langKey = go.transform.GetComponentInChildren<LanguageKey>();
            langKey.GetComponent<Text>().text = rebindable.DisplayName;

            switch (rebindable.Position.Type)
            {
                case RebindUIPosition.Kind.Index:
                    go.transform.SetSiblingIndex(rebindable.Position.Index);
                    break;
                case RebindUIPosition.Kind.Before:
                    go.transform.SetSiblingIndex(GetSiblingIndexByName(parent, rebindable.Position.Target));
                    break;
                case RebindUIPosition.Kind.After:
                    go.transform.SetSiblingIndex(GetSiblingIndexByName(parent, rebindable.Position.Target) + 1);
                    break;
                case RebindUIPosition.Kind.End:
                    break;
            }

            go.SetActive(true);

            _goTableActions.Add(go.GetComponent<GoTable>(), rebindable.Action);
        }
    }
}

internal readonly struct RebindUIPosition
{
    public enum Kind
    {
        End,
        Index,
        Before,
        After
    }

    public Kind Type { get; }
    public int Index { get; }
    public string? Target { get; }

    public static RebindUIPosition AtIndex(int index) => new(Kind.Index, index);
    public static RebindUIPosition Before(string target) => new(Kind.Before, 0, target);
    public static RebindUIPosition After(string target) => new(Kind.After, 0, target);
    public static RebindUIPosition End() => new(Kind.End, 0);

    private RebindUIPosition(Kind type, int index, string? target = null)
    {
        Type = type;
        Index = index;
        Target = target;
    }
}