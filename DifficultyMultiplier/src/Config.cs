using System;
using MelonLoader;

namespace DifficultyMultiplier;

class Config
{
    private static readonly Lazy<Config> _instance = new(() => new Config());

    public static Config Instance => _instance.Value;

    private MelonPreferences_Category Category { get; }

    public MelonPreferences_Entry<float> HpMultiplier { get; }
    public MelonPreferences_Entry<float> AtkMultiplier { get; }
    public MelonPreferences_Entry<float> DefMultiplier { get; }
    public MelonPreferences_Entry<float> SpeedMultiplier { get; }

    private Config()
    {
        Category = MelonPreferences.CreateCategory("DifficultyMultiplier");

        HpMultiplier = Category.CreateEntry(
            "Hp",
            1.0f,
            description: "Difficulty HP stat is used for MP as well by the game.");
        AtkMultiplier = Category.CreateEntry("Atk", 1.0f);
        DefMultiplier = Category.CreateEntry("Def", 1.0f);
        SpeedMultiplier = Category.CreateEntry("Speed", 1.0f);

        Category.SetFilePath("UserData/DifficultyMultiplier.cfg");
        Category.SaveToFile();
    }
}