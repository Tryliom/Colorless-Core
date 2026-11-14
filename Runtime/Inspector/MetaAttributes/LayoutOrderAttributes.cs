using System;

namespace ColorlessCore.Inspector
{
	/// <summary>Sorts the member in the inspector. Lower values are drawn first, members without it count as 0.</summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public class PropertyOrderAttribute : MetaAttribute
	{
		public int Order { get; private set; }

		public PropertyOrderAttribute(int order)
		{
			Order = order;
		}
	}

	/// <summary>Adds vertical space before the member.</summary>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
	public class PropertySpaceAttribute : MetaAttribute
	{
		public float Space { get; private set; }

		public PropertySpaceAttribute(float space = 8f)
		{
			Space = space;
		}
	}

	/// <summary>Customizes how a list is drawn. SerializeReference lists also get a type picker.</summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
	public class ListDrawerSettingsAttribute : MetaAttribute
	{
		public bool ShowFoldout = true;
		public bool ShowIndexLabels = false;
		public int NumberOfItemsPerPage = 0;
		public string CustomAddFunction;
		public bool HideReferenceObjectPicker = false;
	}
}
