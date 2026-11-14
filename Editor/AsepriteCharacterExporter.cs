#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Editor utility that exports a character from an Aseprite file into two sprite sheets
/// using the Aseprite command line:
/// - "-placeholder": only the "Placeholder" layer
/// - "-handle": only the "Handle" layer
/// - "": all layers except those two.
/// All PNGs get the TextureImportPresetApplier settings, in Multiple mode sliced 64x64.
/// </summary>
public static class AsepriteCharacterExporter
{
    private const string AsepritePathKey = "AsepriteCharacterExporter.AsepritePath";
    private const string DefaultAsepritePath = @"C:\Program Files (x86)\Steam\steamapps\common\Aseprite\Aseprite.exe";
    private const int CellSize = 64;
    private const string PlaceholderLayer = "Placeholder";
    private const string HandleLayer = "Handle";

    // -------------------------------------------------------------------------
    // Menu items
    // -------------------------------------------------------------------------

    [MenuItem("Assets/Aseprite/Set Aseprite Executable Path")]
    private static void SetAsepritePath()
    {
        var current = EditorPrefs.GetString(AsepritePathKey, DefaultAsepritePath);
        var selected = EditorUtility.OpenFilePanel("Select Aseprite executable", Path.GetDirectoryName(current), "exe");

        if (!string.IsNullOrEmpty(selected))
        {
            EditorPrefs.SetString(AsepritePathKey, selected);
        }
    }

    [MenuItem("Assets/Aseprite/Export Character Sprite Sheets", true)]
    private static bool ValidateExport()
    {
        foreach (var guid in Selection.assetGUIDs)
        {
            if (IsAsepriteFile(AssetDatabase.GUIDToAssetPath(guid)))
            {
                return true;
            }
        }

        return false;
    }

    [MenuItem("Assets/Aseprite/Export Character Sprite Sheets")]
    private static void ExportSelection()
    {
        var guids = Selection.assetGUIDs;

        try
        {
            for (var i = 0; i < guids.Length; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (!IsAsepriteFile(assetPath))
                {
                    continue;
                }

                EditorUtility.DisplayProgressBar("Exporting Aseprite character...", assetPath, (float)i / guids.Length);

                ExportCharacter(assetPath);
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

    private static bool IsAsepriteFile(string path)
    {
        return path.EndsWith(".aseprite", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".ase", StringComparison.OrdinalIgnoreCase);
    }

    private static void ExportCharacter(string assetPath)
    {
        var asepriteExe = EditorPrefs.GetString(AsepritePathKey, DefaultAsepritePath);

        if (!File.Exists(asepriteExe))
        {
            Debug.LogError("Aseprite executable not found. Use 'Assets/Aseprite/Set Aseprite Executable Path'.");

            return;
        }

        var fullSource = Path.GetFullPath(assetPath);
        var directory = Path.GetDirectoryName(assetPath);
        var baseName = Path.GetFileNameWithoutExtension(assetPath);
        var layers = ListLayers(asepriteExe, fullSource);

        if (layers.Count == 0)
        {
            Debug.LogError($"No layers found in {assetPath}.");

            return;
        }

        // Expected name format: "<character>-<action>-<variant><digits>", e.g. "fireman-attack-light3"
        // gives the sub-folder "light" and the file name "attack3". Other names fall back to the source name.
        var outputDirectory = directory;
        var outputName = baseName;
        var parts = baseName.Split('-');

        if (parts.Length >= 3)
        {
            var variant = Regex.Replace(parts[2], @"\d", string.Empty);
            var digits = Regex.Replace(parts[2], @"\D", string.Empty);

            outputDirectory = $"{directory}/{variant}";
            outputName = parts[1] + digits;

            Directory.CreateDirectory(outputDirectory);
        }

        var placeholderPath = $"{outputDirectory}/{outputName}-placeholder.png".Replace('\\', '/');
        var handlePath = $"{outputDirectory}/{outputName}-handle.png".Replace('\\', '/');
        var layersPath = $"{outputDirectory}/{outputName}.png".Replace('\\', '/');
        var exportedPaths = new List<string>();

        // Export 1: only the "Placeholder" layer.
        if (layers.Contains(PlaceholderLayer))
        {
            var placeholderArgs = $"-b --layer \"{PlaceholderLayer}\" \"{fullSource}\" --sheet \"{Path.GetFullPath(placeholderPath)}\" --sheet-type horizontal";

            RunAseprite(asepriteExe, placeholderArgs);
            exportedPaths.Add(placeholderPath);
        }
        else
        {
            Debug.LogWarning($"Layer '{PlaceholderLayer}' not found in {assetPath}.");
        }

        // Export 2: only the "Handle" layer.
        if (layers.Contains(HandleLayer))
        {
            var handleArgs = $"-b --layer \"{HandleLayer}\" \"{fullSource}\" --sheet \"{Path.GetFullPath(handlePath)}\" --sheet-type horizontal";

            RunAseprite(asepriteExe, handleArgs);
            exportedPaths.Add(handlePath);
        }
        else
        {
            Debug.LogWarning($"Layer '{HandleLayer}' not found in {assetPath}.");
        }

        // Export 3: every layer except "Placeholder" and "Handle".
        var layersArgs = $"-b --ignore-layer \"{PlaceholderLayer}\" --ignore-layer \"{HandleLayer}\" \"{fullSource}\" --sheet \"{Path.GetFullPath(layersPath)}\" --sheet-type horizontal";

        RunAseprite(asepriteExe, layersArgs);
        exportedPaths.Add(layersPath);

        foreach (var path in exportedPaths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        foreach (var path in exportedPaths)
        {
            ApplyMultipleSlicedPreset(path);
        }
    }

    private static List<string> ListLayers(string exe, string source)
    {
        var output = RunAseprite(exe, $"-b --list-layers \"{source}\"");
        var result = new List<string>();

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            result.Add(line.Trim());
        }

        return result;
    }

    private static string RunAseprite(string exe, string arguments)
    {
        var startInfo = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);

        if (process == null)
        {
            return string.Empty;
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Debug.LogError($"Aseprite failed ({arguments}): {error}");
        }

        return output;
    }

    /// <summary>
    /// Applies the shared preset in Multiple mode, then slices the texture in 64x64 cells.
    /// </summary>
    private static void ApplyMultipleSlicedPreset(string path)
    {
        TextureImportPresetApplier.ApplyPreset(path, SpriteImportMode.Multiple);

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        if (!texture)
        {
            return;
        }

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (!importer)
        {
            return;
        }

        var factory = new SpriteDataProviderFactories();

        factory.Init();

        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);

        provider.InitSpriteEditorDataProvider();

        var name = Path.GetFileNameWithoutExtension(path);
        var columns = texture.width / CellSize;
        var rows = texture.height / CellSize;
        var rects = new List<SpriteRect>();
        var index = 0;

        // Slice from the top-left corner, row by row.
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                rects.Add(new SpriteRect
                {
                    name = $"{name}_{index++}",
                    spriteID = GUID.Generate(),
                    rect = new Rect(column * CellSize, texture.height - (row + 1) * CellSize, CellSize, CellSize),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f)
                });
            }
        }

        provider.SetSpriteRects(rects.ToArray());
        provider.Apply();

        importer.SaveAndReimport();
    }
}
#endif

