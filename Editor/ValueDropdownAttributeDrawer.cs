using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ValueDropdownAttribute))]
public class ValueDropdownAttributeDrawer : PropertyDrawer
{
	private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
	{
		var items = GetItems(property);

		if (items == null)
		{
			EditorGUI.HelpBox(position, $"ValueDropdown: cannot find a valid method '{((ValueDropdownAttribute) attribute).MethodName}'", MessageType.Error);

			return;
		}

		var rect = EditorGUI.PrefixLabel(position, label);
		var selected = items.FirstOrDefault(item => Matches(property, item.Value));
		var text = selected != null ? selected.Name : GetCurrentText(property);

		if (EditorGUI.DropdownButton(rect, new GUIContent(text), FocusType.Keyboard))
		{
			var menu = new GenericMenu();
			var target = property.Copy();

			foreach (var item in items)
			{
				var captured = item;

				menu.AddItem(new GUIContent(item.Name), selected == item, () =>
				{
					target.serializedObject.Update();
					Apply(target, captured.Value);
					target.serializedObject.ApplyModifiedProperties();
				});
			}

			menu.DropDown(rect);
		}
	}

	private List<ValueDropdownItem> GetItems(SerializedProperty property)
	{
		var methodName = ((ValueDropdownAttribute) attribute).MethodName;
		var parent = GetParentObject(property);
		MethodInfo method = null;

		if (parent != null)
		{
			method = parent.GetType().GetMethod(methodName, Flags);
		}

		if (method == null && methodName.Contains('.'))
		{
			var split = methodName.LastIndexOf('.');
			var typeName = methodName[..split];
			var type = AppDomain.CurrentDomain.GetAssemblies()
				.SelectMany(assembly => assembly.GetTypes())
				.FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);

			method = type?.GetMethod(methodName[(split + 1)..], Flags);
		}

		if (method == null || method.GetParameters().Length > 0)
		{
			return null;
		}

		if (method.Invoke(method.IsStatic ? null : parent, null) is not IEnumerable result)
		{
			return null;
		}

		var items = new List<ValueDropdownItem>();

		foreach (var entry in result)
		{
			if (entry is ValueDropdownItem item)
			{
				items.Add(item);
			}
			else if (entry != null)
			{
				items.Add(new ValueDropdownItem(entry is UnityEngine.Object obj ? obj.name : entry.ToString(), entry));
			}
		}

		return items;
	}

	// Walks the property path to find the object that declares the field
	private static object GetParentObject(SerializedProperty property)
	{
		object current = property.serializedObject.targetObject;
		var path = property.propertyPath.Replace(".Array.data[", "[").Split('.');

		for (var i = 0; i < path.Length - 1 && current != null; i++)
		{
			var element = path[i];
			var bracket = element.IndexOf('[');

			if (bracket >= 0)
			{
				var fieldName = element[..bracket];
				var index = int.Parse(element[(bracket + 1)..^1]);

				current = GetFieldValue(current, fieldName);

				if (current is IList list && index < list.Count)
				{
					current = list[index];
				}
				else
				{
					return null;
				}
			}
			else
			{
				current = GetFieldValue(current, element);
			}
		}

		// A list element is a direct child of the list, the owner is the object holding the list
		return current;
	}

	private static object GetFieldValue(object target, string name)
	{
		var type = target.GetType();

		while (type != null)
		{
			var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

			if (field != null)
			{
				return field.GetValue(target);
			}

			type = type.BaseType;
		}

		return null;
	}

	private static bool Matches(SerializedProperty property, object value)
	{
		switch (property.propertyType)
		{
			case SerializedPropertyType.String:
				return property.stringValue == (value as string);
			case SerializedPropertyType.Integer:
			case SerializedPropertyType.Enum:
				return value != null && (value.GetType().IsPrimitive || value.GetType().IsEnum) && property.intValue == Convert.ToInt32(value);
			case SerializedPropertyType.ObjectReference:
				return property.objectReferenceValue == value as UnityEngine.Object;
		}

		return false;
	}

	private static void Apply(SerializedProperty property, object value)
	{
		switch (property.propertyType)
		{
			case SerializedPropertyType.String:
				property.stringValue = value as string;
				break;
			case SerializedPropertyType.Integer:
			case SerializedPropertyType.Enum:
				property.intValue = Convert.ToInt32(value);
				break;
			case SerializedPropertyType.ObjectReference:
				property.objectReferenceValue = value as UnityEngine.Object;
				break;
		}
	}

	private static string GetCurrentText(SerializedProperty property)
	{
		switch (property.propertyType)
		{
			case SerializedPropertyType.String:
				return string.IsNullOrEmpty(property.stringValue) ? "None" : property.stringValue;
			case SerializedPropertyType.Integer:
				return property.intValue.ToString();
			case SerializedPropertyType.Enum:
				return property.enumDisplayNames.ElementAtOrDefault(property.enumValueIndex) ?? "None";
			case SerializedPropertyType.ObjectReference:
				return property.objectReferenceValue ? property.objectReferenceValue.name : "None";
		}

		return "Unsupported type";
	}
}
