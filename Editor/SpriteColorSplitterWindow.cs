#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// Editor window that splits one or several sprites (e.g. every frame of an animation)
/// into multiple sub-sprites by clustering their pixels by color similarity (a simple
/// k-means implementation running either in RGB space or in a hue-weighted HSV space).
/// Useful to split a weapon sprite into its main color parts (e.g. the brown wood, the
/// white string, the grey arrowhead) so each part can later be recolored independently.
/// </summary>
public class SpriteColorSplitterWindow : EditorWindow
{
    /// <summary>
    /// Distance metric used to compare pixel colors when clustering.
    /// </summary>
    private enum ColorDistanceMode
    {
        RgbEuclidean,
        HsvWeighted
    }

    /// <summary>
    /// Snapshot of the sprite/texture import settings read from a source sprite, so
    /// every generated part can be re-imported with the same pixels per unit, pivot,
    /// mesh type, filtering and compression as the original it was generated from.
    /// </summary>
    private struct SpriteImportSettingsSnapshot
    {
        public float PixelsPerUnit;
        public SpriteMeshType MeshType;
        public uint Extrude;
        public bool GenerateFallbackPhysicsShape;
        public int Alignment;
        public Vector2 Pivot;
        public Vector4 Border;
        public FilterMode FilterMode;
        public TextureWrapMode WrapMode;
        public bool MipmapEnabled;
        public TextureImporterCompression Compression;
        public int MaxTextureSize;
        public bool AlphaIsTransparency;
        public bool SRgbTexture;
    }

    /// <summary>
    /// Clustering result for a single source unit: one texture per cluster, keeping
    /// every pixel at its original position. A unit is either a single sprite (e.g. one
    /// animation frame) or, when the source texture uses the Multiple sprite import
    /// mode, the whole atlas texture shared by all of its sub-sprites.
    /// </summary>
    private class SourcePreview
    {
        public Sprite Sprite;
        public int SpriteIndex;
        public Texture2D[] ClusterTextures;
        public int[] ClusterPixelCounts;
        public bool IsSpriteSheet;
        public SpriteRect[] SpriteSheetRects;
        public Texture2D SourceTexture;
    }

    /// <summary>
    /// A single PNG about to be written to disk, alongside the import settings that
    /// should be applied to it once Unity has imported it.
    /// </summary>
    private class ExportEntry
    {
        public string AssetPath;
        public Texture2D Texture;
        public SpriteImportSettingsSnapshot Settings;
        public bool IsSpriteSheet;
        public SpriteRect[] SpriteSheetRects;
    }

    // -------------------------------------------------------------------------
    // Inspector-like fields
    // -------------------------------------------------------------------------

    private readonly List<Sprite> _sourceSprites = new ();
    private int _clusterCount = 3;
    private int _maxIterations = 50;
    private int _randomSeed;
    private float _alphaThreshold = 0.05f;
    private ColorDistanceMode _distanceMode = ColorDistanceMode.HsvWeighted;
    private float _hueWeight = 1f;
    private float _saturationWeight = 0.35f;
    private float _valueWeight = 0.35f;
    private string _outputBaseName = "";

    // -------------------------------------------------------------------------
    // Preview state
    // -------------------------------------------------------------------------

    private readonly List<SourcePreview> _previews = new ();
    private Vector2 _scrollPosition;

    [MenuItem("Tools/Colorless Core/Sprite Color Splitter")]
    private static void OpenWindow()
    {
        var window = GetWindow<SpriteColorSplitterWindow>("Sprite Color Splitter");

        window.minSize = new Vector2(460, 520);
    }

    private void OnDisable()
    {
        ClearPreviews();
    }

    // -------------------------------------------------------------------------
    // GUI
    // -------------------------------------------------------------------------

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        DrawSourceList();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Clustering Settings", EditorStyles.boldLabel);

        _clusterCount = EditorGUILayout.IntSlider("Sprite Number (clusters)", _clusterCount, 2, 12);
        _distanceMode = (ColorDistanceMode)EditorGUILayout.EnumPopup("Distance Mode", _distanceMode);

