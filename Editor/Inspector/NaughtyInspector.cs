using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ColorlessCore.Inspector.Editor
{
    [CanEditMultipleObjects]
    [CustomEditor(typeof(UnityEngine.Object), true)]
    public class NaughtyInspector : UnityEditor.Editor
    {
        private const int LayerCount = 4;

        private class Item
        {
            public SerializedProperty Property;
            public MethodInfo Method;
            public FieldInfo Field;
            public PropertyInfo Native;
            public int Order;
            public float Space;
            public string Title;
            public ListDrawerSettingsAttribute List;
            public TableListAttribute Table;
            public TabAttribute Tab;
            public FoldoutAttribute Foldout;
            public BoxGroupAttribute Box;
            public HorizontalGroupAttribute Horizontal;
            public VerticalGroupAttribute Vertical;

            public string Key(int layer)
            {
                switch (layer)
                {
                    case 0: return Tab?.Group;
                    case 1: return Foldout != null ? "f:" + Foldout.Name : Box != null ? "b:" + Box.Name : null;
                    case 2: return Horizontal?.Name;
                    case 3: return Vertical?.Name;
                    default: return null;
                }
            }

            public MemberInfo Member => Method ?? (MemberInfo)Field ?? Native;

            public bool HasGroup => Tab != null || Foldout != null || Box != null || Horizontal != null || Vertical != null;
        }

        private List<SerializedProperty> _serializedProperties = new List<SerializedProperty>();
        private IEnumerable<FieldInfo> _nonSerializedFields;
        private IEnumerable<PropertyInfo> _nativeProperties;
        private List<MethodInfo> _methods;
        private Dictionary<string, SavedBool> _foldouts = new Dictionary<string, SavedBool>();
        private string _foldoutPrefix = string.Empty;
        private List<Item> _items = new List<Item>();
        private Dictionary<string, UnityEditorInternal.ReorderableList> _lists = new Dictionary<string, UnityEditorInternal.ReorderableList>();
        private Dictionary<string, SavedBool> _listFoldouts = new Dictionary<string, SavedBool>();

        protected virtual void OnEnable()
        {
            _nonSerializedFields = ReflectionUtility.GetAllFields(
                target, f => f.GetCustomAttributes(typeof(ShowNonSerializedFieldAttribute), true).Length > 0).ToList();

            _nativeProperties = ReflectionUtility.GetAllProperties(
                target, p => p.GetCustomAttributes(typeof(ShowNativePropertyAttribute), true).Length > 0).ToList();

            _methods = ReflectionUtility.GetAllMethods(
                target, m => m.GetCustomAttributes(typeof(ButtonAttribute), true).Length > 0).ToList();
        }

        protected virtual void OnDisable()
        {
            ReorderableListPropertyDrawer.Instance.ClearCache();
        }

        public override void OnInspectorGUI()
        {
            var previousLabelWidth = EditorGUIUtility.labelWidth;

            // Unity defaults to ~45% of the view width, labels only need 30%
            EditorGUIUtility.labelWidth = Mathf.Max(80f, EditorGUIUtility.currentViewWidth * 0.3f);

            try
            {
                DrawInspector();
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        private void DrawInspector()
        {
            NaughtyListDrawer.BeginInspector(target);
            GetSerializedProperties(ref _serializedProperties);

            var anyNaughtyAttribute = _serializedProperties.Any(p => PropertyUtility.GetAttribute<INaughtyAttribute>(p) != null || IsSerializeReference(p))
                || _serializedProperties.Any(p => NaughtyListDrawer.IsList(p) || p.propertyType == SerializedPropertyType.Generic)
                || _nonSerializedFields.Any()
                || _nativeProperties.Any()
                || _methods.Any();

            if (!anyNaughtyAttribute)
            {
                DrawDefaultInspector();
            }
            else
            {
                DrawSerializedProperties();
            }
        }

        private static FieldInfo GetField(SerializedProperty property)
        {
            return ReflectionUtility.GetField(PropertyUtility.GetTargetObjectWithProperty(property), property.name);
        }

        private static bool IsSerializeReference(SerializedProperty property)
        {
            var field = GetField(property);

            return field != null && field.GetCustomAttribute<SerializeReference>() != null;
        }

        private static Type GetElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType)
            {
                return type.GetGenericArguments()[0];
            }

            return type;
        }

        private static int Depth(Type type)
        {
            var depth = 0;

            while (type != null)
            {
                depth++;
                type = type.BaseType;
            }

            return depth;
        }

        protected void GetSerializedProperties(ref List<SerializedProperty> outSerializedProperties)
        {
            outSerializedProperties.Clear();
            using (var iterator = serializedObject.GetIterator())
            {
                if (iterator.NextVisible(true))
                {
                    do
                    {
                        outSerializedProperties.Add(serializedObject.FindProperty(iterator.name));
                    }
                    while (iterator.NextVisible(false));
                }
            }
        }

        private static Item CreatePropertyItem(SerializedProperty property)
        {
            var field = GetField(property);
            var item = new Item
            {
                Property = property,
                Field = field,
                Tab = PropertyUtility.GetAttribute<TabAttribute>(property),
                Foldout = PropertyUtility.GetAttribute<FoldoutAttribute>(property),
                Box = PropertyUtility.GetAttribute<BoxGroupAttribute>(property),
                Horizontal = PropertyUtility.GetAttribute<HorizontalGroupAttribute>(property),
                Vertical = PropertyUtility.GetAttribute<VerticalGroupAttribute>(property)
            };

            FillCommon(item, field, false);

            return item;
        }

        private static Item CreateMemberItem(MemberInfo member)
        {
            var item = new Item
            {
                Method = member as MethodInfo,
                Field = member as FieldInfo,
                Native = member as PropertyInfo,
                Tab = member.GetCustomAttribute<TabAttribute>(true),
                Foldout = member.GetCustomAttribute<FoldoutAttribute>(true),
                Box = member.GetCustomAttribute<BoxGroupAttribute>(true),
                Horizontal = member.GetCustomAttribute<HorizontalGroupAttribute>(true),
                Vertical = member.GetCustomAttribute<VerticalGroupAttribute>(true)
            };

            FillCommon(item, member, true);

            return item;
        }

        private static void FillCommon(Item item, MemberInfo member, bool readTitle)
        {
            if (member == null)
            {
                return;
            }

            item.Order = member.GetCustomAttribute<PropertyOrderAttribute>(true)?.Order ?? 0;
            item.Space = member.GetCustomAttribute<PropertySpaceAttribute>(true)?.Space ?? 0f;
            item.List = member.GetCustomAttribute<ListDrawerSettingsAttribute>(true);
            item.Table = member.GetCustomAttribute<TableListAttribute>(true);

            if (readTitle)
            {
                item.Title = member.GetCustomAttribute<TitleAttribute>(true)?.Title;
            }
        }

        private static int FirstLayer(Item item)
        {
            for (var i = 0; i < LayerCount; i++)
            {
                if (item.Key(i) != null)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FirstLayerFrom(Item item, int from)
        {
            for (var i = from; i < LayerCount; i++)
            {
                if (item.Key(i) != null)
                {
                    return i;
                }
            }

            return -1;
        }

        protected void DrawSerializedProperties()
        {
            serializedObject.Update();

            var items = new List<Item>();
            var grouped = new List<Item>();

            foreach (var property in _serializedProperties)
            {
                items.Add(CreatePropertyItem(property));
            }

            // Non-serialized fields and native properties follow the closest serialized field declared before them
            foreach (var field in _nonSerializedFields)
            {
                var item = CreateMemberItem(field);

                if (item.HasGroup)
                {
                    grouped.Add(item);

                    continue;
                }

                var index = -1;
                var bestToken = -1;

                for (var i = 0; i < items.Count; i++)
                {
                    var other = items[i].Field;

                    if (other != null && other.DeclaringType == field.DeclaringType
                        && other.MetadataToken < field.MetadataToken && other.MetadataToken > bestToken)
                    {
                        bestToken = other.MetadataToken;
                        index = i;
                    }
                }

                if (index < 0)
                {
                    items.Add(item);
                }
                else
                {
                    items.Insert(index + 1, item);
                }
            }

            foreach (var native in _nativeProperties)
            {
                AddMember(CreateMemberItem(native), items, grouped);
            }

            foreach (var method in _methods)
            {
                AddMember(CreateMemberItem(method), items, grouped);
            }

            items = items.OrderBy(i => i.Order).ToList();

            foreach (var item in grouped)
            {
                var layer = FirstLayer(item);
                var key = item.Key(layer);
                var index = items.FindLastIndex(i => i.Key(layer) == key);

                if (index < 0)
                {
                    index = items.FindIndex(i => i.Order > item.Order);

                    if (index < 0)
                    {
                        items.Add(item);
                    }
                    else
                    {
                        items.Insert(index, item);
                    }
                }
                else
                {
                    items.Insert(index + 1, item);
                }
            }

            DrawLevel(items, 0);

            serializedObject.ApplyModifiedProperties();
        }

        private static void AddMember(Item item, List<Item> items, List<Item> grouped)
        {
            if (item.HasGroup)
            {
                grouped.Add(item);
            }
            else
            {
                items.Add(item);
            }
        }

        private void DrawLevel(List<Item> items, int depth)
        {
            if (depth >= LayerCount)
            {
                foreach (var item in items)
                {
                    DrawItem(item);
                }

                return;
            }

            var handled = new HashSet<string>();

            for (var i = 0; i < items.Count; i++)
            {
                var key = items[i].Key(depth);

                if (key == null)
                {
                    var deeper = FirstLayerFrom(items[i], depth + 1);

                    if (deeper < 0)
                    {
                        DrawLevel(new List<Item> { items[i] }, LayerCount);

                        continue;
                    }

                    // Items without a key here but sharing a deeper group are merged into the same deeper group
                    var deeperKey = items[i].Key(deeper);

                    if (!handled.Add($"{deeper}:{deeperKey}"))
                    {
                        continue;
                    }

                    var passThrough = items
                        .Where(x => x.Key(depth) == null && FirstLayerFrom(x, depth + 1) == deeper && x.Key(deeper) == deeperKey)
                        .ToList();

                    if (passThrough.Any(IsVisible))
                    {
                        DrawLevel(passThrough, depth + 1);
                    }

                    continue;
                }

                // Groups with the same name are merged even when not declared contiguously
                if (!handled.Add(key))
                {
                    continue;
                }

                var run = items.Where(x => x.Key(depth) == key).ToList();

                if (!run.Any(IsVisible))
                {
                    continue;
                }

                DrawGroup(run, depth, key);
            }
        }

        private void DrawGroup(List<Item> run, int depth, string key)
        {
            switch (depth)
            {
                case 0:
                    DrawTabs(run, depth, key);
                    break;
                case 1:
                    DrawFoldoutOrBox(run, depth, key);
                    break;
                case 2:
                    DrawHorizontal(run, depth);
                    break;
                default:
                    EditorGUILayout.BeginVertical();
                    DrawLevel(run, depth + 1);
                    EditorGUILayout.EndVertical();
                    break;
            }
        }

        private void DrawTabs(List<Item> run, int depth, string key)
        {
            var visible = run.Where(IsVisible).ToList();
            var names = visible.Select(i => i.Tab.Name).Distinct().ToList();
            var stateKey = $"NaughtyTab.{target.GetType().Name}.{key}.{string.Join("|", names)}";
            var selected = Mathf.Clamp(SessionState.GetInt(stateKey, 0), 0, names.Count - 1);

            selected = GUILayout.Toolbar(selected, names.ToArray());
            SessionState.SetInt(stateKey, selected);

            var boxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(18, 6, 6, 6) };

            EditorGUILayout.BeginVertical(boxStyle);
            DrawLevel(visible.Where(i => i.Tab.Name == names[selected]).ToList(), depth + 1);
            EditorGUILayout.EndVertical();
        }

        private void DrawFoldoutOrBox(List<Item> run, int depth, string key)
        {
            var isFoldout = key.StartsWith("f:", StringComparison.Ordinal);
            var displayName = key.Substring(2);
            var name = _foldoutPrefix + displayName;

            if (isFoldout)
            {
                if (!_foldouts.ContainsKey(name))
                {
#if UNITY_6000_4_OR_NEWER
                    _foldouts[name] = new SavedBool($"{EntityId.ToULong(target.GetEntityId())}.{name}", false);
#else
                    _foldouts[name] = new SavedBool($"{target.GetInstanceID()}.{name}", false);
#endif
                }

                var first = run[0].Property;
                var boolTitle = first != null && first.propertyType == SerializedPropertyType.Boolean
                    ? PropertyUtility.GetAttribute<BoolTitleAttribute>(first)
                    : null;

                var clean = run.Any(x => x.Foldout != null && x.Foldout.Clean);

                if (boolTitle != null)
                {
                    // The BoolTitle toggle acts as the foldout header; only the other fields are folded
                    if (!clean)
                    {
                        EditorGUILayout.BeginVertical();
                    }

                    var rect = clean ? EditorGUILayout.GetControlRect() : GetBoxHeaderRect();
                    var content = new GUIContent(boolTitle.Title ?? first.displayName, first.tooltip);

                    _foldouts[name].Value = EditorGUI.Foldout(new Rect(rect.x, rect.y, 5f, rect.height), _foldouts[name].Value, GUIContent.none, true);

                    var titleStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = Mathf.Max(8, (EditorStyles.boldLabel.fontSize > 0 ? EditorStyles.boldLabel.fontSize : 12) - 1)
                    };

                    EditorGUI.BeginProperty(rect, content, first);
                    first.boolValue = EditorGUI.ToggleLeft(new Rect(rect.x + 5f, rect.y, rect.width - 5f, rect.height), content, first.boolValue, titleStyle);
                    EditorGUI.EndProperty();

                    if (_foldouts[name].Value)
                    {
                        var enabled = first.boolValue;

                        // When unchecked, still list the grouped fields (greyed out) instead of hiding them
                        var previous = PropertyUtility.ForceVisible;
                        PropertyUtility.ForceVisible = previous || !enabled;

                        if (!clean)
                        {
                            EditorGUILayout.BeginVertical(GetBoxContentStyle());
                        }

                        EditorGUI.indentLevel++;

                        using (new EditorGUI.DisabledScope(!enabled))
                        {
                            DrawLevel(run.Skip(1).ToList(), depth + 1);
                        }

                        EditorGUI.indentLevel--;

                        if (!clean)
                        {
                            EditorGUILayout.EndVertical();
                        }

                        PropertyUtility.ForceVisible = previous;
                    }

                    if (!clean)
                    {
                        EditorGUILayout.EndVertical();
                        DrawOutline(GUILayoutUtility.GetLastRect());
                        GUILayout.Space(3f);
                    }

                    return;
                }

                if (clean)
                {
                    _foldouts[name].Value = EditorGUILayout.Foldout(_foldouts[name].Value, displayName, true);

                    if (_foldouts[name].Value)
                    {
                        DrawLevel(run, depth + 1);
                    }

                    return;
                }

                EditorGUILayout.BeginVertical();

                var headerRect = GetBoxHeaderRect();

                _foldouts[name].Value = EditorGUI.Foldout(headerRect, _foldouts[name].Value, displayName, true);

                if (_foldouts[name].Value)
                {
                    EditorGUILayout.BeginVertical(GetBoxContentStyle());
                    DrawLevel(run, depth + 1);
                    EditorGUILayout.EndVertical();
                }

                EditorGUILayout.EndVertical();
                DrawOutline(GUILayoutUtility.GetLastRect());
                GUILayout.Space(3f);
            }
            else
            {
                NaughtyEditorGUI.BeginBoxGroup_Layout(displayName);
                DrawLevel(run, depth + 1);
                NaughtyEditorGUI.EndBoxGroup_Layout();
            }
        }

        private static Texture2D _headerTexture;
        private static Texture2D _contentTexture;
        private static GUIStyle _contentStyle;

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };

            texture.SetPixel(0, 0, color);
            texture.Apply();

            return texture;
        }

        private static Rect GetBoxHeaderRect()
        {
            if (_headerTexture == null)
            {
                _headerTexture = MakeTexture(EditorGUIUtility.isProSkin ? new Color(0.26f, 0.26f, 0.26f) : new Color(0.72f, 0.72f, 0.72f));
            }

            var rect = GUILayoutUtility.            GetRect(0f, 26f, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, _headerTexture);
            }

                        rect.x += 18f;
                        rect.width -= 18f;
                        rect.y += 4f;
            rect.height = EditorGUIUtility.singleLineHeight;

            return rect;
        }

        private static GUIStyle GetBoxContentStyle()
        {
            if (_contentTexture == null || _contentStyle == null)
            {
                _contentTexture = MakeTexture(EditorGUIUtility.isProSkin ? new Color(0.19f, 0.19f, 0.19f) : new Color(0.78f, 0.78f, 0.78f));
                _contentStyle = new GUIStyle
                {
                    padding = new RectOffset(4, 4, 3, 3),
                    normal = { background = _contentTexture }
                };
            }

            return _contentStyle;
        }

        private void DrawHorizontal(List<Item> run, int depth)
        {
            var columns = new List<List<Item>>();

            foreach (var item in run)
            {
                var key = item.Key(depth + 1);
                var last = columns.Count > 0 ? columns[columns.Count - 1] : null;

                if (last != null && key != null && last[0].Key(depth + 1) == key)
                {
                    last.Add(item);
                }
                else
                {
                    columns.Add(new List<Item> { item });
                }
            }

            var totalWidth = EditorGUIUtility.currentViewWidth - 40f;
            var oldLabelWidth = EditorGUIUtility.labelWidth;

            EditorGUILayout.BeginHorizontal();

            foreach (var column in columns)
            {
                if (!column.Any(IsVisible))
                {
                    continue;
                }

                var width = column[0].Horizontal.Width;
                GUILayoutOption option;

                if (width <= 0f)
                {
                    option = GUILayout.ExpandWidth(true);
                }
                else
                {
                    option = GUILayout.Width(width < 1f ? width * totalWidth : width);
                }

                EditorGUILayout.BeginVertical(option);
                EditorGUIUtility.labelWidth = Mathf.Min(oldLabelWidth, Mathf.Max(50f, totalWidth / columns.Count * 0.45f));
                DrawLevel(column, depth + 1);
                EditorGUIUtility.labelWidth = oldLabelWidth;
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool IsVisible(Item item)
        {
            return item.Property != null
                ? PropertyUtility.IsVisible(item.Property)
                : ButtonUtility.IsVisible(target, item.Member);
        }

        private void DrawItem(Item item)
        {
            var property = item.Property;

            if (property != null && property.name.Equals("m_Script", StringComparison.Ordinal))
            {
                using (new EditorGUI.DisabledScope(disabled: true))
                {
                    EditorGUILayout.PropertyField(property);
                }

                return;
            }

            if (!IsVisible(item))
            {
                return;
            }

            if (item.Space > 0f)
            {
                GUILayout.Space(item.Space);
            }

            if (!string.IsNullOrEmpty(item.Title))
            {
                EditorGUILayout.LabelField(item.Title, EditorStyles.boldLabel);

                var line = EditorGUILayout.GetControlRect(false, 1f);

                EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.6f));
                GUILayout.Space(2f);
            }

            if (item.Method != null)
            {
                DrawButton(item.Method);
            }
            else if (item.Native != null)
            {
                NaughtyEditorGUI.NativeProperty_Layout(serializedObject.targetObject, item.Native);
            }
            else if (property == null)
            {
                DrawNonSerializedField(item.Field);
            }
            else if (NaughtyListDrawer.IsDictionary(property))
            {
                var rect = EditorGUILayout.GetControlRect(false, NaughtyListDrawer.GetHeight(property));

                NaughtyListDrawer.Field(rect, property);
            }
            else if (item.Table != null && property.isArray && property.propertyType != SerializedPropertyType.String
                && PropertyUtility.GetTargetObjectOfProperty(property) is System.Collections.IList tableList)
            {
                // Flush pending edits of previously drawn fields, otherwise the Update below would discard them
                serializedObject.ApplyModifiedProperties();
                NaughtyTableDrawer.Draw(tableList, target, ObjectNames.NicifyVariableName(property.name),                 item.Table.IsReadOnly, null, property);
                serializedObject.Update();
            }
            else if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                DrawList(item);
            }
            else if (property.propertyType == SerializedPropertyType.ManagedReference
                && item.Field != null && property.managedReferenceValue == null)
            {
                DrawNullReference(property, item.Field.FieldType);
            }
            else if (NaughtyListDrawer.IsContainer(property)
                && PropertyUtility.GetAttribute<InlinePropertyAttribute>(property) == null
                && PropertyUtility.GetAttribute<HideLabelAttribute>(property) == null)
            {
                DrawNested(property);
            }
            else
            {
                NaughtyEditorGUI.PropertyField_Layout(property, includeChildren: true);
            }
        }

        private static bool HasGroupedChildren(SerializedProperty property)
        {
            if (!NaughtyListDrawer.IsContainer(property))
            {
                return false;
            }

            var iterator = property.Copy();
            var end = iterator.GetEndProperty();

            if (!iterator.NextVisible(true))
            {
                return false;
            }

            do
            {
                if (SerializedProperty.EqualContents(iterator, end))
                {
                    return false;
                }

                if (PropertyUtility.GetAttribute<TabAttribute>(iterator) != null
                    || PropertyUtility.GetAttribute<FoldoutAttribute>(iterator) != null
                    || PropertyUtility.GetAttribute<BoxGroupAttribute>(iterator) != null
                    || PropertyUtility.GetAttribute<HorizontalGroupAttribute>(iterator) != null
                    || PropertyUtility.GetAttribute<VerticalGroupAttribute>(iterator) != null)
                {
                    return true;
                }
            }
            while (iterator.NextVisible(false));

            return false;
        }

        private void DrawNested(SerializedProperty property)
        {
            GUILayout.Space(5f);
            EditorGUILayout.BeginVertical();

            DrawNestedBox(property);

            EditorGUILayout.EndVertical();
            DrawOutline(GUILayoutUtility.GetLastRect());
            GUILayout.Space(3f);
        }

        private static void DrawOutline(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                NaughtyListDrawer.DrawOutline(rect);
            }
        }

        private void DrawNestedBox(SerializedProperty property)
        {
            var headerRect = GetBoxHeaderRect();

            property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, PropertyUtility.GetLabel(property), true);

            if (!property.isExpanded)
            {
                return;
            }

            var items = new List<Item>();
            var iterator = property.Copy();
            var end = iterator.GetEndProperty();

            if (iterator.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(iterator, end))
                    {
                        break;
                    }

                    items.Add(CreatePropertyItem(iterator.Copy()));
                }
                while (iterator.NextVisible(false));
            }

            var previousPrefix = _foldoutPrefix;

            _foldoutPrefix = property.propertyPath + ".";

            EditorGUILayout.BeginVertical(GetBoxContentStyle());
            DrawLevel(items.OrderBy(i => i.Order).ToList(), 0);
            EditorGUILayout.EndVertical();

            _foldoutPrefix = previousPrefix;
        }

        private void DrawNullReference(SerializedProperty property, Type baseType)
        {
            var path = property.propertyPath;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(PropertyUtility.GetLabel(property));

                if (EditorGUILayout.DropdownButton(new GUIContent("Select type..."), FocusType.Keyboard))
                {
                    ShowTypeMenu(baseType, type =>
                    {
                        serializedObject.Update();
                        serializedObject.FindProperty(path).managedReferenceValue = Activator.CreateInstance(type);
                        serializedObject.ApplyModifiedProperties();
                    });
                }
            }
        }

        private static void ShowTypeMenu(Type baseType, Action<Type> onSelected)
        {
            var types = TypeCache.GetTypesDerivedFrom(baseType).ToList();

            if (!baseType.IsInterface)
            {
                types.Add(baseType);
            }

            types = types
                .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsGenericType && !typeof(UnityEngine.Object).IsAssignableFrom(t))
                .OrderBy(t => t.Name)
                .ToList();

            var menu = new GenericMenu();

            if (types.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No type available"));
            }

            foreach (var type in types)
            {
                var captured = type;

                menu.AddItem(new GUIContent(ObjectNames.NicifyVariableName(type.Name)), false, () => onSelected(captured));
            }

            menu.ShowAsContext();
        }

        private void DrawList(Item item)
        {
            var rect = EditorGUILayout.GetControlRect(false, NaughtyListDrawer.GetHeight(item.Property));

            NaughtyListDrawer.Draw(rect, item.Property);
        }

        private void DrawNonSerializedField(FieldInfo field)
        {
            var infoBoxes = field.GetCustomAttributes<InfoBoxAttribute>(true);

            foreach (var info in infoBoxes)
            {
                NaughtyEditorGUI.HelpBox_Layout(ResolveText(info.Text), ToMessageType(info.Type));
            }

            var label = field.GetCustomAttribute<LabelAttribute>(true)?.Label ?? ObjectNames.NicifyVariableName(field.Name);

            if (field.GetCustomAttribute<HideLabelAttribute>(true) != null)
            {
                label = string.Empty;
            }

            var owner = field.IsStatic ? null : (object)target;
            var value = field.GetValue(owner);
            var readOnly = field.IsInitOnly || field.IsLiteral
                || field.GetCustomAttribute<ReadOnlyAttribute>(true) != null
                || !ButtonUtility.IsEnabled(target, field);

            if (value == null && !readOnly)
            {
                if (typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType))
                {
                    var picked = EditorGUILayout.ObjectField(label, null, field.FieldType, true);

                    if (picked != null)
                    {
                        Undo.RecordObject(target, "Change " + field.Name);
                        field.SetValue(owner, picked);
                        EditorUtility.SetDirty(target);
                        InvokeValueChanged(field);
                    }

                    return;
                }

                if (field.FieldType == typeof(string))
                {
                    value = string.Empty;
                }
            }

            if (value == null)
            {
                EditorGUILayout.LabelField(label, "null");

                return;
            }

            if (value is System.Collections.IList tableValue && field.GetCustomAttribute<TableListAttribute>(true) is { } tableAttribute)
            {
                NaughtyTableDrawer.Draw(tableValue, target, label, readOnly || tableAttribute.IsReadOnly);

                return;
            }

            if (value is System.Collections.IList nonSerializedList)
            {
                DrawNonSerializedList(nonSerializedList, label, field.Name);

                return;
            }

            if (readOnly)
            {
                NaughtyEditorGUI.Field_Layout(value, label);

                return;
            }

            if (!NaughtyEditorGUI.EditableField_Layout(value, label, out var newValue) || Equals(value, newValue))
            {
                return;
            }

            Undo.RecordObject(target, "Change " + field.Name);
            field.SetValue(owner, newValue);
            EditorUtility.SetDirty(target);

            InvokeValueChanged(field);
            serializedObject.Update();
        }

        private void InvokeValueChanged(FieldInfo field)
        {
            foreach (var changed in field.GetCustomAttributes<OnValueChangedAttribute>(true))
            {
                var callback = target.GetType().GetMethod(
                    changed.CallbackName,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                callback?.Invoke(target, null);
            }
        }

        private readonly Dictionary<string, bool> _nonSerializedListFoldouts = new Dictionary<string, bool>();

        private void DrawNonSerializedList(System.Collections.IList list, string label, string key)
        {
            _nonSerializedListFoldouts.TryGetValue(key, out var open);

            var headerRect = EditorGUILayout.GetControlRect(false, 22f);

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(headerRect, EditorGUIUtility.isProSkin ? new Color(0.26f, 0.26f, 0.26f) : new Color(0.72f, 0.72f, 0.72f));
            }

            open = EditorGUI.Foldout(new Rect(headerRect.x + 14f, headerRect.y + 3f, headerRect.width - 100f, 16f), open, label, true);
            _nonSerializedListFoldouts[key] = open;

            var countStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, fontSize = 12 };

            if (list.Count == 0)
            {
                countStyle.normal.textColor = Color.gray;
            }

            EditorGUI.LabelField(new Rect(headerRect.xMax - 86f, headerRect.y, 80f, headerRect.height),
                list.Count == 0 ? "Empty" : list.Count == 1 ? "1 Element" : list.Count + " Elements", countStyle);

            if (!open)
            {
                return;
            }

            EditorGUI.indentLevel++;

            for (var i = 0; i < list.Count; i++)
            {
                var element = list[i];

                if (element == null)
                {
                    continue;
                }

                var elementType = element.GetType();

                if (elementType.IsPrimitive || elementType == typeof(string) || elementType.IsValueType || element is UnityEngine.Object)
                {
                    NaughtyEditorGUI.Field_Layout(element, $"Element {i}");

                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                foreach (var f in elementType.GetFields(flags))
                {
                    var visible = f.IsPublic
                        ? f.GetCustomAttribute<NonSerializedAttribute>() == null && f.GetCustomAttribute<HideInInspector>() == null
                        : f.GetCustomAttribute<SerializeField>() != null;

                    var fieldValue = f.GetValue(element);

                    if (!visible || fieldValue == null)
                    {
                        continue;
                    }

                    if (NaughtyEditorGUI.EditableField_Layout(fieldValue, ObjectNames.NicifyVariableName(f.Name), out var newValue) && !Equals(fieldValue, newValue))
                    {
                        f.SetValue(element, newValue);
                        EditorUtility.SetDirty(target);
                    }
                }

                foreach (var method in elementType.GetMethods(flags))
                {
                    var button = method.GetCustomAttribute<ButtonAttribute>(true);

                    if (button == null || !method.GetParameters().All(p => p.IsOptional))
                    {
                        continue;
                    }

                    var text = string.IsNullOrEmpty(button.Text) ? ObjectNames.NicifyVariableName(method.Name) : button.Text;

                    if (GUILayout.Button(text))
                    {
                        method.Invoke(element, method.GetParameters().Select(p => p.DefaultValue).ToArray());
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUI.indentLevel--;
        }

        private void DrawButton(MethodInfo method)
        {
            foreach (var info in method.GetCustomAttributes<InfoBoxAttribute>(true))
            {
                NaughtyEditorGUI.HelpBox_Layout(ResolveText(info.Text), ToMessageType(info.Type));
            }

            NaughtyEditorGUI.Button(serializedObject.targetObject, method);
        }

        private string ResolveText(string text)
        {
            if (string.IsNullOrEmpty(text) || text[0] != '$')
            {
                return text;
            }

            var name = text.Substring(1);
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            var type = target.GetType();
            var method = type.GetMethod(name, flags, null, Type.EmptyTypes, null);

            if (method != null)
            {
                return method.Invoke(target, null)?.ToString() ?? string.Empty;
            }

            var property = type.GetProperty(name, flags);

            if (property != null)
            {
                return property.GetValue(target)?.ToString() ?? string.Empty;
            }

            var field = type.GetField(name, flags);

            return field != null ? field.GetValue(target)?.ToString() ?? string.Empty : text;
        }

        private static MessageType ToMessageType(EInfoBoxType type)
        {
            switch (type)
            {
                case EInfoBoxType.Warning: return MessageType.Warning;
                case EInfoBoxType.Error: return MessageType.Error;
                default: return MessageType.Info;
            }
        }

    }
}
