using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds "Copy as Description Variable" entries to the context menu of every property in the Inspector.
/// </summary>
[InitializeOnLoad]
public static class PropertyPathContextMenu
{
	static PropertyPathContextMenu()
	{
		EditorApplication.contextualPropertyMenu -= OnPropertyContextMenu;
		EditorApplication.contextualPropertyMenu += OnPropertyContextMenu;
	}

	private static void OnPropertyContextMenu(GenericMenu menu, SerializedProperty property)
	{
		var path = property.propertyPath;

		menu.AddSeparator("");
		menu.AddItem(new GUIContent("Copy as Description Variable"), false, () =>
		{
			EditorGUIUtility.systemCopyBuffer = $"<{InspectorPathConverter.FromPropertyPath(path, false).Split('.').Last()}>";
		});
		menu.AddItem(new GUIContent("Copy as Description Variable Deep"), false, () =>
		{
			EditorGUIUtility.systemCopyBuffer = $"<{InspectorPathConverter.FromPropertyPath(path, false)}>";
		});
		menu.AddItem(new GUIContent("Copy as Description Variable With Index"), false, () =>
		{
			EditorGUIUtility.systemCopyBuffer = $"<{InspectorPathConverter.FromPropertyPath(path, true)}>";
		});
	}
}

/// <summary>
/// Converts a Unity SerializedProperty path into the dot-notation format expected by
/// StringUtility.InsertVariablesFromClass,
/// e.g. "_shootSettings.Array.data[0].Speed" -> "_shootSettings[0].Speed".
/// </summary>
public static class InspectorPathConverter
{
	private static readonly Regex ArrayData = new (@"\.Array\.data\[(\d+)\]", RegexOptions.Compiled);

	public static string FromPropertyPath(string propertyPath, bool includeArrayIndices)
	{
		return ArrayData.Replace(propertyPath, includeArrayIndices ? "[$1]" : string.Empty);
	}
}
