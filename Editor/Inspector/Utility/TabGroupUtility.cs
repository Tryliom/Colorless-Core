using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ColorlessCore.Inspector.Editor
{
    /// <summary>
    /// Shared logic for Tab groups, used by the standard inspector (layout based) and by custom property drawers (rect based).
    /// </summary>
    public static class TabGroupUtility
    {
        public const float BoxPaddingLeft = 18f;
        public const float BoxPaddingRight = 6f;
        public const float BoxPaddingVertical = 6f;

        private static GUIStyle _boxStyle;

        public sealed class TabGroup
        {
            public string Key;
            public string StateKey;
            public List<string> Names = new();
            public int Selected;

            public string SelectedName => Names[Selected];

            public void Select(int index)
            {
                Selected = index;
                SessionState.SetInt(StateKey, index);
            }
        }

        public static GUIStyle BoxStyle
        {
            get
            {
                return _boxStyle ??= new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset((int) BoxPaddingLeft, (int) BoxPaddingRight, (int) BoxPaddingVertical, (int) BoxPaddingVertical)
                };
            }
        }

        public static TabGroup Create(string key, IEnumerable<string> names, string stateKey)
        {
            var group = new TabGroup { Key = key, StateKey = stateKey, Names = names.Distinct().ToList() };

            group.Selected = Mathf.Clamp(SessionState.GetInt(stateKey, 0), 0, Mathf.Max(0, group.Names.Count - 1));

            return group;
        }

        /// <summary>
        /// Builds one group per Tab group key, with the names of its tabs in declaration order.
        /// </summary>
        public static Dictionary<string, TabGroup> BuildGroups(IEnumerable<TabAttribute> tabs, Func<string, string> stateKeyFor)
        {
            var namesByGroup = new Dictionary<string, List<string>>();

            foreach (var tab in tabs)
            {
                if (!namesByGroup.TryGetValue(tab.Group, out var names))
                {
                    names = new List<string>();
                    namesByGroup[tab.Group] = names;
                }

                names.Add(tab.Name);
            }

            return namesByGroup.ToDictionary(pair => pair.Key, pair => Create(pair.Key, pair.Value, stateKeyFor(pair.Key)));
        }

        /// <summary>
        /// Width of the widest label among the given properties, capped relatively to the available width.
        /// Returns 0 when there is nothing to measure.
        /// </summary>
        public static float MeasureLabelWidth(IEnumerable<SerializedProperty> properties, float availableWidth)
        {
            var widest = NaughtyListDrawer.MeasureWidestLabel(properties.Where(p => p != null).ToList());

            return widest > 0f ? Mathf.Min(widest, availableWidth * 0.6f) : 0f;
        }
    }
}
