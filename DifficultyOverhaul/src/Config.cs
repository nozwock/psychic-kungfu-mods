using BepInEx.Configuration;

namespace DifficultyOverhaul;

internal class ModConfig
{
    public enum StatMultiplierConstraint
    {
        OnlyEnemyAi,
        AllAi,
    }

    private static ModConfig? _instance;
    public static ModConfig Instance => _instance ??= new(Plugin.Instance.Config);

    public ConfigEntry<StatMultiplierConstraint> StatMultiplierScope { get; }

    public ConfigEntry<float> HpMultiplier { get; }
    public ConfigEntry<float> MpMultiplier { get; }
    public ConfigEntry<float> AttackMultiplier { get; }
    public ConfigEntry<float> PierceMultiplier { get; }
    public ConfigEntry<float> DefenseMultiplier { get; }
    public ConfigEntry<float> SpeedMultiplier { get; }

    private const string _sectionGeneral = "General";
    private const string _sectionStatMultipliers = "StatMultipliers";

    private ModConfig(ConfigFile config)
    {
        StatMultiplierScope = config.Bind(
            _sectionGeneral,
            "Scope",
            StatMultiplierConstraint.OnlyEnemyAi);

        HpMultiplier = config.Bind(_sectionStatMultipliers, "Hp", 1.0f);
        MpMultiplier = config.Bind(_sectionStatMultipliers, "Mp", 1.0f);
        AttackMultiplier = config.Bind(_sectionStatMultipliers, "Attack", 1.0f);
        PierceMultiplier = config.Bind(_sectionStatMultipliers, "Pierce", 1.0f);
        DefenseMultiplier = config.Bind(_sectionStatMultipliers, "Defense", 1.0f);
        SpeedMultiplier = config.Bind(_sectionStatMultipliers, "Speed", 1.0f);
    }
}