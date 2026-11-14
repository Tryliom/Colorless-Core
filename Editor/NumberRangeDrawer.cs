using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(NumberRange))]
[CustomPropertyDrawer(typeof(NumberRangeSettingsAttribute))]
public class NumberRangeDrawer : PropertyDrawer
{
	private const float Spacing = 4f;
	private const float TypeWidth = 50f;
	private const float ConditionWidth = 80f;
	private const float FieldWidth = 50f;

	public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
	{
		var settings = attribute as NumberRangeSettingsAttribute;
		var typeProp = property.FindPropertyRelative(nameof(NumberRange.Type));
		var conditionProp = property.FindPropertyRelative(nameof(NumberRange.Condition));
		var valueProp = property.FindPropertyRelative(nameof(NumberRange.Value));
		var maxProp = property.FindPropertyRelative(nameof(NumberRange.MaxValue));

		EditorGUI.BeginProperty(position, label, property);

		var indent = EditorGUI.indentLevel;
		position = EditorGUI.PrefixLabel(position, label);
		EditorGUI.indentLevel = 0;

		var typeForced = settings != null && settings.HasType;
		var typeRect = new Rect(position.x, position.y, typeForced ? 0f : TypeWidth, position.height);
		var conditionX = typeForced ? position.x : typeRect.xMax + Spacing;
		var conditionRect = new Rect(conditionX, position.y, ConditionWidth, position.height);
		var rest = new Rect(conditionRect.xMax + Spacing, position.y, position.xMax - conditionRect.xMax - Spacing, position.height);

		if (typeForced)
		{
			if (typeProp.enumValueIndex != (int) settings.Type)
			{
				typeProp.enumValueIndex = (int) settings.Type;
			}
		}
		else
		{
			EditorGUI.PropertyField(typeRect, typeProp, GUIContent.none);
		}

		EditorGUI.PropertyField(conditionRect, conditionProp, GUIContent.none);

		var isInt = (NumberType) typeProp.enumValueIndex == NumberType.Int;
		var condition = (RangeCondition) conditionProp.enumValueIndex;
		var hasLimits = settings != null && settings.Max > settings.Min;
		var step = settings?.Step ?? 0f;

		if (isInt)
		{
			step = Mathf.Max(1f, Mathf.Round(step));
		}

		float Sanitize(float v)
		{
			if (step > 0f)
			{
				var origin = hasLimits ? settings.Min : 0f;
				v = origin + Mathf.Round((v - origin) / step) * step;
			}

			if (hasLimits)
			{
				v = Mathf.Clamp(v, settings.Min, settings.Max);
			}

			return isInt ? Mathf.Round(v) : v;
		}

		EditorGUI.BeginChangeCheck();

		var value = Sanitize(valueProp.floatValue);
		var maxValue = Sanitize(maxProp.floatValue);

		if (condition == RangeCondition.Between)
		{
			var minRect = new Rect(rest.x, rest.y, FieldWidth, rest.height);
			var maxRect = new Rect(rest.xMax - FieldWidth, rest.y, FieldWidth, rest.height);

			if (hasLimits)
			{
				var sliderRect = new Rect(minRect.xMax + Spacing, rest.y, maxRect.x - minRect.xMax - Spacing * 2f, rest.height);

				value = DrawNumber(minRect, value, isInt);
				maxValue = DrawNumber(maxRect, maxValue, isInt);

				var low = Mathf.Min(value, maxValue);
				var high = Mathf.Max(value, maxValue);
				EditorGUI.MinMaxSlider(sliderRect, ref low, ref high, settings.Min, settings.Max);
				value = low;
				maxValue = high;
			}
			else
			{
				var half = (rest.width - Spacing) * 0.5f;
				value = DrawNumber(new Rect(rest.x, rest.y, half, rest.height), value, isInt);
				maxValue = DrawNumber(new Rect(rest.x + half + Spacing, rest.y, half, rest.height), maxValue, isInt);
			}
		}
		else if (hasLimits)
		{
			var fieldRect = new Rect(rest.xMax - FieldWidth, rest.y, FieldWidth, rest.height);
			var sliderRect = new Rect(rest.x, rest.y, fieldRect.x - rest.x - Spacing, rest.height);

			value = GUI.HorizontalSlider(sliderRect, value, settings.Min, settings.Max);
			value = DrawNumber(fieldRect, value, isInt);
		}
		else
		{
			value = DrawNumber(rest, value, isInt);
		}

		if (EditorGUI.EndChangeCheck())
		{
			valueProp.floatValue = Sanitize(value);
			maxProp.floatValue = Sanitize(maxValue);
		}
		else if (!Mathf.Approximately(valueProp.floatValue, value) || !Mathf.Approximately(maxProp.floatValue, maxValue))
		{
			// Keeps stored data consistent when the type or the attribute settings change
			valueProp.floatValue = value;
			maxProp.floatValue = maxValue;
		}

		EditorGUI.indentLevel = indent;
		EditorGUI.EndProperty();
	}

	private static float DrawNumber(Rect rect, float value, bool isInt)
	{
		return isInt ? EditorGUI.IntField(rect, Mathf.RoundToInt(value)) : EditorGUI.FloatField(rect, value);
	}
}
