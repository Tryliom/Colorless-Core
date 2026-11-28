using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ColorlessOdyssey
{
	/// <summary>
	/// Shared dropdown drawing of the string based ids (weapon, aoe, ray, enemy). Mods are grouped by namespace, base game ids are at the root.
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
				var modGroups = new List<string>();
				string baseGroup = null;

				foreach (var id in _ids)
				{
					var group = GetGroup(id);

					if (group == null)
					{
						root.AddChild(new Item(ProjectileIdRegistry.ToDisplayName(id), id));
					}
					else if (ProjectileIdRegistry.IsBaseGameId(id))
					{
						baseGroup = group;
					}
					else if (!modGroups.Contains(group))
					{
						modGroups.Add(group);
					}
				}

				modGroups.Sort(StringComparer.Ordinal);

				// The base game folder comes first, then one folder per mod
				if (baseGroup != null)
				{
					var folder = new AdvancedDropdownItem("Colorless Odyssey");
					groups[baseGroup] = folder;
					root.AddChild(folder);
				}

				foreach (var group in modGroups)
				{
					var folder = new AdvancedDropdownItem(ProjectileIdRegistry.ToDisplayName(group));
					groups[group] = folder;
					root.AddChild(folder);
				}

				foreach (var id in _ids)
				{
					var group = GetGroup(id);

					if (group != null)
					{
						var name = ProjectileIdRegistry.ToDisplayName(id[(id.IndexOf(ProjectileIdRegistry.Separator) + 1)..]);

						groups[group].AddChild(new Item(name, id));
					}
				}

				return root;
			}

			private static string GetGroup(string id)
			{
				var separatorIndex = string.IsNullOrEmpty(id) ? -1 : id.IndexOf(ProjectileIdRegistry.Separator);

				return separatorIndex < 0 ? null : id[..separatorIndex];
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

	[CustomPropertyDrawer(typeof(ProjectileId))]
	public class ProjectileIdDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			IdDrawerUtility.Draw(position, property, label, "Projectile Id", () =>
			{
				var ids = new List<string>();

				foreach (var entry in ProjectileIdRegistry.GetAll())
				{
					ids.Add(entry.Id.Value ?? string.Empty);
				}

				return ids;
			});
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

	[CustomPropertyDrawer(typeof(EnemyId))]
	public class EnemyIdDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			IdDrawerUtility.Draw(position, property, label, "Enemy Id", () =>
			{
				var ids = new List<string>();

				foreach (var entry in EnemyIdRegistry.GetAll())
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
