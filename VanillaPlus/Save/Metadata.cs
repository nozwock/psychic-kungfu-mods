using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Common.Extensions;
using Newtonsoft.Json;

namespace VanillaPlus.Save;

public record class SaveMetadata(
    string Name,
    string Surname,
    string Forename,
    int GameTime,
    long SaveTime,
    DiffucultEnum Difficulty,
    SceneEnum Scene,
    int ShowMainId
)
{
    [JsonIgnore]
    public string? Filepath { get; set; }

    public static implicit operator SaveMetadata(SaveData save) =>
        new(
            Name: save.m_name,
            Surname: save.m_leaderFamily,
            Forename: save.m_leaderName,
            GameTime: save.m_gameTime,
            SaveTime: save.m_saveTime,
            Difficulty: save.m_diffucultEnum,
            Scene: save.m_scene,
            ShowMainId: save.m_showMainId
        )
        {
            Filepath = save.m_path.RemoveSuffix(".bytes") + ".meta.json",
        };

    public static SaveMetadata Read(string filepath)
    {
        var metadata =
            (SaveMetadata?)
                JsonConvert.DeserializeObject(File.ReadAllText(filepath), typeof(SaveMetadata))
            ?? throw new InvalidDataException(
                $"{nameof(SaveMetadata)} file '{filepath}' contained null json"
            );
        metadata.Filepath = filepath;
        return metadata;
    }

    public void Write(string filepath) =>
        File.WriteAllText(filepath, JsonConvert.SerializeObject(this));

    public static Task WriteMissingMetadataFilesAsync()
    {
        var saveManager = SaveManager.Instance;
        string[] searchPaths = [saveManager.SavePath, saveManager.FixedPath, saveManager.AutoPath];
        return Task.Run(
            delegate
            {
                Parallel.ForEach(
                    searchPaths.SelectMany(path => Directory.EnumerateFiles(path, "*.bytes")),
                    savefile =>
                    {
                        var metafile = savefile.RemoveSuffix(".bytes") + ".meta.json";
                        if (!File.Exists(metafile))
                        {
                            saveManager
                                .Load(
                                    Path.GetDirectoryName(savefile),
                                    Path.GetFileNameWithoutExtension(savefile) + ".meta.json"
                                )
                                ?.ToMetadata()
                                .Write(metafile);
                        }
                    }
                );
            }
        );
    }
}
