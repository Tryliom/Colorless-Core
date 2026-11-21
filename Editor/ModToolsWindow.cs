using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace ColorlessOdyssey
{
	/// <summary>Mod menu: updates the Colorless packages (optionally on a given branch) and syncs the mod stubs.</summary>
	public class ModToolsWindow : EditorWindow
	{
		private const string BranchPrefKey = "ModToolsWindow.Branch.";

		private const string BranchPackageName = "com.tryliom.colorless-core";
		private const string BranchPlaceholder = "0.7.0";

		private static readonly string[] PackageNames = { "com.tryliom.colorless-core", "com.tryliom.my-inspector" };

		private readonly string[] _branches = new string[PackageNames.Length];
		private readonly bool[] _loaded = new bool[PackageNames.Length];

		private AddRequest _request;
		private string _requestPackage;

		[MenuItem("Tools/Colorless Core/Mod Tools", priority = 0)]
		public static void Open()
		{
			GetWindow<ModToolsWindow>("Mod Tools");
		}

		// The domain reload triggered by a package update serializes the request but drops the update callback
		private void OnEnable()
		{
			EditorApplication.update -= PollRequest;

			if (_request != null)
			{
				EditorApplication.update += PollRequest;
			}
		}

		private void OnDisable()
		{
			EditorApplication.update -= PollRequest;
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Packages", EditorStyles.boldLabel);

			for (var i = 0; i < PackageNames.Length; i++)
			{
				DrawPackage(i);
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Mod stubs", EditorStyles.boldLabel);

			using (new EditorGUI.DisabledScope(_request != null))
			{
				if (GUILayout.Button("Sync Mod Stubs"))
				{
					ModStubsSync.SyncNow();
				}
			}
		}

		private void DrawPackage(int index)
		{
			var packageName = PackageNames[index];
			var manifestValue = ReadManifestValue(packageName);
			var isGit = manifestValue != null && manifestValue.Contains(".git");

			if (!_loaded[index])
			{
				_loaded[index] = true;
				_branches[index] = EditorPrefs.GetString(BranchPrefKey + packageName, GetBranch(manifestValue));
			}

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField(packageName, EditorStyles.boldLabel);

			if (!isGit)
			{
				EditorGUILayout.HelpBox("Not referenced by a git URL in manifest.json (embedded or transitive), nothing to update.", MessageType.Info);
				EditorGUILayout.EndVertical();

				return;
			}

			if (packageName == BranchPackageName)
			{
				EditorGUI.BeginChangeCheck();

				var controlName = "ModToolsBranch";
				var rect = EditorGUILayout.GetControlRect();

				GUI.SetNextControlName(controlName);
				_branches[index] = EditorGUI.TextField(rect, new GUIContent("Branch", "Empty = the repository's default branch."), _branches[index]);

				if (string.IsNullOrEmpty(_branches[index]) && GUI.GetNameOfFocusedControl() != controlName)
				{
					var placeholderRect = new Rect(rect.x + EditorGUIUtility.labelWidth + 2, rect.y, rect.width - EditorGUIUtility.labelWidth - 2, rect.height);
					var color = GUI.color;

					GUI.color = new Color(1f, 1f, 1f, 0.4f);
					GUI.Label(placeholderRect, BranchPlaceholder, EditorStyles.label);
					GUI.color = color;
				}

				if (EditorGUI.EndChangeCheck())
				{
					EditorPrefs.SetString(BranchPrefKey + packageName, _branches[index]);
				}
			}
			else
			{
				_branches[index] = string.Empty;
			}

			using (new EditorGUI.DisabledScope(_request != null))
			{
				if (GUILayout.Button(_requestPackage == packageName ? "Updating..." : "Update"))
				{
					UpdatePackage(packageName, manifestValue, _branches[index]);
				}
			}

			EditorGUILayout.EndVertical();
		}

		private void UpdatePackage(string packageName, string manifestValue, string branch)
		{
			var url = StripFragment(manifestValue);
			var target = string.IsNullOrWhiteSpace(branch) ? url : $"{url}#{branch.Trim()}";
			
			_requestPackage = packageName;
			_request = Client.Add(target);
			EditorApplication.update += PollRequest;
		}

		private void PollRequest()
		{
			if (_request == null || !_request.IsCompleted)
			{
				return;
			}

			EditorApplication.update -= PollRequest;

			// After a domain reload the restored request can report Failure without an Error even though the update succeeded
			if (_request.Status == StatusCode.Failure && _request.Error != null)
			{
				Debug.LogError($"[ModTools] Update of {_requestPackage} failed: {_request.Error.message}");
			}

			_request = null;
			_requestPackage = null;
			Repaint();
		}

		private static string ReadManifestValue(string packageName)
		{
			var manifestPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Packages", "manifest.json");

			if (!File.Exists(manifestPath))
			{
				return null;
			}

			var match = Regex.Match(File.ReadAllText(manifestPath), "\"" + Regex.Escape(packageName) + "\"\\s*:\\s*\"([^\"]+)\"");

			return match.Success ? match.Groups[1].Value : null;
		}

		private static string StripFragment(string value)
		{
			var hash = value.IndexOf('#');

			return hash >= 0 ? value[..hash] : value;
		}

		/// <summary>Returns the branch/tag in the git URL fragment, or empty when there is none or it is a commit hash.</summary>
		private static string GetBranch(string value)
		{
			var hash = value?.IndexOf('#') ?? -1;

			if (hash < 0)
			{
				return string.Empty;
			}

			var fragment = value[(hash + 1)..];

			return Regex.IsMatch(fragment, "^[0-9a-f]{40}$") ? string.Empty : fragment;
		}
	}
}
