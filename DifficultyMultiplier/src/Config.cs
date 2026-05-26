using System;
using MelonLoader;

namespace DifficultyMultiplier;

class Config
{
    public enum StatMultiplierConstraint
    {
        OnlyEnemyAi,
        AllAi,
    }

    private static readonly string _filename = $"UserData/{Plugin.Id}.cfg";

    private static readonly Lazy<Config> _instance = new(() => new Config());

    public static Config Instance => _instance.Value;

    private MelonPreferences_Category CategoryGeneral { get; }
    public MelonPreferences_Entry<StatMultiplierConstraint> StatMultiplierScope { get; }

    private MelonPreferences_Category CategoryStatMultipliers { get; }
    public MelonPreferences_Entry<float> HpMultiplier { get; }
    public MelonPreferences_Entry<float> MpMultiplier { get; }
    public MelonPreferences_Entry<float> AtkMultiplier { get; }
    public MelonPreferences_Entry<float> DefMultiplier { get; }
    public MelonPreferences_Entry<float> SpeedMultiplier { get; }

    private Config()
    {
        CategoryGeneral = MelonPreferences.CreateCategory("General");
        StatMultiplierScope = CategoryGeneral.CreateEntry(
            "Scope",
            StatMultiplierConstraint.OnlyEnemyAi,
            description: $"Valid values: {string.Join(", ", Enum.GetNames(typeof(StatMultiplierConstraint)))}");

        CategoryStatMultipliers = MelonPreferences.CreateCategory("StatMultipliers");
        HpMultiplier = CategoryStatMultipliers.CreateEntry("Hp", 1.0f);
        MpMultiplier = CategoryStatMultipliers.CreateEntry("Mp", 1.0f);
        AtkMultiplier = CategoryStatMultipliers.CreateEntry("Atk", 1.0f);
        DefMultiplier = CategoryStatMultipliers.CreateEntry("Def", 1.0f);
        SpeedMultiplier = CategoryStatMultipliers.CreateEntry("Speed", 1.0f);

        CategoryGeneral.SetFilePath(_filename);
        CategoryStatMultipliers.SetFilePath(_filename);

        CategoryGeneral.SaveToFile();
        CategoryStatMultipliers.SaveToFile();
    }
}