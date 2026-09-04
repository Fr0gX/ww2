using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WW2.Editor
{
    public static class ArtDiagnostics
    {
        private static readonly string[] RuntimeTexturePaths =
        {
            "Assets/WW2/Resources/ArtV6/Terrain/forest-overlay-v2.png",
            "Assets/WW2/Resources/ArtV6/Terrain/hill-overlay-v2.png",
            "Assets/WW2/Resources/ArtV6/Terrain/mountain-overlay-v2.png",
            "Assets/WW2/Resources/ArtV6/Terrain/marsh-overlay.png",
            "Assets/WW2/Resources/ArtV6/Units/main-infantry.png",
            "Assets/WW2/Resources/ArtV6/Units/medic.png",
            "Assets/WW2/Resources/ArtV6/Units/light-artillery.png",
            "Assets/WW2/Resources/ArtV6/Units/light-armor.png",
            "Assets/WW2/Resources/Art/FX/dirt_01.png",
            "Assets/WW2/Resources/Art/FX/dirt_03.png",
            "Assets/WW2/Resources/Art/FX/fire_01.png",
            "Assets/WW2/Resources/Art/FX/muzzle_01.png",
            "Assets/WW2/Resources/Art/FX/muzzle_03.png",
            "Assets/WW2/Resources/Art/FX/smoke_04.png",
            "Assets/WW2/Resources/Art/FX/smoke_07.png",
            "Assets/WW2/Resources/Art/FX/spark_06.png"
        };

        [MenuItem("WW2/Run Art Diagnostics")]
        public static void Run()
        {
            var lines = new List<string>();
            foreach (var path in RuntimeTexturePaths)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) throw new InvalidDataException($"Missing runtime art asset: {path}");
                lines.Add($"TEXTURE {Path.GetFileName(path)} size={texture.width}x{texture.height}");
            }

            Directory.CreateDirectory("Tools");
            File.WriteAllLines("Tools/art-diagnostic.log", lines);
            Debug.Log($"Art diagnostics passed ({lines.Count} runtime textures)");
        }
    }
}
