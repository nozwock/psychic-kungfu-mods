using System.IO;
using System.Linq;
using Common.Extensions;
using UnityEngine;

namespace VanillaPlus.Save;

internal static class SaveManagerExtensions
{
    public static SaveData? ReadRecentSaveData(this SaveManager self, string[]? searchPaths = null)
    {
        var mostRecent = (searchPaths ?? [self.SavePath, self.FixedPath, self.AutoPath])
            .SelectMany(path => Directory.EnumerateFiles(path, "*.meta.json"))
            .AsParallel()
            .Select(path => (Meta: (SaveMetadata?)SaveMetadata.Read(path), Path: path))
            .DefaultIfEmpty((null, ""))
            .Aggregate(
                (a, b) =>
                {
                    if (a.Meta == null)
                        return b;
                    if (b.Meta == null)
                        return a;
                    return a.Meta.SaveTime > b.Meta.SaveTime ? a : b;
                }
            );

        if (mostRecent.Meta != null)
        {
            var savefile = Path.Combine(mostRecent.Path.RemoveSuffix(".meta.json") + ".bytes");
            return SaveManager.Instance.Load(
                Path.GetDirectoryName(savefile),
                Path.GetFileName(savefile)
            );
        }

        return null;
    }
}

internal static class SaveDataExtensions
{
    public static SaveMetadata ToMetadata(this SaveData save) => (SaveMetadata)save;

    public static void Write(this SaveData save, string filepath)
    {
        save.m_path = filepath;
        File.WriteAllBytes(filepath, SaveManager.Instance.Encrypt(JsonUtility.ToJson(save)));
        save.ToMetadata().Write(filepath.RemoveSuffix(".bytes") + ".meta.json");
    }
}