        if (_distanceMode == ColorDistanceMode.HsvWeighted)
        {
            _hueWeight = EditorGUILayout.Slider("Hue Weight", _hueWeight, 0f, 2f);
            _saturationWeight = EditorGUILayout.Slider("Saturation Weight", _saturationWeight, 0f, 2f);
            _valueWeight = EditorGUILayout.Slider("Value Weight", _valueWeight, 0f, 2f);
        }

        _alphaThreshold = EditorGUILayout.Slider("Alpha Threshold", _alphaThreshold, 0f, 1f);
        _maxIterations = EditorGUILayout.IntSlider("Max Iterations", _maxIterations, 1, 200);
        _randomSeed = EditorGUILayout.IntField("Random Seed", _randomSeed);

        EditorGUILayout.Space();

        var hasAnySprite = HasAnySourceSprite();

        using (new EditorGUI.DisabledScope(!hasAnySprite))
        {
            if (GUILayout.Button("Generate Preview"))
            {
                GeneratePreview();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Export", EditorStyles.boldLabel);

        _outputBaseName = EditorGUILayout.TextField("Output Base Name", _outputBaseName);

        EditorGUILayout.HelpBox(
            "Output files are named \"<base name>_<split number>_<input image index>.png\" " +
            "and saved next to each source sprite. If a source sprite belongs to a texture " +
            "using the Multiple sprite mode, the whole sheet is processed as one unit and " +
            "each generated layer keeps the exact same sub-sprite slicing as the original.",
            MessageType.None);

        using (new EditorGUI.DisabledScope(_previews.Count == 0))
        {
            if (GUILayout.Button("Save Generated Sprites as PNGs"))
            {
                SaveGeneratedSprites();
            }
        }

        EditorGUILayout.Space();
        DrawPreview();

        EditorGUILayout.EndScrollView();
    }

    private bool HasAnySourceSprite()
    {
	    return _sourceSprites.Any(sprite => sprite);
    }

    private void DrawSourceList()
    {
        EditorGUILayout.LabelField("Source Sprites (e.g. animation frames)", EditorStyles.boldLabel);

        DrawDropArea();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Selected Sprites"))
            {
                AddSpritesFromSelection();
            }

            if (GUILayout.Button("Add Empty Slot"))
            {
                _sourceSprites.Add(null);
            }

            if (GUILayout.Button("Clear All"))
            {
                _sourceSprites.Clear();
                ClearPreviews();
            }
        }

        var removeIndex = -1;

        for (var i = 0; i < _sourceSprites.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _sourceSprites[i] = (Sprite)EditorGUILayout.ObjectField(
                    $"Frame {i}",
                    _sourceSprites[i],
                    typeof(Sprite),
                    false);

                if (GUILayout.Button("X", GUILayout.Width(24)))
                {
                    removeIndex = i;
                }
            }
        }

