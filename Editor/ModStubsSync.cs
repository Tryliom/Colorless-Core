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

		[MenuItem("Tools/Colorless Core/Sync Mod Stubs")]
		public static void Sync()
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

			AssetDatabase.Refresh();
			Debug.Log($"[ModStubsSync] {count} files copied to {TargetFolder}.");
		}

		private static int CopyDirectory(string source, string target)
		{
			var count = 0;

			Directory.CreateDirectory(target);

			foreach (var file in Directory.GetFiles(source))
			{
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
