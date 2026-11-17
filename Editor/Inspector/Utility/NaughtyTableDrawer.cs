using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ColorlessCore.Inspector.Editor
{
    /// <summary>
    /// Draws an IList as a table through reflection: one row per element, one column per member (or per group).
    /// Works for serialized and non serialized lists, including polymorphic elements.
    /// </summary>
    public static class NaughtyTableDrawer
    {
        private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const float RowButtonWidth = 22f;
        private const float MinColumnWidth = 60f;
        private const float NestedLabelWidth = 70f;
        private const int MaxDepth = 4;

        private class Member
        {
            public string Name;
            public int Order;
            public float Width;
            public bool HideLabel;
            public FieldInfo Field;
            public PropertyInfo Property;
            public MethodInfo Method;
            public string ButtonText;
            public bool ReadOnly;
        }

        private class Column
        {
            public string Key;
            public string Header;
            public bool Horizontal;
            public int Order;
            public int Index;
            public float Width;
            public float ComputedWidth;
            public List<Member> Members = new List<Member>();
        }

        private static readonly Dictionary<Type, List<Member>> MemberCache = new Dictionary<Type, List<Member>>();
        private static readonly Dictionary<int, List<Column>> ColumnCache = new Dictionary<int, List<Column>>();
        private static readonly Dictionary<FieldInfo, bool> PropertyDrawerCache = new Dictionary<FieldInfo, bool>();
        private static readonly Dictionary<(Column, Type), List<Member>> CellMembersCache = new Dictionary<(Column, Type), List<Member>>();
        private static readonly Dictionary<string, bool> Foldouts = new Dictionary<string, bool>();
        private static UnityEngine.Object _owner;
        private static SerializedProperty _listProperty;
        private static SerializedProperty _elementProperty;
        private static HashSet<Type> _drawerTypes;
        private static float _lastWidth;
        private static readonly Dictionary<(int, string), float> MeasuredHeights = new Dictionary<(int, string), float>();

        private static (int, string) MeasureKey(SerializedProperty property)
        {
            return (property.serializedObject.targetObject != null ? property.serializedObject.targetObject.GetHashCode() : 0, property.propertyPath);
        }

        private static void RepaintAll()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                window.Repaint();
            }
        }

        /// <summary>Returns the last measured height of a table drawn for this property, or an estimate before the first repaint.</summary>
        public static float GetHeight(SerializedProperty property)
        {
            if (MeasuredHeights.TryGetValue(MeasureKey(property), out var height))
            {
                return height;
            }

            return 22f + 22f + Mathf.Max(0, property.arraySize) * 26f + 4f;
        }

        public static void Draw(IList list, UnityEngine.Object owner, string label, bool isReadOnly, Action<bool> onChanged = null,
            SerializedProperty listProperty = null)
        {
            _owner = owner;
            _listProperty = listProperty;
            _elementProperty = null;

            var changed = false;
            var elementType = GetElementType(list);
            var canEdit = !isReadOnly && !list.IsReadOnly && !list.IsFixedSize;
            var outer = EditorGUILayout.BeginVertical();

            if (Event.current.type == EventType.Repaint && outer.width > 1f)
            {
                _lastWidth = outer.width;
            }

            DrawHeader(list, label, elementType, canEdit, ref changed);

            var columns = BuildColumns(list);

            ComputeWidths(columns, canEdit);

            if (list.Count > 0 && columns.Count > 0)
            {
                DrawColumnHeaders(columns, canEdit);
            }

            var removeAt = -1;

            for (var i = 0; i < list.Count; i++)
            {
                if (DrawRow(list, i, columns, canEdit, isReadOnly, ref changed))
                {
                    removeAt = i;
                }
            }

            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.Repaint)
            {
                DrawBorder(outer);

                if (listProperty != null && outer.height > 1f)
                {
                    var key = MeasureKey(listProperty);

                    if (!MeasuredHeights.TryGetValue(key, out var previous) || Mathf.Abs(previous - outer.height) > 0.5f)
                    {
                        MeasuredHeights[key] = outer.height;
                        NaughtyListDrawer.ClearHeightCache();
                        EditorApplication.delayCall += RepaintAll;
                    }
                }
            }

            if (removeAt >= 0)
            {
                Mark();
                list.RemoveAt(removeAt);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(owner);
                onChanged?.Invoke(true);
                GUI.changed = true;
            }
        }

        private static void DrawHeader(IList list, string label, Type elementType, bool canEdit, ref bool changed)
        {
            var rect = EditorGUILayout.GetControlRect(false, 22f);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.26f, 0.26f, 0.26f) : new Color(0.72f, 0.72f, 0.72f));
            }

            var addWidth = canEdit ? RowButtonWidth + 4f : 0f;
            var labelRect = new Rect(rect.x + 6f, rect.y, rect.width - addWidth - 90f, rect.height);
            var countRect = new Rect(rect.xMax - addWidth - 86f, rect.y, 80f, rect.height);
            var countStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 12 };

            if (list.Count == 0)
            {
                countStyle.normal.textColor = Color.gray;
            }

            EditorGUI.LabelField(labelRect, label, EditorStyles.boldLabel);
            EditorGUI.LabelField(countRect, list.Count == 0 ? "Empty" : list.Count == 1 ? "1 Element" : list.Count + " Elements", countStyle);

            if (canEdit && GUI.Button(new Rect(rect.xMax - RowButtonWidth - 2f, rect.y + 1f, RowButtonWidth, rect.height - 2f),
                    EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.iconButton))
            {
                AddElement(list, elementType, () =>
                {
                    EditorUtility.SetDirty(_owner);
                    GUI.changed = true;
                });
            }
        }

        private static void DrawColumnHeaders(List<Column> columns, bool canEdit)
        {
            var rect = EditorGUILayout.BeginHorizontal();

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f) : new Color(0.78f, 0.78f, 0.78f));
            }

            foreach (var column in columns)
            {
                GUILayout.Label(column.Header == "-" ? string.Empty : column.Header, EditorStyles.miniBoldLabel, ColumnOptions(column));
            }

            if (canEdit)
            {
                GUILayout.Space(RowButtonWidth);
            }

            EditorGUILayout.EndHorizontal();
        }

        private static bool DrawRow(IList list, int index, List<Column> columns, bool canEdit, bool readOnly, ref bool changed)
        {
            var remove = false;
            var element = list[index];

            _elementProperty = _listProperty != null && _listProperty.isArray && index < _listProperty.arraySize
                ? _listProperty.GetArrayElementAtIndex(index)
                : null;

            var rect = EditorGUILayout.BeginHorizontal();

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, index % 2 == 0 ? new Color(1f, 1f, 1f, 0.04f) : new Color(0f, 0f, 0f, 0.09f));
            }

            if (element == null)
            {
                GUILayout.Label("null", EditorStyles.miniLabel);
            }
            else if (IsSimple(element.GetType()))
            {
                var value = element;

                if (DrawValue(ref value, string.Empty, readOnly, 0))
                {
                    list[index] = value;
                    changed = true;
                }
            }
            else
            {
                var type = element.GetType();

                foreach (var column in columns)
                {
                    DrawCell(column, element, type, readOnly, ref changed);
                }

                if (element.GetType().IsValueType)
                {
                    list[index] = element;
                }
            }

            if (canEdit)
            {
                GUILayout.FlexibleSpace();
                GUILayout.BeginVertical(GUILayout.Width(RowButtonWidth));
                GUILayout.FlexibleSpace();

                var removeStyle = new GUIStyle(EditorStyles.iconButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0)
                };

                if (GUILayout.Button("x", removeStyle, GUILayout.Width(RowButtonWidth - 2f), GUILayout.Height(20f)))
                {
                    remove = true;
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();

            return remove;
        }

        private static void DrawCell(Column column, object element, Type type, bool readOnly, ref bool changed)
        {
            var cellKey = (column, type);

            if (!CellMembersCache.TryGetValue(cellKey, out var members))
            {
                members = column.Members.Where(m => IsMemberOf(m, type)).ToList();
                CellMembersCache[cellKey] = members;
            }

            if (column.Horizontal)
            {
                EditorGUILayout.BeginHorizontal(ColumnOptions(column));
            }
            else
            {
                EditorGUILayout.BeginVertical(ColumnOptions(column));
            }

            var showLabels = members.Count > 1 && !column.Horizontal;

            foreach (var member in members)
            {
                if (DrawMember(member, element, showLabels && !member.HideLabel, readOnly))
                {
                    changed = true;
                }
            }

            if (column.Horizontal)
            {
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.EndVertical();
            }
        }

        private static bool DrawMember(Member member, object element, bool showLabel, bool readOnly)
        {
            var label = showLabel ? ObjectNames.NicifyVariableName(member.Name) : string.Empty;

            if (member.Method != null)
            {
                var text = string.IsNullOrEmpty(member.ButtonText) ? ObjectNames.NicifyVariableName(member.Name) : member.ButtonText;

                if (GUILayout.Button(text))
                {
                    member.Method.Invoke(element, member.Method.GetParameters().Select(p => p.DefaultValue).ToArray());
                }

                return false;
            }

            var value = member.Field != null ? member.Field.GetValue(element) : member.Property.GetValue(element);
            var memberReadOnly = readOnly || member.ReadOnly;

            if (!memberReadOnly && member.Field != null && _elementProperty != null && (value is IDictionary || value is IList) && !(value is Array))
            {
                var container = _elementProperty.FindPropertyRelative(member.Name);

                if (container != null)
                {
                    var owner = _owner;
                    var listProperty = _listProperty;
                    var elementProperty = _elementProperty;

                    var height = NaughtyListDrawer.GetHeight(container);
                    var rect = GUILayoutUtility.GetRect(1f, height, GUILayout.ExpandWidth(true));

                    EditorGUI.BeginChangeCheck();
                    NaughtyListDrawer.Field(rect, container, new GUIContent(label));

                    if (EditorGUI.EndChangeCheck())
                    {
                        Mark();
                        container.serializedObject.ApplyModifiedProperties();
                    }

                    _owner = owner;
                    _listProperty = listProperty;
                    _elementProperty = elementProperty;

                    return false;
                }
            }

            if (!memberReadOnly && member.Field != null && _elementProperty != null && UsesPropertyDrawer(member.Field))
            {
                var property = _elementProperty.FindPropertyRelative(member.Name);

                if (property != null)
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(property, new GUIContent(label), true);

                    if (EditorGUI.EndChangeCheck())
                    {
                        Mark();
                        property.serializedObject.ApplyModifiedProperties();
                    }

                    return false;
                }
            }

            if (!DrawValue(ref value, label, memberReadOnly, 0, member.Field?.FieldType ?? member.Property.PropertyType))
            {
                return false;
            }

            Mark();
            member.Field?.SetValue(element, value);

            return true;
        }

        /// <summary>Draws any value. Returns true when the value changed and must be written back by the caller.</summary>
        private static bool DrawValue(ref object value, string label, bool readOnly, int depth, Type declaredType = null)
        {
            var type = value?.GetType() ?? declaredType;
            var oldLabelWidth = EditorGUIUtility.labelWidth;

            EditorGUIUtility.labelWidth = string.IsNullOrEmpty(label) ? 1f : NestedLabelWidth;

            try
            {
                if (value == null)
                {
                    return DrawNull(ref value, label, readOnly, type);
                }

                if (value is IDictionary dictionary)
                {
                    return DrawDictionary(dictionary, label, readOnly, depth);
                }

                if (value is IList nested && !(value is Array && readOnly))
                {
                    return DrawNestedList(nested, label, readOnly, depth);
                }

                if (!IsSimple(type))
                {
                    return depth < MaxDepth && DrawObject(value, label, readOnly, depth);
                }

                if (readOnly)
                {
                    NaughtyEditorGUI.Field_Layout(value, label);

                    return false;
                }

                if (NaughtyEditorGUI.EditableField_Layout(value, label, out var newValue) && !Equals(value, newValue))
                {
                    value = newValue;

                    return true;
                }

                return false;
            }
            finally
            {
                EditorGUIUtility.labelWidth = oldLabelWidth;
            }
        }

        private static bool DrawNull(ref object value, string label, bool readOnly, Type type)
        {
            if (readOnly || type == null)
            {
                EditorGUILayout.LabelField(label, "null");

                return false;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var picked = EditorGUILayout.ObjectField(label, null, type, true);

                if (picked != null)
                {
                    value = picked;

                    return true;
                }

                return false;
            }

            if (type == typeof(string))
            {
                var text = EditorGUILayout.TextField(label, string.Empty);

                if (!string.IsNullOrEmpty(text))
                {
                    value = text;

                    return true;
                }

                return false;
            }

            EditorGUILayout.LabelField(label, "null");

            return false;
        }

        private static bool DrawObject(object obj, string label, bool readOnly, int depth)
        {
            var changed = false;
            var members = GetMembers(obj.GetType()).Where(m => m.Method == null).ToList();

            if (!string.IsNullOrEmpty(label) && members.Count > 0)
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            }

            foreach (var member in members)
            {
                var value = member.Field != null ? member.Field.GetValue(obj) : member.Property.GetValue(obj);
                var memberLabel = member.HideLabel ? string.Empty : ObjectNames.NicifyVariableName(member.Name);

                if (!DrawValue(ref value, memberLabel, readOnly || member.ReadOnly, depth + 1, member.Field?.FieldType ?? member.Property.PropertyType))
                {
                    continue;
                }

                Mark();
                member.Field?.SetValue(obj, value);
                changed = true;
            }

            return changed;
        }

        private static bool DrawNestedList(IList list, string label, bool readOnly, int depth)
        {
            var changed = false;
            var elementType = GetElementType(list);
            var canEdit = !readOnly && !list.IsReadOnly && !list.IsFixedSize;
            var removeAt = -1;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(label) ? "List" : label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(list.Count + (list.Count == 1 ? " Element" : " Elements"), EditorStyles.miniLabel, GUILayout.Width(75f));

            if (canEdit && GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.iconButton, GUILayout.Width(RowButtonWidth)))
            {
                AddElement(list, elementType, () =>
                {
                    EditorUtility.SetDirty(_owner);
                    GUI.changed = true;
                });
            }

            EditorGUILayout.EndHorizontal();

            for (var i = 0; i < list.Count; i++)
            {
                var element = list[i];

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();

                if (DrawValue(ref element, string.Empty, readOnly, depth + 1, elementType))
                {
                    Mark();
                    list[i] = element;
                    changed = true;
                }

                EditorGUILayout.EndVertical();

                if (canEdit && GUILayout.Button("X", GUILayout.Width(RowButtonWidth - 2f)))
                {
                    removeAt = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            if (removeAt >= 0)
            {
                Mark();
                list.RemoveAt(removeAt);
                changed = true;
            }

            return changed;
        }

        private static void AddElement(IList list, Type elementType, Action onAdded)
        {
            if (elementType == null)
            {
                return;
            }

            var candidates = new List<Type>();

            if (!elementType.IsAbstract && !elementType.IsInterface)
            {
                candidates.Add(elementType);
            }

            if (!elementType.IsValueType && elementType != typeof(string) && !typeof(UnityEngine.Object).IsAssignableFrom(elementType))
            {
                candidates.AddRange(TypeCache.GetTypesDerivedFrom(elementType)
                    .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && !typeof(UnityEngine.Object).IsAssignableFrom(t)));
            }

            if (candidates.Count == 0)
            {
                Mark();
                list.Add(null);
                onAdded();

                return;
            }

            if (candidates.Count == 1)
            {
                Mark();
                list.Add(CreateInstance(candidates[0]));
                onAdded();

                return;
            }

            var menu = new GenericMenu();

            foreach (var candidate in candidates)
            {
                var captured = candidate;

                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(captured.Name)), false, () =>
                {
                    Mark();
                    list.Add(CreateInstance(captured));
                    onAdded();
                });
            }

            menu.ShowAsContext();
        }

        private static object CreateInstance(Type type)
        {
            if (type == typeof(string))
            {
                return string.Empty;
            }

            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch (Exception)
            {
                return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            }
        }

        private static List<Column> BuildColumns(IList list)
        {
            var types = new HashSet<Type>();

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null && !IsSimple(list[i].GetType()))
                {
                    types.Add(list[i].GetType());
                }
            }

            var typesKey = 0;

            foreach (var type in types)
            {
                typesKey += type.GetHashCode() * 31 + 17;
            }

            if (ColumnCache.TryGetValue(typesKey, out var cachedColumns))
            {
                return cachedColumns;
            }

            var built = BuildColumns(types);

            ColumnCache[typesKey] = built;

            return built;
        }

        private static List<Column> BuildColumns(HashSet<Type> types)
        {
            var columns = new Dictionary<string, Column>();

            foreach (var type in types)
            {
                foreach (var member in GetMembers(type))
                {
                    var key = GetColumnKey(member, out var header, out var horizontal);

                    if (!columns.TryGetValue(key, out var column))
                    {
                        column = new Column { Key = key, Header = header, Horizontal = horizontal, Index = columns.Count, Order = member.Order };
                        columns[key] = column;
                    }

                    if (column.Members.All(m => m.Name != member.Name))
                    {
                        column.Members.Add(member);
                    }

                    column.Order = Math.Min(column.Order, member.Order);

                    if (member.Width > 0f)
                    {
                        column.Width = member.Width;
                    }
                }
            }

            foreach (var column in columns.Values)
            {
                column.Members = column.Members.OrderBy(m => m.Order).ToList();
            }

            return columns.Values.OrderBy(c => c.Order).ThenBy(c => c.Index).ToList();
        }

        private static string GetColumnKey(Member member, out string header, out bool horizontal)
        {
            var info = (MemberInfo)member.Field ?? (MemberInfo)member.Property ?? member.Method;
            var horizontalGroup = info.GetCustomAttribute<HorizontalGroupAttribute>(true);
            var verticalGroup = info.GetCustomAttribute<VerticalGroupAttribute>(true);

            horizontal = horizontalGroup != null;

            if (horizontalGroup != null)
            {
                header = horizontalGroup.Name;

                return "h:" + horizontalGroup.Name;
            }

            if (verticalGroup != null)
            {
                header = verticalGroup.Name;

                return "v:" + verticalGroup.Name;
            }

            header = member.Method != null ? string.Empty : ObjectNames.NicifyVariableName(member.Name);

            return "m:" + member.Name;
        }

        private static List<Member> GetMembers(Type type)
        {
            if (MemberCache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var members = new List<Member>();
            var hierarchy = new List<Type>();

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                hierarchy.Insert(0, t);
            }

            foreach (var t in hierarchy)
            {
                foreach (var field in t.GetFields(MemberFlags))
                {
                    var show = field.GetCustomAttribute<ShowNonSerializedFieldAttribute>(true) != null;
                    var serialized = field.IsPublic
                        ? field.GetCustomAttribute<NonSerializedAttribute>() == null
                        : field.GetCustomAttribute<SerializeField>() != null || field.GetCustomAttribute<SerializeReference>() != null;

                    if (field.Name.Contains("<") || field.GetCustomAttribute<HideInInspector>() != null || !(serialized || show))
                    {
                        continue;
                    }

                    members.Add(CreateMember(field, field.Name, !serialized || field.IsInitOnly || field.IsLiteral));
                }

                foreach (var property in t.GetProperties(MemberFlags))
                {
                    if (property.GetCustomAttribute<ShowNativePropertyAttribute>(true) == null || !property.CanRead || property.GetIndexParameters().Length > 0)
                    {
                        continue;
                    }

                    members.Add(CreateMember(property, property.Name, true));
                }

                foreach (var method in t.GetMethods(MemberFlags))
                {
                    var button = method.GetCustomAttribute<ButtonAttribute>(true);

                    if (button == null || !method.GetParameters().All(p => p.IsOptional))
                    {
                        continue;
                    }

                    var member = CreateMember(method, method.Name, false);

                    member.Method = method;
                    member.ButtonText = button.Text;
                    members.Add(member);
                }
            }

            MemberCache[type] = members;

            return members;
        }

        private static Member CreateMember(MemberInfo info, string name, bool readOnly)
        {
            return new Member
            {
                Name = name,
                Field = info as FieldInfo,
                Property = info as PropertyInfo,
                Order = info.GetCustomAttribute<PropertyOrderAttribute>(true)?.Order ?? 0,
                Width = info.GetCustomAttribute<TableColumnWidthAttribute>(true)?.Width ?? 0f,
                HideLabel = info.GetCustomAttribute<HideLabelAttribute>(true) != null,
                ReadOnly = readOnly || info.GetCustomAttribute<ReadOnlyAttribute>(true) != null
            };
        }

        private static bool IsMemberOf(Member member, Type type)
        {
            var declaring = (member.Field?.DeclaringType) ?? member.Property?.DeclaringType ?? member.Method.DeclaringType;

            return declaring.IsAssignableFrom(type);
        }

        private static GUILayoutOption[] ColumnOptions(Column column)
        {
            return new[] { GUILayout.Width(column.ComputedWidth), GUILayout.ExpandWidth(false) };
        }

        /// <summary>Gives every column an explicit width so the header and the rows always line up.</summary>
        private static void ComputeWidths(List<Column> columns, bool canEdit)
        {
            var available = _lastWidth > 1f ? _lastWidth : EditorGUIUtility.currentViewWidth - 40f;
            var fixedWidth = columns.Where(c => c.Width > 0f).Sum(c => c.Width);
            var flexCount = columns.Count(c => c.Width <= 0f);
            var spacing = columns.Count * 4f + 12f + (canEdit ? RowButtonWidth + 4f : 0f);
            var flexWidth = flexCount > 0 ? Mathf.Max(MinColumnWidth, (available - fixedWidth - spacing) / flexCount) : 0f;

            foreach (var column in columns)
            {
                column.ComputedWidth = column.Width > 0f ? column.Width : flexWidth;
            }
        }

        private static bool UsesPropertyDrawer(FieldInfo field)
        {
            if (PropertyDrawerCache.TryGetValue(field, out var cached))
            {
                return cached;
            }

            var result = ComputeUsesPropertyDrawer(field);

            PropertyDrawerCache[field] = result;

            return result;
        }

        private static bool ComputeUsesPropertyDrawer(FieldInfo field)
        {
            if (field.GetCustomAttributes(typeof(PropertyAttribute), true).Length > 0)
            {
                return true;
            }

            var type = field.FieldType;

            if (IsSimple(type) || typeof(IList).IsAssignableFrom(type))
            {
                return false;
            }

            if (_drawerTypes == null)
            {
                _drawerTypes = new HashSet<Type>();

                var typeField = typeof(CustomPropertyDrawer).GetField("m_Type", BindingFlags.Instance | BindingFlags.NonPublic);

                foreach (var drawer in TypeCache.GetTypesDerivedFrom<PropertyDrawer>())
                {
                    foreach (var attribute in drawer.GetCustomAttributes<CustomPropertyDrawer>(true))
                    {
                        if (typeField?.GetValue(attribute) is Type target)
                        {
                            _drawerTypes.Add(target);
                        }
                    }
                }
            }

            return _drawerTypes.Contains(type);
        }

        private static bool DrawDictionary(IDictionary dictionary, string label, bool readOnly, int depth)
        {
            var changed = false;
            var type = dictionary.GetType();
            var args = GetDictionaryArguments(type);
            var canEdit = !readOnly;
            var keys = dictionary.Keys.Cast<object>().ToList();
            object removeKey = null;
            var hasRemove = false;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(label) ? "Dictionary" : label, EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(keys.Count + (keys.Count == 1 ? " Element" : " Elements"), EditorStyles.miniLabel, GUILayout.Width(75f));

            if (canEdit && args != null && GUILayout.Button(EditorGUIUtility.IconContent("Toolbar Plus"), EditorStyles.iconButton, GUILayout.Width(RowButtonWidth)))
            {
                var newKey = CreateUnusedKey(dictionary, args[0]);

                if (newKey != null)
                {
                    Mark();
                    dictionary[newKey] = args[1] == typeof(string) ? string.Empty : (args[1].IsValueType ? Activator.CreateInstance(args[1]) : null);
                    EditorUtility.SetDirty(_owner);
                    GUI.changed = true;
                    changed = true;
                }
            }

            EditorGUILayout.EndHorizontal();

            foreach (var key in keys)
            {
                EditorGUILayout.BeginHorizontal();

                var keyText = key == null ? "null" : ObjectNames.NicifyVariableName(key.ToString());

                EditorGUILayout.LabelField(keyText, GUILayout.Width(Mathf.Max(40f, NestedLabelWidth)));

                var value = dictionary[key];

                EditorGUILayout.BeginVertical();

                if (DrawValue(ref value, string.Empty, readOnly, depth + 1, args?[1]))
                {
                    Mark();
                    dictionary[key] = value;
                    changed = true;
                }

                EditorGUILayout.EndVertical();

                if (canEdit && GUILayout.Button("X", GUILayout.Width(RowButtonWidth - 2f)))
                {
                    removeKey = key;
                    hasRemove = true;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            if (hasRemove)
            {
                Mark();
                dictionary.Remove(removeKey);
                changed = true;
            }

            return changed;
        }

        private static Type[] GetDictionaryArguments(Type type)
        {
            while (type != null)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                {
                    return type.GetGenericArguments();
                }

                type = type.BaseType;
            }

            return null;
        }

        private static object CreateUnusedKey(IDictionary dictionary, Type keyType)
        {
            if (keyType.IsEnum)
            {
                foreach (var value in Enum.GetValues(keyType))
                {
                    if (!dictionary.Contains(value))
                    {
                        return value;
                    }
                }

                return null;
            }

            if (keyType == typeof(string))
            {
                return dictionary.Contains(string.Empty) ? null : string.Empty;
            }

            if (keyType.IsValueType)
            {
                var key = Activator.CreateInstance(keyType);

                return dictionary.Contains(key) ? null : key;
            }

            return null;
        }

        private static bool IsSimple(Type type)
        {
            return type != null && (type.IsPrimitive || type.IsEnum || type == typeof(string) || typeof(UnityEngine.Object).IsAssignableFrom(type)
                || type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Vector2Int)
                || type == typeof(Vector3Int) || type == typeof(Color) || type == typeof(Bounds) || type == typeof(Rect)
                || type == typeof(RectInt) || type == typeof(decimal));
        }

        private static Type GetElementType(IList list)
        {
            var type = list.GetType();

            if (type.IsArray)
            {
                return type.GetElementType();
            }

            return type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
        }

        private static void Mark()
        {
            if (_owner != null)
            {
                Undo.RecordObject(_owner, "Edit Table");
            }
        }

        private static void DrawBorder(Rect rect)
        {
            var color = EditorGUIUtility.isProSkin ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.4f, 0.4f, 0.4f);

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }
    }
}
