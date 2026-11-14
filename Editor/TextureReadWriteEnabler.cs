#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class TextureReadWriteEnabler
{
	[MenuItem("Tools/Enable Read-Write on All Textures")]
	private static void EnableReadWriteOnAllTextures()
	{
		var guids = AssetDatabase.FindAssets("t:Texture2D");
		var count = 0;

		try
		{
			for (var i = 0; i < guids.Length; i++)
			{
				var path = AssetDatabase.GUIDToAssetPath(guids[i]);

				EditorUtility.DisplayProgressBar(
					"Enabling Read/Write...",
					path,
					(float)i / guids.Length
				);

				var importer = AssetImporter.GetAtPath(path) as TextureImporter;
				if (importer == null || importer.isReadable) continue;

				importer.isReadable = true;
				importer.SaveAndReimport();
				count++;
			}
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}

		Debug.Log($"[TextureReadWriteEnabler] {count} texture(s) mise(s) à jour sur {guids.Length} trouvée(s).");
	}
}
#endif