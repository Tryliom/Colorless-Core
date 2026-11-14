#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SpriteNormalMapAssigner
{
    [MenuItem("Tools/Assign Normal Maps to Sprites")]
    private static void AssignNormalMapsToSprites()
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D");
        var count = 0;

        try
        {
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);

                EditorUtility.DisplayProgressBar(
                    "Assigning Normal Maps...",
                    path,
                    (float)i / guids.Length
                );
                
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (fileName.EndsWith("_n")) continue;

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                
                if (!importer || importer.textureType != TextureImporterType.Sprite) continue;
                
                var directory = Path.GetDirectoryName(path)?.Replace("\\", "/");
                var extension = Path.GetExtension(path);
                var normalMapPath = $"{directory}/{fileName}_n{extension}";
                var normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalMapPath);
                
                if (!normalMapTexture) continue;
                
                var existing = importer.secondarySpriteTextures;
                var alreadyAssigned = false;

                foreach (var t in existing)
                {
                    if (t.name != "_NormalMap" || t.texture != normalMapTexture) continue;
                    
                    alreadyAssigned = true;
                    break;
                }

                if (alreadyAssigned) continue;
                
                var normalMapIndex = -1;
                
                for (var j = 0; j < existing.Length; j++)
                {
                    if (existing[j].name != "_NormalMap") continue;
                
                    normalMapIndex = j;
                    break;
                }

                SecondarySpriteTexture[] updated;

                if (normalMapIndex >= 0)
                {
                    updated = existing;
                    updated[normalMapIndex] = new SecondarySpriteTexture
                    {
                        name = "_NormalMap",
                        texture = normalMapTexture
                    };
                }
                else
                {
                    updated = new SecondarySpriteTexture[existing.Length + 1];
                    existing.CopyTo(updated, 0);
                    updated[existing.Length] = new SecondarySpriteTexture
                    {
                        name = "_NormalMap",
                        texture = normalMapTexture
                    };
                }

                importer.secondarySpriteTextures = updated;
                importer.SaveAndReimport();
                count++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"[SpriteNormalMapAssigner] {count} sprite updated on {guids.Length} found.");
    }
}
#endif