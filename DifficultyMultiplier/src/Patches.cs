using DBLoad;
using HarmonyLib;

namespace DifficultyMultiplier.Patches;

[HarmonyPatch(
    typeof(DifficultData),
    MethodType.Constructor,
[
        typeof(int),
        typeof(string),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(int),
        typeof(string),
        typeof(int)
    ])]
internal class DifficultData_ctro_Patch
{
    private static void Prefix(ref int _hp,
        ref int _atk,
        ref int _def,
        ref int _speed)
    {
        var cfg = Config.Instance;
        _hp = (int)(_hp * cfg.HpMultiplier.Value);
        _atk = (int)(_atk * cfg.AtkMultiplier.Value);
        _def = (int)(_def * cfg.DefMultiplier.Value);
        _speed = (int)(_speed * cfg.SpeedMultiplier.Value);
    }
}