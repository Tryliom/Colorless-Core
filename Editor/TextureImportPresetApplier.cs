#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility that applies a standard set of import settings to weapon sprite
/// textures located under "Assets/Textures/Weapons", via a right-click context menu.
/// </summary>
public static class TextureImportPresetApplier
{
    // -------------------------------------------------------------------------
    // Menu items — context menu on selected texture assets
    // -------------------------------------------------------------------------

    [MenuItem("Assets/Apply Multi Sprite Import Preset", true)]
    private static bool ValidateSelectedTextures()
    {
        var paths = Selection.assetGUIDs;

        return paths != null && paths.Length != 0;
    }

    [MenuItem("Assets/Apply Multi Sprite Import Preset")]
    private static void ApplyPresetToSelection()
    {
        var guids = Selection.assetGUIDs;

        try
        {
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);

                EditorUtility.DisplayProgressBar(
                    "Applying Multi Sprite Preset...",
                    path,
                    (float)i / guids.Length
                );

                ApplyPreset(path);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
    }

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies the standard weapon sprite import preset to the texture at the given path.
    /// </summary>
    /// <param name="path">Project-relative asset path of the texture.</param>
    /// <param name="spriteMode">Sprite import mode to apply (Single by default).</param>
    internal static void ApplyPreset(string path, SpriteImportMode spriteMode = SpriteImportMode.Single)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (!importer) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.mipmapEnabled = false;
        importer.isReadable = true;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;

        // Sprite-specific settings (mode, mesh type, extrude, physics shape) live on
        // the TextureImporterSettings struct rather than directly on the importer.
        var textureSettings = new TextureImporterSettings();
        
        importer.ReadTextureSettings(textureSettings);

        textureSettings.spriteMode = (int)spriteMode;
        textureSettings.spritePixelsPerUnit = 32f;
        textureSettings.spriteMeshType = SpriteMeshType.FullRect;
        textureSettings.spriteExtrude = 0;
        textureSettings.spriteGenerateFallbackPhysicsShape = false;

        importer.SetTextureSettings(textureSettings);

        var platformSettings = importer.GetDefaultPlatformTextureSettings();
        
        platformSettings.maxTextureSize = GetMaxSizeForTexture(path);
        platformSettings.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformTextureSettings(platformSettings);

        EditorUtility.SetDirty(importer);
        importer.SaveAndReimport();

    }

    /// <summary>
    /// Computes the smallest power-of-two max size (Unity-supported value) that fits
    /// the largest dimension of the texture on disk, so no downscaling occurs.
    /// </summary>
    /// <param name="path">Project-relative asset path of the texture.</param>
    private static int GetMaxSizeForTexture(string path)
    {
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), path);

        if (!File.Exists(fullPath)) return 2048;

        var bytes = File.ReadAllBytes(fullPath);
        var tempTexture = new Texture2D(2, 2);
        
        tempTexture.LoadImage(bytes);

        var largestDimension = Mathf.Max(tempTexture.width, tempTexture.height);

        Object.DestroyImmediate(tempTexture);

        var candidateSizes = new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };

        foreach (var size in candidateSizes)
        {
            if (largestDimension <= size)
            {
                return size;
            }
        }

        return 8192;
    }
}
#endif



