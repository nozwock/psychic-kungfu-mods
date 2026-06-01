using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace JinGuLib.Patches;

[HarmonyPatch(typeof(InputActionRebindingExtensions), "LoadBindingOverridesFromJsonInternal")]
internal class InputActionRebindingExtensions_LoadBindingOverridesFromJsonInternal_Patch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions
    )
    {
        var codes = instructions.ToArray();
        var ctorNotImplementedException = typeof(NotImplementedException).GetConstructor(types: []);

        for (var i = 0; i < codes.Length; i++)
        {
            var code = codes[i];

            // This random thrown NotImplementedException doesn't even make sense. And, it breaks InputManager's Awake
            // since it's not wrapping the call in a try-catch.
            if (
                i + 1 <= codes.Length
                && code.opcode == OpCodes.Newobj
                && code.operand is ConstructorInfo ctor
                && ctor == ctorNotImplementedException
                && codes[i + 1].opcode == OpCodes.Throw
            )
            {
                code.opcode = OpCodes.Nop;
                code.operand = null;
                codes[i + 1].opcode = OpCodes.Nop;
                codes[i + 1].operand = null;
            }

            yield return code;
        }
    }
}
