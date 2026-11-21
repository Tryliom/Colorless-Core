using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ColorlessOdyssey
{
	[CustomPropertyDrawer(typeof(ProjectileId))]
	public class ProjectileIdDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			var valueProperty = property.FindPropertyRelative("_value");
			var currentText = string.IsNullOrEmpty(valueProperty.stringValue) ? "<none>" : ProjectileIdRegistry.ToDisplayName(valueProperty.stringValue);

			EditorGUI.BeginProperty(position, label, property);

			position = EditorGUI.PrefixLabel(position, label);

			if (EditorGUI.DropdownButton(position, new GUIContent(currentText), FocusType.Keyboard))
			{
				var serializedObject = valueProperty.serializedObject;
				var path = valueProperty.propertyPath;
				var dropdown = new ProjectileIdDropdown(new AdvancedDropdownState(), selected =>
				{
					serializedObject.Update();
					serializedObject.FindProperty(path).stringValue = selected;
					serializedObject.ApplyModifiedProperties();
				});

				dropdown.Show(position);
			}

			EditorGUI.EndProperty();
		}

		private class ProjectileIdDropdown : AdvancedDropdown
		{
			private class Item : AdvancedDropdownItem
			{
				public readonly string Value;

				public Item(string name, string value) : base(name)
				{
					Value = value;
				}
			}

			private readonly Action<string> _onSelected;

			public ProjectileIdDropdown(AdvancedDropdownState state, Action<string> onSelected) : base(state)
			{
				_onSelected = onSelected;
				minimumSize = new Vector2(0f, 300f);
			}

			protected override AdvancedDropdownItem BuildRoot()
			{
				var root = new AdvancedDropdownItem("Projectile Id");
				var groups = new Dictionary<string, AdvancedDropdownItem>();

				foreach (var entry in ProjectileIdRegistry.GetAll())
				{
					var id = entry.Id.Value ?? string.Empty;
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
				if (item is Item projectileItem)
				{
					_onSelected(projectileItem.Value);
				}
			}
		}
	}
}
