using System;
using UnityEngine;

/**
 * Draws a dropdown whose options come from a method.
 * The method is looked up on the object owning the field (instance or static), or can be given as "TypeName.Method" for a static method elsewhere.
 * It must return an IEnumerable of ValueDropdownItem, or of plain values (string, int, enum, UnityEngine.Object...).
 * Item names containing '/' create sub menus. When placed on a list, it applies to each element.
 */
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class ValueDropdownAttribute : PropertyAttribute
{
	public readonly string MethodName;

	public ValueDropdownAttribute(string methodName)
	{
		MethodName = methodName;
	}
}

public class ValueDropdownItem
{
	public readonly string Name;
	public readonly object Value;

	public ValueDropdownItem(string name, object value)
	{
		Name = name;
		Value = value;
	}
}
