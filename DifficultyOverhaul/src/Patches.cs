using HarmonyLib;
using UnityEngine;

namespace DifficultyOverhaul.Patches;

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
    private static void Postfix(Role __instance, Fight.CampType camp, bool ai)
    {
        // NOTE: This ctor postfix patch gets invoked twice per each new Role instance (newobj) *if* melonloader is used
        // for the plugin/patch. This issue doesn't occur on BepInEx, I tested. Why? How? Idk.
        var self = __instance;
        var cfg = ModConfig.Instance;
        if (ai
            && (cfg.StatMultiplierScope.Value == ModConfig.StatMultiplierConstraint.AllAi
            || camp == Fight.CampType.Enermy))
        {
            self.m_maxHp = Mathf.RoundToInt(self.m_maxHp * cfg.HpMultiplier.Value);
            self.m_maxMp = Mathf.RoundToInt(self.m_maxMp * cfg.MpMultiplier.Value);
            self.m_damage = Mathf.RoundToInt(self.m_damage * cfg.AttackMultiplier.Value);
            self.m_atk = Mathf.RoundToInt(self.m_atk * cfg.PierceMultiplier.Value);
            self.m_def = Mathf.RoundToInt(self.m_def * cfg.DefenseMultiplier.Value);
            self.m_speed = Mathf.RoundToInt(self.m_speed * cfg.SpeedMultiplier.Value);
            self.m_curHp = self.m_maxHp;
            self.m_curMp = self.m_maxMp;
        }
    }
}