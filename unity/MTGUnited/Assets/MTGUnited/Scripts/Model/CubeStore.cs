using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>A saved cube: a name and its card list (one card per line, "1 Name" or just "Name").</summary>
    public class SavedCube
    {
        public string File, Name, List = "";
        public int Count => DeckTextImport.Parse(List ?? "", DeckTextFormat.Plain).Cards.Sum(c => c.count);
    }

    /// <summary>Cubes kept as plain .txt files in the app's data folder (Limited tab: import, export, delete).</summary>
    public static class CubeStore
    {
        static string Dir => Path.Combine(Application.persistentDataPath, "cubes");
        public static event Action Changed;

        public static List<SavedCube> LoadAll()
        {
            var list = new List<SavedCube>();
            try
            {
                if (!Directory.Exists(Dir)) return list;
                foreach (var f in Directory.GetFiles(Dir, "*.txt"))
                    list.Add(new SavedCube { File = f, Name = Path.GetFileNameWithoutExtension(f), List = System.IO.File.ReadAllText(f) });
            }
            catch (Exception ex) { Debug.LogWarning("Couldn't read cubes: " + ex.Message); }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Saves (or replaces) a cube under this name. Returns it.</summary>
        public static SavedCube Save(string name, string list)
        {
            Directory.CreateDirectory(Dir);
            name = Safe(name);
            var path = Path.Combine(Dir, name + ".txt");
            System.IO.File.WriteAllText(path, (list ?? "").Replace("\r\n", "\n"), new UTF8Encoding(false));
            Changed?.Invoke();
            return new SavedCube { File = path, Name = name, List = list };
        }

        public static void Delete(SavedCube c)
        {
            try { if (System.IO.File.Exists(c.File)) System.IO.File.Delete(c.File); } catch (Exception ex) { Debug.LogWarning(ex.Message); }
            Changed?.Invoke();
        }

        static string Safe(string name)
        {
            name = (name ?? "").Trim();
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '-');
            return name.Length == 0 ? "Cube" : name;
        }
    }
}
