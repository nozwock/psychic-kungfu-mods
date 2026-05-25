using HarmonyLib;
using UnityEngine;

namespace DifficultyMultiplier.Patches;

[HarmonyPatch(
    typeof(Role),
    MethodType.Constructor,
[
        typeof(int),
        typeof(Fight.CampType),
        typeof(GameObject),
        typeof(Fight.Cell),
        typeof(Fight.Orientation),
        typeof(bool)
    ])]
internal class Role_ctor_Patch
{
    private static void Postfix(Role __instance, bool ai)
    {
        var self = __instance;
        if (ai)
        {
            var cfg = Config.Instance;
            self.m_maxHp = Mathf.RoundToInt(self.m_maxHp * cfg.HpMultiplier.Value);
            self.m_maxMp = Mathf.RoundToInt(self.m_maxMp * cfg.MpMultiplier.Value);
            self.m_atk = Mathf.RoundToInt(self.m_atk * cfg.AtkMultiplier.Value);
            self.m_def = Mathf.RoundToInt(self.m_def * cfg.DefMultiplier.Value);
            self.m_speed = Mathf.RoundToInt(self.m_speed * cfg.SpeedMultiplier.Value);
            self.m_curHp = self.m_maxHp;
            self.m_curMp = self.m_maxMp;
        }
    }
}