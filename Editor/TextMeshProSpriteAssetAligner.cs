#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to automatically set BX and BY bearing values on all glyphs
/// of a <see cref="TMP_SpriteAsset"/> based on their individual glyph size.
/// </summary>
public static class TextMeshProSpriteAssetAligner
{
    // -------------------------------------------------------------------------
    // Menu items — context menu on TMP_SpriteAsset
    // -------------------------------------------------------------------------

    [MenuItem("Assets/Auto-Align TMP Sprite Asset", true)]
    private static bool ValidateSelectedSpriteAsset() => Selection.activeObject is TMP_SpriteAsset;

    [MenuItem("Assets/Auto-Align TMP Sprite Asset")]
    private static void AlignCenter() => AlignSelectedSpriteAsset();

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies bearing alignment to the currently selected <see cref="TMP_SpriteAsset"/>.
    /// </summary>
    private static void AlignSelectedSpriteAsset()
    {
        var spriteAsset = Selection.activeObject as TMP_SpriteAsset;

        if (!spriteAsset)
        {
            Debug.LogWarning("[TMP_SpriteAssetAligner] No TMP_SpriteAsset selected.");
            return;
        }

        AlignSpriteAsset(spriteAsset);
    }

    /// <summary>
    /// Iterates over every glyph in the given <see cref="TMP_SpriteAsset"/> and
    /// recalculates its <c>bearingX</c> and <c>bearingY</c> values based on the
    /// glyph dimensions.
    /// </summary>
    /// <param name="spriteAsset">Target sprite asset to modify.</param>
    private static void AlignSpriteAsset(TMP_SpriteAsset spriteAsset)
    {
        if (!spriteAsset) throw new System.ArgumentNullException(nameof(spriteAsset));

        var glyphs = spriteAsset.spriteGlyphTable;

        if (glyphs == null || glyphs.Count == 0)
        {
            Debug.LogWarning($"[TMP_SpriteAssetAligner] '{spriteAsset.name}' contains no glyphs.");
            return;
        }

        Undo.RecordObject(spriteAsset, "Auto-Align TMP Sprite Bearings");

        var count = 0;

        foreach (var glyph in glyphs)
        {
            var metrics = glyph.metrics;
            var w = metrics.width;
            var h = metrics.height;

            // BX = 5% of the glyph width, giving proportional horizontal padding.
            // This yields ~0.4 for an 8 px sprite, ~0.8 for 16 px, and so on.
            var bx = w * 0.05f;
            metrics.horizontalBearingX = bx;

            // Custom BY for the project
            metrics.horizontalBearingY = h * 0.87f;

            // Advance = width + left bearing + right bearing (symmetric padding) so glyphs don't overlap.
            metrics.horizontalAdvance = w + 2f * bx;

            glyph.metrics = metrics;
            count++;
        }

        EditorUtility.SetDirty(spriteAsset);
        AssetDatabase.SaveAssetIfDirty(spriteAsset);

        // Notify TMP that the sprite asset properties have changed so open scenes refresh immediately.
        TMPro_EventManager.ON_SPRITE_ASSET_PROPERTY_CHANGED(true, spriteAsset);
    }
}
#endif


