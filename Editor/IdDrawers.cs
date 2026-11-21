using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ColorlessOdyssey
{
	/// <summary>
	/// Shared dropdown drawing of the string based ids (weapon, aoe, ray). Mods are grouped by namespace, base game ids are at the root.
	/// </summary>
	internal static class IdDrawerUtility
	{
		public static void Draw(Rect position, SerializedProperty property, GUIContent label, string title, Func<List<string>> getIds)
		{
			var valueProperty = property.FindPropertyRelative("_value");
			var currentText = string.IsNullOrEmpty(valueProperty.stringValue) ? "<none>" : ProjectileIdRegistry.ToDisplayName(valueProperty.stringValue);

			EditorGUI.BeginProperty(position, label, property);

			position = EditorGUI.PrefixLabel(position, label);

			if (EditorGUI.DropdownButton(position, new GUIContent(currentText), FocusType.Keyboard))
			{
				var serializedObject = valueProperty.serializedObject;
				var path = valueProperty.propertyPath;
				var dropdown = new IdDropdown(new AdvancedDropdownState(), title, getIds(), selected =>
				{
					serializedObject.Update();
					serializedObject.FindProperty(path).stringValue = selected;
					serializedObject.ApplyModifiedProperties();
				});

				dropdown.Show(position);
			}

			EditorGUI.EndProperty();
		}

		private class IdDropdown : AdvancedDropdown
		{
			private class Item : AdvancedDropdownItem
			{
				public readonly string Value;

				public Item(string name, string value) : base(name)
				{
					Value = value;
				}
			}

			private readonly string _title;
			private readonly List<string> _ids;
			private readonly Action<string> _onSelected;

			public IdDropdown(AdvancedDropdownState state, string title, List<string> ids, Action<string> onSelected) : base(state)
			{
				_title = title;
				_ids = ids;
				_onSelected = onSelected;
				minimumSize = new Vector2(0f, 300f);
			}

			protected override AdvancedDropdownItem BuildRoot()
			{
				var root = new AdvancedDropdownItem(_title);
				var groups = new Dictionary<string, AdvancedDropdownItem>();

				foreach (var id in _ids)
				{
					var separatorIndex = id.IndexOf(ProjectileIdRegistry.Separator);
					var parent = root;
					var name = ProjectileIdRegistry.ToDisplayName(id);

					if (separatorIndex >= 0 && !ProjectileIdRegistry.IsBaseGameId(id))
					{
						var group = id[..separatorIndex];
						name = ProjectileIdRegistry.ToDisplayName(id[(separatorIndex + 1)..]);

						if (!groups.TryGetValue(group, out parent))
						{
							parent = new AdvancedDropdownItem(ProjectileIdRegistry.ToDisplayName(group));
							groups[group] = parent;
							root.AddChild(parent);
						}
					}

					parent.AddChild(new Item(name, id));
				}

				return root;
			}

			protected override void ItemSelected(AdvancedDropdownItem item)
			{
				if (item is Item idItem)
				{
					_onSelected(idItem.Value);
				}
			}
		}
	}

	[CustomPropertyDrawer(typeof(WeaponId))]
	public class WeaponIdDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			IdDrawerUtility.Draw(position, property, label, "Weapon Id", () =>
			{
				var ids = new List<string>();

				foreach (var entry in WeaponIdRegistry.GetAll())
				{
					ids.Add(entry.Id.Value);
				}

				return ids;
			});
		}
	}

	[CustomPropertyDrawer(typeof(AoeId))]
	public class AoeIdDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			IdDrawerUtility.Draw(position, property, label, "Aoe Id", () =>
			{
				var ids = new List<string>();

				foreach (var entry in AoeIdRegistry.GetAll())
				{
					ids.Add(entry.Id.Value);
				}

				return ids;
			});
		}
	}

	[CustomPropertyDrawer(typeof(RayId))]
	public class RayIdDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			IdDrawerUtility.Draw(position, property, label, "Ray Id", () =>
			{
				var ids = new List<string>();

				foreach (var entry in RayIdRegistry.GetAll())
				{
					ids.Add(entry.Id.Value);
				}

				return ids;
			});
		}
	}
}