        if (removeIndex >= 0)
        {
            _sourceSprites.RemoveAt(removeIndex);
        }
    }

    private void DrawDropArea()
    {
        var dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));

        GUI.Box(dropRect, "Drag & drop sprites or textures here");

        var currentEvent = Event.current;

        if (!dropRect.Contains(currentEvent.mousePosition)) return;

        if (currentEvent.type == EventType.DragUpdated)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            currentEvent.Use();
        }
        else if (currentEvent.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();

            foreach (var draggedObject in DragAndDrop.objectReferences)
            {
                AddSpriteFromObject(draggedObject);
            }

            currentEvent.Use();
        }
    }

    private void AddSpritesFromSelection()
    {
        foreach (var selectedObject in Selection.objects)
        {
            AddSpriteFromObject(selectedObject);
        }
    }

    private void AddSpriteFromObject(Object asset)
    {
        if (asset is Sprite sprite)
        {
            if (!_sourceSprites.Contains(sprite))
            {
                _sourceSprites.Add(sprite);
            }

            return;
        }

        if (!(asset is Texture2D texture))
        {
            return;
        }

        var path = AssetDatabase.GetAssetPath(texture);
        var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);

        foreach (var subAsset in subAssets)
        {
            if (subAsset is Sprite spriteAsset && !_sourceSprites.Contains(spriteAsset))
            {
                _sourceSprites.Add(spriteAsset);
            }
        }
    }

    private void DrawPreview()
    {
        if (_previews.Count == 0)
        {
            EditorGUILayout.HelpBox("Generate a preview to see the resulting sprites.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);


        const float previewSize = 100f;
        const int columns = 4;

        foreach (var preview in _previews)
        {
            var label = preview.IsSpriteSheet
                ? $"Frame {preview.SpriteIndex} — {preview.SourceTexture.name} " +
                  $"(sprite sheet, {preview.SpriteSheetRects.Length} sub-sprites)"
                : $"Frame {preview.SpriteIndex} — {preview.Sprite.name}";

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);

            for (var i = 0; i < preview.ClusterTextures.Length; i++)
            {
                if (i % columns == 0)
                {
                    EditorGUILayout.BeginHorizontal();
                }

                EditorGUILayout.BeginVertical(GUILayout.Width(previewSize + 10));

                var rect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.ExpandWidth(false));

                EditorGUI.DrawTextureTransparent(rect, preview.ClusterTextures[i], ScaleMode.ScaleToFit);

                var pixelCount = preview.ClusterPixelCounts != null && i < preview.ClusterPixelCounts.Length
                    ? preview.ClusterPixelCounts[i]
                    : 0;

                EditorGUILayout.LabelField($"Split {i + 1} ({pixelCount} px)", EditorStyles.centeredGreyMiniLabel);

                EditorGUILayout.EndVertical();

                if (i % columns == columns - 1 || i == preview.ClusterTextures.Length - 1)
                {
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space();
        }
    }

    // -------------------------------------------------------------------------
    // Preview generation (k-means clustering)
    // -------------------------------------------------------------------------

    private void GeneratePreview()
    {
        ClearPreviews();

        var validSprites = new List<(Sprite Sprite, int Index)>();

        for (var i = 0; i < _sourceSprites.Count; i++)
        {
            if (_sourceSprites[i] != null)
            {
                validSprites.Add((_sourceSprites[i], i));
            }
        }

        if (validSprites.Count == 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(_outputBaseName))
        {
            _outputBaseName = validSprites[0].Sprite.name;
        }

        var processedSpriteSheetTextures = new HashSet<Texture2D>();

        foreach (var entry in validSprites)
        {
            var importer = GetTextureImporter(entry.Sprite);
            var isSpriteSheet = importer != null && importer.spriteImportMode == SpriteImportMode.Multiple;

            SourcePreview preview;

            if (isSpriteSheet)
            {
                // All sub-sprites of the same sheet share the same atlas texture; process
                // it only once even if several of its sub-sprites are in the source list.
                if (!processedSpriteSheetTextures.Add(entry.Sprite.texture))
                {
                    continue;
                }

                preview = GenerateSpriteSheetPreview(entry.Sprite, entry.Index, importer);
            }
            else
            {
                preview = GenerateSourcePreview(entry.Sprite, entry.Index);
            }

            if (preview != null)
            {
                _previews.Add(preview);
            }
        }
    }

    /// <summary>
    /// Returns the texture importer backing the given sprite's texture, or null if it
    /// cannot be resolved (e.g. the sprite is not backed by an on-disk texture asset).
    /// </summary>
    private static TextureImporter GetTextureImporter(Sprite sprite)
    {
        var path = AssetDatabase.GetAssetPath(sprite);

        return AssetImporter.GetAtPath(path) as TextureImporter;
    }

    /// <summary>
    /// Runs the whole clustering pipeline (pixel reading, k-means, texture building)
    /// for a single source sprite (Single sprite import mode).
    /// </summary>
    private SourcePreview GenerateSourcePreview(Sprite sprite, int spriteIndex)
    {
        if (!TryReadSpritePixels(sprite, out var pixels, out var width, out var height))
        {
            Debug.LogError($"[SpriteColorSplitter] Unable to read pixels from \"{sprite.name}\".");

            return null;
        }

        return BuildPreview(sprite, spriteIndex, pixels, width, height, false, null);
    }

    /// <summary>
    /// Runs the whole clustering pipeline over an entire atlas texture (Multiple sprite
    /// import mode), so every cluster texture covers the full sheet and can later be
    /// re-sliced with the exact same sub-sprite rects as the source.
    /// </summary>
    private SourcePreview GenerateSpriteSheetPreview(Sprite representativeSprite, int spriteIndex, TextureImporter importer)
    {
        var texture = representativeSprite.texture;

        if (!TryReadWholeTexturePixels(texture, out var pixels, out var width, out var height))
        {
            Debug.LogError($"[SpriteColorSplitter] Unable to read pixels from \"{texture.name}\".");

            return null;
        }

        var spriteRects = GetSourceSpriteRects(importer);
        var preview = BuildPreview(representativeSprite, spriteIndex, pixels, width, height, true, spriteRects);

        if (preview != null)
        {
            preview.SourceTexture = texture;
        }

        return preview;
    }

    /// <summary>
    /// Reads the sub-sprite rects (position, name, pivot, alignment, border) currently
    /// defined on a Multiple-mode texture importer, via the Sprite Editor data provider.
    /// </summary>
    private static SpriteRect[] GetSourceSpriteRects(TextureImporter importer)
    {
        var factories = new SpriteDataProviderFactories();

        factories.Init();

        var dataProvider = factories.GetSpriteEditorDataProviderFromObject(importer);

        dataProvider.InitSpriteEditorDataProvider();

        return dataProvider.GetSpriteRects();
    }

    /// <summary>
    /// Shared clustering pipeline (opaque pixel selection, k-means, cluster texture
    /// building) used for both single-sprite and whole-sheet processing.
    /// </summary>
    private SourcePreview BuildPreview(
        Sprite sprite,
        int spriteIndex,
        Color[] pixels,
        int width,
        int height,
        bool isSpriteSheet,
        SpriteRect[] spriteSheetRects)
    {
        var assignments = new int[pixels.Length];
        var opaqueIndices = new List<int>();

        for (var i = 0; i < pixels.Length; i++)
        {
            assignments[i] = -1;

            if (pixels[i].a >= _alphaThreshold)
            {
                opaqueIndices.Add(i);
            }
        }

        if (opaqueIndices.Count == 0)
        {
            Debug.LogWarning($"[SpriteColorSplitter] \"{sprite.name}\" has no opaque pixel above the alpha threshold.");

            return null;
        }

        var clusterCount = Mathf.Min(_clusterCount, opaqueIndices.Count);
        var random = new System.Random(_randomSeed);
        var centers = InitializeCenters(pixels, opaqueIndices, clusterCount, random);

        RunKMeans(pixels, opaqueIndices, centers, assignments, out var clusterPixelCounts);

        var clusterTextures = BuildClusterTextures(pixels, assignments, clusterCount, width, height);

        return new SourcePreview
        {
            Sprite = sprite,
            SpriteIndex = spriteIndex,
            ClusterTextures = clusterTextures,
            ClusterPixelCounts = clusterPixelCounts,
            IsSpriteSheet = isSpriteSheet,
            SpriteSheetRects = spriteSheetRects
        };
    }

    /// <summary>
    /// Picks initial cluster centers using a k-means++ style weighted selection,
    /// so the initial centers already tend to be spread across distinct colors.
    /// </summary>
    private Vector4[] InitializeCenters(Color[] pixels, List<int> opaqueIndices, int clusterCount, System.Random random)
    {
        var centers = new Vector4[clusterCount];
        var firstIndex = opaqueIndices[random.Next(opaqueIndices.Count)];

        centers[0] = ToFeatureVector(pixels[firstIndex]);

        for (var c = 1; c < clusterCount; c++)
        {
            var distances = new double[opaqueIndices.Count];
            var totalWeight = 0.0;

            for (var i = 0; i < opaqueIndices.Count; i++)
            {
                var feature = ToFeatureVector(pixels[opaqueIndices[i]]);
                var minDistance = double.MaxValue;

                for (var c2 = 0; c2 < c; c2++)
                {
                    var distance = (feature - centers[c2]).sqrMagnitude;

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                    }
                }

                distances[i] = minDistance;
                totalWeight += minDistance;
            }

            if (totalWeight <= 0.0)
            {
                var fallbackIndex = opaqueIndices[random.Next(opaqueIndices.Count)];

                centers[c] = ToFeatureVector(pixels[fallbackIndex]);

                continue;
            }

            var target = random.NextDouble() * totalWeight;
            var cumulative = 0.0;
            var chosenIndex = opaqueIndices[^1];

            for (var i = 0; i < opaqueIndices.Count; i++)
            {
                cumulative += distances[i];

                if (cumulative >= target)
                {
                    chosenIndex = opaqueIndices[i];

                    break;
                }
            }

            centers[c] = ToFeatureVector(pixels[chosenIndex]);
        }

        return centers;
    }

    /// <summary>
    /// Runs the k-means loop: assign every opaque pixel to its nearest cluster center,
    /// then recompute each center as the average feature of its assigned pixels, until
    /// no assignment changes or the iteration budget is exhausted.
    /// </summary>
    private void RunKMeans(
        Color[] pixels,
        List<int> opaqueIndices,
        Vector4[] centers,
        int[] assignments,
        out int[] clusterPixelCounts)
    {
        var clusterCount = centers.Length;

        for (var iteration = 0; iteration < _maxIterations; iteration++)
        {
            var changed = false;

            foreach (var pixelIndex in opaqueIndices)
            {
                var feature = ToFeatureVector(pixels[pixelIndex]);

                var bestCluster = 0;
                var bestDistance = float.MaxValue;

                for (var c = 0; c < clusterCount; c++)
                {
                    var distance = (feature - centers[c]).sqrMagnitude;

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestCluster = c;
                    }
                }

                if (assignments[pixelIndex] != bestCluster)
                {
                    assignments[pixelIndex] = bestCluster;
                    changed = true;
                }
            }

            var sums = new Vector4[clusterCount];
            var counts = new int[clusterCount];

            foreach (var pixelIndex in opaqueIndices)
            {
                var cluster = assignments[pixelIndex];

                sums[cluster] += ToFeatureVector(pixels[pixelIndex]);
                counts[cluster]++;
            }

            for (var c = 0; c < clusterCount; c++)
            {
                if (counts[c] > 0)
                {
                    centers[c] = sums[c] / counts[c];
                }
            }

            if (!changed)
            {
                break;
            }
        }

        clusterPixelCounts = new int[clusterCount];

        foreach (var pixelIndex in opaqueIndices)
        {
            clusterPixelCounts[assignments[pixelIndex]]++;
        }
    }

    /// <summary>
    /// Converts a pixel color into the feature space used for distance comparisons.
    /// In HSV mode, hue is embedded as a point on a unit circle (cos/sin) so its
    /// circular nature is naturally handled by a plain Euclidean distance.
    /// </summary>
    private Vector4 ToFeatureVector(Color color)
    {
        if (_distanceMode == ColorDistanceMode.RgbEuclidean)
        {
            return new Vector4(color.r, color.g, color.b, 0f);
        }

        Color.RGBToHSV(color, out var hue, out var saturation, out var value);

        var angle = hue * Mathf.PI * 2f;

        return new Vector4(
            Mathf.Cos(angle) * _hueWeight,
            Mathf.Sin(angle) * _hueWeight,
            saturation * _saturationWeight,
            value * _valueWeight);
    }

    /// <summary>
    /// Builds one RGBA texture per cluster, keeping every pixel at its original
    /// position: a pixel is opaque in exactly one output texture (its cluster) and
    /// fully transparent in every other one.
    /// </summary>
    private static Texture2D[] BuildClusterTextures(
        Color[] pixels,
        int[] assignments,
        int clusterCount,
        int width,
        int height)
    {
        var textures = new Texture2D[clusterCount];

        for (var c = 0; c < clusterCount; c++)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var clusterPixels = new Color[pixels.Length];

            for (var i = 0; i < pixels.Length; i++)
            {
                clusterPixels[i] = assignments[i] == c ? pixels[i] : Color.clear;
            }

            texture.SetPixels(clusterPixels);
            texture.Apply();

            textures[c] = texture;
        }

        return textures;
    }

    private void ClearPreviews()
    {
        foreach (var preview in _previews)
        {
            if (preview.ClusterTextures == null)
            {
                continue;
            }

            foreach (var texture in preview.ClusterTextures)
            {
                if (texture)
                {
                    DestroyImmediate(texture);
                }
            }
        }

        _previews.Clear();
    }

    // -------------------------------------------------------------------------
    // Sprite pixel reading
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads the pixel colors of a sprite, taking its texture rect into account so
    /// only the sprite's own region is returned, regardless of whether the source
    /// texture is marked readable in its import settings.
    /// </summary>
    private static bool TryReadSpritePixels(Sprite sprite, out Color[] pixels, out int width, out int height)
    {
        return TryReadPixelsInRect(sprite.texture, sprite.textureRect, out pixels, out width, out height);
    }

    /// <summary>
    /// Reads every pixel of a whole texture (used when the source uses the Multiple
    /// sprite import mode, so the entire atlas is clustered and rebuilt as one unit).
    /// </summary>
    private static bool TryReadWholeTexturePixels(Texture2D texture, out Color[] pixels, out int width, out int height)
    {
        if (texture == null)
        {
            pixels = null;
            width = 0;
            height = 0;

            return false;
        }

        return TryReadPixelsInRect(texture, new Rect(0, 0, texture.width, texture.height), out pixels, out width, out height);
    }

    /// <summary>
    /// Reads the pixel colors contained in the given rect of a texture, regardless of
    /// whether the texture is marked readable in its import settings.
    /// </summary>
    private static bool TryReadPixelsInRect(Texture2D sourceTexture, Rect rect, out Color[] pixels, out int width, out int height)
    {
        pixels = null;
        width = 0;
        height = 0;

        if (!sourceTexture) return false;

        var readableTexture = MakeTextureReadable(sourceTexture);

        width = Mathf.RoundToInt(rect.width);
        height = Mathf.RoundToInt(rect.height);

        try
        {
            pixels = readableTexture.GetPixels(
                Mathf.RoundToInt(rect.x),
                Mathf.RoundToInt(rect.y),
                width,
                height);
        }
        finally
        {
            if (readableTexture != sourceTexture)
            {
                Object.DestroyImmediate(readableTexture);
            }
        }

        return pixels != null;
    }

    /// <summary>
    /// Returns a readable copy of the given texture via a temporary render target,
    /// so pixels can be extracted even when "Read/Write" is disabled in the importer.
    /// </summary>
    private static Texture2D MakeTextureReadable(Texture2D source)
    {
        if (source.isReadable)
        {
            return source;
        }

        var renderTexture = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear);

        Graphics.Blit(source, renderTexture);

        var previousActive = RenderTexture.active;

        RenderTexture.active = renderTexture;

        var readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);

        readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        readable.Apply();

        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);

        return readable;
    }

    // -------------------------------------------------------------------------
    // Export
    // -------------------------------------------------------------------------

    /// <summary>
    /// Writes every generated cluster texture to disk, right next to the source
    /// sprite it was generated from, named
    /// "&lt;base name&gt;_&lt;split number&gt;_&lt;input image index&gt;.png".
    /// </summary>
    private void SaveGeneratedSprites()
    {
        if (_previews.Count == 0)
        {
            return;
        }

        var baseName = string.IsNullOrEmpty(_outputBaseName) ? "Sprite" : _outputBaseName;
        var entries = new List<ExportEntry>();

        foreach (var preview in _previews)
        {
            var sourcePath = AssetDatabase.GetAssetPath(preview.Sprite);
            var directory = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/");

            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            var hasSourceSettings = TryGetSourceImportSettings(preview.Sprite, out var sourceSettings);
            var settings = hasSourceSettings ? sourceSettings : GetDefaultImportSettings();

            for (var i = 0; i < preview.ClusterTextures.Length; i++)
            {
                var assetPath = $"{directory}/{baseName}_{i + 1}_{preview.SpriteIndex}.png";

                entries.Add(new ExportEntry
                {
                    AssetPath = assetPath,
                    Texture = preview.ClusterTextures[i],
                    Settings = settings,
                    IsSpriteSheet = preview.IsSpriteSheet,
                    SpriteSheetRects = preview.SpriteSheetRects
                });
            }
        }

        foreach (var entry in entries)
        {
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), entry.AssetPath);
            var bytes = entry.Texture.EncodeToPNG();

            File.WriteAllBytes(fullPath, bytes);
        }

        AssetDatabase.Refresh();

        foreach (var entry in entries)
        {
            if (entry.IsSpriteSheet)
            {
                ApplySpriteSheetImportPreset(entry.AssetPath, entry.Settings, entry.SpriteSheetRects);
            }
            else
            {
                ApplySpriteImportPreset(entry.AssetPath, entry.Settings);
            }
        }
    }

    /// <summary>
    /// Reads the sprite/texture import settings from a source sprite's importer,
    /// so they can later be re-applied to every generated sprite part.
    /// </summary>
    private static bool TryGetSourceImportSettings(Sprite sprite, out SpriteImportSettingsSnapshot snapshot)
    {
        snapshot = default;

        var sourcePath = AssetDatabase.GetAssetPath(sprite);
        var importer = AssetImporter.GetAtPath(sourcePath) as TextureImporter;

        if (!importer)
        {
            return false;
        }

        var settings = new TextureImporterSettings();

        importer.ReadTextureSettings(settings);

        var platformSettings = importer.GetDefaultPlatformTextureSettings();

        snapshot = new SpriteImportSettingsSnapshot
        {
            PixelsPerUnit = settings.spritePixelsPerUnit,
            MeshType = settings.spriteMeshType,
            Extrude = settings.spriteExtrude,
            GenerateFallbackPhysicsShape = settings.spriteGenerateFallbackPhysicsShape,
            Alignment = settings.spriteAlignment,
            Pivot = settings.spritePivot,
            Border = importer.spriteBorder,
            FilterMode = importer.filterMode,
            WrapMode = importer.wrapMode,
            MipmapEnabled = importer.mipmapEnabled,
            Compression = importer.textureCompression,
            MaxTextureSize = platformSettings.maxTextureSize,
            AlphaIsTransparency = importer.alphaIsTransparency,
            SRgbTexture = importer.sRGBTexture
        };

        return true;
    }

    /// <summary>
    /// Fallback settings used when a source sprite's import settings could not be read.
    /// </summary>
    private static SpriteImportSettingsSnapshot GetDefaultImportSettings()
    {
        return new SpriteImportSettingsSnapshot
        {
            PixelsPerUnit = 100f,
            MeshType = SpriteMeshType.FullRect,
            Extrude = 0,
            GenerateFallbackPhysicsShape = false,
            Alignment = (int)SpriteAlignment.Center,
            Pivot = new Vector2(0.5f, 0.5f),
            Border = Vector4.zero,
            FilterMode = FilterMode.Point,
            WrapMode = TextureWrapMode.Clamp,
            MipmapEnabled = false,
            Compression = TextureImporterCompression.Uncompressed,
            MaxTextureSize = 2048,
            AlphaIsTransparency = true,
            SRgbTexture = true
        };
    }

    /// <summary>
    /// Applies the sprite import type plus the given settings snapshot (pixels per
    /// unit, pivot, mesh type, filtering, compression, ...) to the texture at the
    /// given path, so it matches the source sprite it was generated from.
    /// </summary>
    private static void ApplySpriteImportPreset(string path, SpriteImportSettingsSnapshot snapshot)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (!importer)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.alphaIsTransparency = snapshot.AlphaIsTransparency;
        importer.mipmapEnabled = snapshot.MipmapEnabled;
        importer.filterMode = snapshot.FilterMode;
        importer.wrapMode = snapshot.WrapMode;
        importer.textureCompression = snapshot.Compression;
        importer.sRGBTexture = snapshot.SRgbTexture;
        importer.spriteBorder = snapshot.Border;

        var settings = new TextureImporterSettings();

        importer.ReadTextureSettings(settings);
        settings.spriteMode = (int)SpriteImportMode.Single;
        settings.spritePixelsPerUnit = snapshot.PixelsPerUnit;
        settings.spriteMeshType = snapshot.MeshType;
        settings.spriteExtrude = snapshot.Extrude;
        settings.spriteGenerateFallbackPhysicsShape = snapshot.GenerateFallbackPhysicsShape;
        settings.spriteAlignment = snapshot.Alignment;
        settings.spritePivot = snapshot.Pivot;
        importer.SetTextureSettings(settings);

        var platformSettings = importer.GetDefaultPlatformTextureSettings();

        platformSettings.maxTextureSize = snapshot.MaxTextureSize;
        platformSettings.textureCompression = snapshot.Compression;
        importer.SetPlatformTextureSettings(platformSettings);

        importer.SaveAndReimport();
    }

    /// <summary>
    /// Applies the sprite import type plus the given settings snapshot to a generated
    /// layer texture, then re-slices it with the exact same sub-sprite rects, names,
    /// pivots and borders as the source atlas it was generated from, so the layer keeps
    /// the same "separation" as the original sprite sheet.
    /// </summary>
    private static void ApplySpriteSheetImportPreset(
        string path,
        SpriteImportSettingsSnapshot snapshot,
        SpriteRect[] spriteSheetRects)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (!importer)
        {
            return;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.alphaIsTransparency = snapshot.AlphaIsTransparency;
        importer.mipmapEnabled = snapshot.MipmapEnabled;
        importer.filterMode = snapshot.FilterMode;
        importer.wrapMode = snapshot.WrapMode;
        importer.textureCompression = snapshot.Compression;
        importer.sRGBTexture = snapshot.SRgbTexture;

        var settings = new TextureImporterSettings();

        importer.ReadTextureSettings(settings);
        settings.spriteMode = (int)SpriteImportMode.Multiple;
        settings.spritePixelsPerUnit = snapshot.PixelsPerUnit;
        settings.spriteMeshType = snapshot.MeshType;
        settings.spriteExtrude = snapshot.Extrude;
        settings.spriteGenerateFallbackPhysicsShape = snapshot.GenerateFallbackPhysicsShape;
        importer.SetTextureSettings(settings);

        var platformSettings = importer.GetDefaultPlatformTextureSettings();

        platformSettings.maxTextureSize = snapshot.MaxTextureSize;
        platformSettings.textureCompression = snapshot.Compression;
        importer.SetPlatformTextureSettings(platformSettings);

        // Commit the Multiple import mode and the settings above first, so the sprite
        // editor data provider below reads/writes against a consistent importer state.
        importer.SaveAndReimport();

        ApplySpriteRects(importer, spriteSheetRects);

        importer.SaveAndReimport();
    }

    /// <summary>
    /// Writes a copy of the given sub-sprite rects (position, name, pivot, alignment,
    /// border) to a Multiple-mode texture importer, via the Sprite Editor data provider,
    /// so the resulting sprites keep the same identity (name/GUID) as the source ones.
    /// </summary>
    private static void ApplySpriteRects(TextureImporter importer, SpriteRect[] sourceSpriteRects)
    {
        var clonedRects = new SpriteRect[sourceSpriteRects.Length];

        for (var i = 0; i < sourceSpriteRects.Length; i++)
        {
            var source = sourceSpriteRects[i];

            clonedRects[i] = new SpriteRect
            {
                name = source.name,
                rect = source.rect,
                alignment = source.alignment,
                pivot = source.pivot,
                border = source.border,
                spriteID = source.spriteID
            };
        }

        var factories = new SpriteDataProviderFactories();

        factories.Init();

        var dataProvider = factories.GetSpriteEditorDataProviderFromObject(importer);

        dataProvider.InitSpriteEditorDataProvider();
        dataProvider.SetSpriteRects(clonedRects);

        var nameFileIdDataProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();

        if (nameFileIdDataProvider != null)
        {
            var nameFileIdPairs = new SpriteNameFileIdPair[clonedRects.Length];

            for (var i = 0; i < clonedRects.Length; i++)
            {
                nameFileIdPairs[i] = new SpriteNameFileIdPair(clonedRects[i].name, clonedRects[i].spriteID);
            }

            nameFileIdDataProvider.SetNameFileIdPairs(nameFileIdPairs);
        }

        dataProvider.Apply();
    }
}
#endif

