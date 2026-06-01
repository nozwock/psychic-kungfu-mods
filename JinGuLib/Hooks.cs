using System;
using System.Reflection;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine.InputSystem;

namespace JinGuLib.Hooks;

internal class Fix_LoadBindingOverridesFromJsonInternal
{
    private static ILHook? _hook;

    public static void Apply()
    {
        _hook = new ILHook(
            typeof(InputActionRebindingExtensions).GetMethod(
                "LoadBindingOverridesFromJsonInternal",
                BindingFlags.Static | BindingFlags.NonPublic
            ),
            Manipulator
        );
    }

    public static void Undo()
    {
        _hook?.Dispose();
        _hook = null;
    }

    private static void Manipulator(ILContext il)
    {
        var c = new ILCursor(il);

        c.GotoNext(
            MoveType.Before,
            i => i.MatchNewobj<NotImplementedException>(),
            i => i.MatchThrow()
        );

        // This random thrown NotImplementedException doesn't even make sense. And, it breaks InputManager's Awake since
        // it's not wrapping the call in a try-catch.
        var newObj = c.Next;
        var @throw = newObj.Next;

        c.MoveAfterLabels();
        c.Emit(OpCodes.Br_S, @throw.Next);
    }
}
