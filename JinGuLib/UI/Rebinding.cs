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

namespace JinGuLib.UI;

public static class RebindRegistry
{
    private record class RebindableAction(
        InputAction Action,
        string DisplayName,
        RebindPosition Position,
        string? Filepath
    );

    private sealed record class BindingOverride(int Index, string Path);

    private sealed record class BindingOverrides(
        Dictionary<string, List<BindingOverride>> Bindings
    );

    public static bool VerboseLogging { get; set; }

    private static readonly Dictionary<string, RebindableAction> _actionsNameMap = [];
    private static readonly Dictionary<string, BindingOverrides> _bindingOverridesByFile = [];
    private static GameObject? _controlPrefab;

    static RebindRegistry()
    {
#if DEBUG
        VerboseLogging = true;
#endif
    }

    /// <summary>
    /// <paramref name="id"/> must be unique among all registrations done via <see cref="RebindRegistry"/>.
    /// <para/>
    /// If <paramref name="id"/> is null, <see cref="InputAction.name"/> will be used in its place.
    /// <para/>
    /// Tip: You may register <see cref="InputAction"/> instances to the <see cref="InputManager"/>'s <see
    /// cref="InputActionMap"/>s in your plugin's <c>Start</c> method (not <c>Awake</c> or earlier).  When doing so,
    /// temporarily disable <see cref="InputManager.m_asset"/> before adding them, then re-enable it afterward, since
    /// new <see cref="InputAction"/> instances cannot be added while any actions are enabled.
    /// </summary>
    /// <param name="filepath">
    /// If provided, the binding for the action is stored at this filepath.
    /// </param>
    public static void AddRebindableAction(
        string displayName,
        InputAction action,
        string? id = null,
        RebindPosition? position = null,
        string? filepath = null
    )
    {
        try
        {
            if (
                filepath != null
                && !_bindingOverridesByFile.TryGetValue(filepath, out _)
                && File.Exists(filepath)
            )
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
        _actionsNameMap[id] = new(action, displayName, position ?? RebindPosition.End(), filepath);

        if (
            filepath != null
            && _bindingOverridesByFile.TryGetValue(filepath, out var bindingOverrides)
            && bindingOverrides.Bindings.TryGetValue(id, out var bindings)
        )
        {
            foreach (var binding in bindings)
            {
                if (VerboseLogging)
                    Debug.Log(
                        $"Overriding binding: id=\"{id}\" index={binding.Index} path=\"{binding.Path}\""
                    );
                if (!string.IsNullOrEmpty(binding.Path))
                    action.ApplyBindingOverride(binding.Index, binding.Path);
            }
        }
    }

    internal static void SaveBindingOverrides()
    {
        Debug.Log("Saving bindings for custom InputActions...");
        var actionsNameMapByFile = _actionsNameMap
            .Where(pair => !string.IsNullOrEmpty(pair.Value.Filepath))
            .GroupBy(pair => pair.Value.Filepath!)
            .ToDictionary(
                group => group.Key,
                group => group.ToDictionary(pair => pair.Key, pair => pair.Value)
            );

        foreach (var (filepath, actionsNameMap) in actionsNameMapByFile)
        {
            if (!_bindingOverridesByFile.TryGetValue(filepath, out _))
            {
                _bindingOverridesByFile[filepath] = new([]);
            }

            var bindingOverrides = _bindingOverridesByFile[filepath];

            // Populate bindings
            foreach (var (id, rebindable) in actionsNameMap)
            {
                bindingOverrides.Bindings[id] =
                [
                    .. rebindable.Action.bindings.Select(
                        (b, i) =>
                            new BindingOverride(
                                i,
                                string.IsNullOrEmpty(b.overridePath) ? b.path : b.overridePath
                            )
                    ),
                ];
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

    internal static IEnumerable<(GameObject Go, InputAction Action)> CreateAllControlGameObjects(
        GameObject controlGo,
        bool resetPrefab = false
    )
    {
        static int GetSiblingIndexByName(Transform parent, string? childGoName)
        {
            if (childGoName == null)
                return parent.childCount;
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child.name == childGoName)
                    return child.GetSiblingIndex();
            }
            return parent.childCount;
        }

        if (resetPrefab && _controlPrefab != null)
        {
            UnityEngine.Object.DestroyImmediate(_controlPrefab);
            _controlPrefab = null;
        }
        if (_controlPrefab == null)
        {
            _controlPrefab = UnityEngine.Object.Instantiate(controlGo);
            _controlPrefab.SetActive(false);
            _controlPrefab.name = $"{nameof(RebindRegistry)}_Control_Prefab";
        }

        if (VerboseLogging)
            Debug.Log($"Pending control count: {_actionsNameMap.Count}");
        foreach (var (id, rebindable) in _actionsNameMap)
        {
            if (VerboseLogging)
                Debug.Log($"Creating custom control GameObject: id=\"{id}\"");

            var parent = controlGo.transform.parent;
            var go = UnityEngine.Object.Instantiate(_controlPrefab, parent);
            go.name = id;

            var langKey = go.transform.GetComponentInChildren<LanguageKey>();
            langKey.GetComponent<Text>().text = rebindable.DisplayName;

            switch (rebindable.Position.Type)
            {
                case RebindPosition.Kind.Index:
                    go.transform.SetSiblingIndex(rebindable.Position.Index);
                    break;
                case RebindPosition.Kind.Before:
                    go.transform.SetSiblingIndex(
                        GetSiblingIndexByName(parent, rebindable.Position.Target)
                    );
                    break;
                case RebindPosition.Kind.After:
                    go.transform.SetSiblingIndex(
                        GetSiblingIndexByName(parent, rebindable.Position.Target) + 1
                    );
                    break;
                case RebindPosition.Kind.End:
                    break;
            }

            yield return (go, rebindable.Action);
        }
    }
}

internal sealed class RebindUILifecycle : IDisposable
{
    private readonly List<IDetour> _detours = [];
    private readonly ConditionalWeakTable<GoTable, InputAction> _goTableActions = new();
    private readonly MethodInfo _method_OptionWindow_OnAwake_SetInput;
    private FieldInfo? _field_OptionWindow_OnAwake_goTable;

    public RebindUILifecycle()
    {
        _method_OptionWindow_OnAwake_SetInput = typeof(OptionWindow).GetLocalMethod(
            $"<{nameof(OptionWindow.OnAwake)}>g__SetInput",
            [typeof(GoTable), typeof(InputAction), typeof(int)]
        );
        var method_OptionWindow_OnAwake_SetControl = typeof(OptionWindow).GetLocalMethod(
            $"<{nameof(OptionWindow.OnAwake)}>g__SetControl",
            []
        );

        // Default doesn't include NonPublic I think
        var instanceBindingAttr =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // MonoMod is used instead of Harmony since we want to manage the hooks ourselves here in the class instance and
        // there's no way to skip Harmony patches from being included in PatchAll(Assembly.GetExecutingAssembly()).
        _detours.AddRange([
            new Hook(
                typeof(OptionWindow).GetMethod(nameof(OptionWindow.OnAwake), instanceBindingAttr),
                Hook_OptionWindow_OnAwake
            ),
            new Hook(
                method_OptionWindow_OnAwake_SetControl,
                Hook_OptionWindow_OnAwake_CreateControl
            ),
        ]);
    }

    public void Dispose()
    {
        foreach (var detour in _detours)
            detour.Dispose();
        _detours.Clear();
    }

    private void Hook_OptionWindow_OnAwake(Action<OptionWindow> orig, OptionWindow self)
    {
        try
        {
            var goTable = self.transform.GetComponent<GoTable>();
            var controlPageGoTable = goTable.GetNode<GoTable>("ControlPage_GoTable");
            var controlGoTable = controlPageGoTable
                .m_list.Select(it => it.obj)
                .OfType<GoTable>()
                .First();

            foreach (
                var (go, action) in RebindRegistry.CreateAllControlGameObjects(
                    controlGoTable.gameObject
                )
            )
            {
                go.SetActive(true);
                _goTableActions.Add(go.GetComponent<GoTable>(), action);
            }
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
            var controlGoTable = controlPageGoTable
                .m_list.Select(it => it.obj)
                .OfType<GoTable>()
                .First();

            var controlsContainer = controlGoTable.transform.parent;
            for (var i = controlsContainer.childCount - 1; i >= 0; i--)
            {
                var childGoTable = controlsContainer.GetChild(i).GetComponent<GoTable>();
                if (_goTableActions.TryGetValue(childGoTable, out var action))
                {
                    if (RebindRegistry.VerboseLogging)
                        Debug.Log(
                            "Updating custom control: "
                                + $"id=\"{childGoTable.gameObject.name}\" goTableName=\"{childGoTable.name}\""
                        );
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
}

public readonly struct RebindPosition
{
    public enum Kind
    {
        End,
        Index,
        Before,
        After,
    }

    public Kind Type { get; }
    public int Index { get; }
    public string? Target { get; }

    public static RebindPosition AtIndex(int index) => new(Kind.Index, index);

    /// <summary>
    /// <paramref name="target"/> is the name of a rebinding control GameObject within the Controls tab of the Settings
    /// page. The control is expected to be a child of: "OptionWindow(Clone)/ControlPage/Scroll/View/Content/".
    /// </summary>
    public static RebindPosition Before(string target) => new(Kind.Before, 0, target);

    /// <inheritdoc cref="RebindPosition.Before(string)"/>
    public static RebindPosition After(string target) => new(Kind.After, 0, target);

    public static RebindPosition End() => new(Kind.End, 0);

    private RebindPosition(Kind type, int index, string? target = null)
    {
        Type = type;
        Index = index;
        Target = target;
    }
}
