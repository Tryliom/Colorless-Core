using System.IO;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ColorlessOdyssey
{
	/// <summary>Copies the game stubs shipped in the package (ModStubs~) into the mod project, replacing any previous copy.</summary>
	public static class ModStubsSync
	{
		private const string PackageName = "com.tryliom.colorless-core";
		private const string StubsFolder = "ModStubs~";
		private const string TargetFolder = "Assets/ModSDK";
		private const string GameMarkerFile = "Assets/Scripts/Core/Projectile.cs";
		private const string SettingsFile = "ProjectSettings.json";

		private const string PendingKey = "ModStubsSync.Pending";

		[MenuItem("Tools/Colorless Core/Sync Mod Stubs")]
		public static void Sync()
		{
			var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
			var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
			var url = File.Exists(manifestPath) ? ReadGitUrl(File.ReadAllText(manifestPath)) : null;

			if (url == null)
			{
				SyncNow();

				return;
			}

			// Re-adding the git URL without a hash resolves the latest commit; a domain reload may follow.
			SessionState.SetBool(PendingKey, true);
			Debug.Log("[ModStubsSync] Updating the package before syncing...");

			var request = UnityEditor.PackageManager.Client.Add(url);

			void Poll()
			{
				if (!request.IsCompleted)
				{
					return;
				}

				EditorApplication.update -= Poll;

				if (request.Status == UnityEditor.PackageManager.StatusCode.Failure)
				{
					Debug.LogWarning($"[ModStubsSync] Package update failed ({request.Error.message}), syncing with the current version.");
				}

				RunPending();
			}

			EditorApplication.update += Poll;
		}

		[InitializeOnLoadMethod]
		private static void OnLoad()
		{
			if (SessionState.GetBool(PendingKey, false))
			{
				EditorApplication.delayCall += RunPending;
			}
		}

		private static void RunPending()
		{
			if (!SessionState.GetBool(PendingKey, false))
			{
				return;
			}

			SessionState.SetBool(PendingKey, false);
			SyncNow();
		}

		private static string ReadGitUrl(string manifest)
		{
			var match = System.Text.RegularExpressions.Regex.Match(manifest, "\"" + PackageName + "\"\\s*:\\s*\"([^\"]+)\"");

			if (!match.Success || !match.Groups[1].Value.Contains(".git"))
			{
				return null;
			}

			var value = match.Groups[1].Value;
			var hash = value.IndexOf('#');

			return hash >= 0 && value.Length - hash - 1 == 40 ? value[..hash] : value;
		}

		private static void SyncNow()
		{
			var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;

			if (File.Exists(Path.Combine(projectRoot, GameMarkerFile)))
			{
				Debug.LogError("[ModStubsSync] This is the game project (real scripts present). Stubs must only be synced into a mod project.");

				return;
			}

			var package = PackageInfo.FindForAssetPath($"Packages/{PackageName}");

			if (package == null)
			{
				Debug.LogError($"[ModStubsSync] Package {PackageName} not found.");

				return;
			}

			var source = Path.Combine(package.resolvedPath, StubsFolder);

			if (!Directory.Exists(source))
			{
				Debug.LogError($"[ModStubsSync] {StubsFolder} not found in the package. Generate the stubs in the game project and update the package.");

				return;
			}

			var target = Path.Combine(projectRoot, TargetFolder);

			if (Directory.Exists(target))
			{
				Directory.Delete(target, true);
			}

			var count = CopyDirectory(source, target);

			ApplyProjectSettings(Path.Combine(source, SettingsFile));
			AssetDatabase.Refresh();
			Debug.Log($"[ModStubsSync] {count} files copied to {TargetFolder}.");
		}

		[System.Serializable]
		private class SortingLayerData
		{
			public string name;
			public long uniqueID;
			public int locked;
		}

		[System.Serializable]
		private class ProjectSettingsData
		{
			public string[] tags;
			public string[] layers;
			public SortingLayerData[] sortingLayers;
		}

		/// <summary>Mirrors the game's tags, layers and sorting layers so the mod project uses the same indices and IDs.</summary>
		private static void ApplyProjectSettings(string settingsPath)
		{
			if (!File.Exists(settingsPath))
			{
				Debug.LogWarning($"[ModStubsSync] {SettingsFile} not found, tags and layers were not synced.");

				return;
			}

			var data = JsonUtility.FromJson<ProjectSettingsData>(File.ReadAllText(settingsPath));
			var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

			var tags = tagManager.FindProperty("tags");

			tags.arraySize = data.tags.Length;

			for (var i = 0; i < data.tags.Length; i++)
			{
				tags.GetArrayElementAtIndex(i).stringValue = data.tags[i];
			}

			var layers = tagManager.FindProperty("layers");

			for (var i = 0; i < data.layers.Length && i < layers.arraySize; i++)
			{
				layers.GetArrayElementAtIndex(i).stringValue = data.layers[i];
			}

			var sortingLayers = tagManager.FindProperty("m_SortingLayers");

			sortingLayers.arraySize = data.sortingLayers.Length;

			for (var i = 0; i < data.sortingLayers.Length; i++)
			{
				var element = sortingLayers.GetArrayElementAtIndex(i);

				element.FindPropertyRelative("name").stringValue = data.sortingLayers[i].name;
				element.FindPropertyRelative("uniqueID").longValue = data.sortingLayers[i].uniqueID;
				element.FindPropertyRelative("locked").intValue = data.sortingLayers[i].locked;
			}

			tagManager.ApplyModifiedProperties();
			AssetDatabase.SaveAssets();
			Debug.Log($"[ModStubsSync] Synced {data.tags.Length} tags, layers and {data.sortingLayers.Length} sorting layers.");
		}

		private static int CopyDirectory(string source, string target)
		{
			var count = 0;

			Directory.CreateDirectory(target);

			foreach (var file in Directory.GetFiles(source))
			{
				if (Path.GetFileName(file) == SettingsFile)
				{
					continue;
				}

				File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
				count++;
			}

			foreach (var directory in Directory.GetDirectories(source))
			{
				count += CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
			}

			return count;
		}
	}
}
