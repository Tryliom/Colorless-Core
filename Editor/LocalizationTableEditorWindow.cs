using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization.Tables;

namespace ColorlessOdyssey
{
	/// <summary>
	/// Alternative editor for string tables: word-wrapped cells that always grow with their content,
	/// key add/remove, search, sorting and a full window (no wrap) view of a single text.
	/// </summary>
	public class LocalizationTableEditorWindow : EditorWindow
	{
		private enum SortMode
		{
			TableOrder,
			Ascending,
			Descending
		}

		private sealed class Row
		{
			public long Id;
			public string Key;
			public float Height = -1f;
			public float Y;
		}

		private const float KeyColumnWidth = 220f;
		private const float MinCellWidth = 220f;
		private const float CellHeaderHeight = 18f;
		private const float MinRowHeight = 70f;
		private const float Padding = 4f;
		private const float ScrollbarWidth = 15f;
		private const float HeaderButtonWidth = 22f;
		private const float DeleteColumnWidth = 28f;
		private const float ColumnHeaderHeight = 32f;
		private const string SearchControlName = "SearchField";
		private static readonly Color SeparatorColor = new(0.1f, 0.1f, 0.1f, 1f);
		private static readonly Color HeaderBackgroundColor = new(0.22f, 0.22f, 0.22f, 1f);
		private const string PageSizePrefKey = "ColorlessOdyssey.LocalizationTableEditor.PageSize";
		private const string FullscreenWrapPrefKey = "ColorlessOdyssey.LocalizationTableEditor.FullscreenWordWrap";
		private const int DefaultPageSize = 25;

		private static readonly string[] SortLabels = { "Table order", "A → Z", "Z → A" };

		private readonly List<Row> _rows = new();
		private readonly List<Row> _visible = new();
		private int _page;
		private int _pageSize = DefaultPageSize;
		private bool _fullscreenWordWrap = true;
		private readonly GUIContent _measureContent = new();
		private readonly List<string> _collectionNames = new();
		private readonly List<string> _tableLabels = new();

		private List<StringTableCollection> _collections = new();
		private List<StringTable> _tables = new();
		private StringTableCollection _collection;
		private int _collectionIndex;

		private string _search = string.Empty;
		private string _newKey = string.Empty;
		private SortMode _sortMode = SortMode.Ascending;

		private Vector2 _scroll;
		private float _layoutWidth;
		private int _firstVisibleRow = -1;
		private long _pendingScrollKeyId = -1;

		private bool _fullscreen;
		private long _fullscreenKeyId;
		private int _fullscreenTableIndex;
		private Vector2 _fullscreenScroll;

		private GUIStyle _wrapStyle;
		private GUIStyle _noWrapStyle;
		private GUIStyle _headerStyle;
		private GUIStyle _toolbarLabelStyle;
		private GUIStyle _pageFieldStyle;
		private GUIStyle _keyStyle;
		private GUIContent _fullscreenOnContent;
		private GUIContent _fullscreenOffContent;
		private GUIContent _deleteContent;

		[MenuItem("Tools/Colorless Core/Localization Table Editor", priority = 1)]
		public static void Open()
		{
			GetWindow<LocalizationTableEditorWindow>("Localization Table Editor");
		}

		private void OnEnable()
		{
			minSize = new Vector2(500f, 300f);
			_pageSize = Mathf.Max(0, EditorPrefs.GetInt(PageSizePrefKey, DefaultPageSize));
			_fullscreenWordWrap = EditorPrefs.GetBool(FullscreenWrapPrefKey, true);
			Undo.undoRedoPerformed += OnUndoRedo;
			RefreshCollections();
		}

		private void OnDisable()
		{
			Undo.undoRedoPerformed -= OnUndoRedo;
			SaveAssets();
		}

		private void OnLostFocus()
		{
			SaveAssets();
		}

		private void OnFocus()
		{
			RefreshCollections();
		}

		private void OnUndoRedo()
		{
			RebuildRows();
			Repaint();
		}

		private void OnGUI()
		{
			EnsureStyles();

			if (_collection == null)
			{
				RefreshCollections();
			}

			if (_fullscreen && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
			{
				Event.current.Use();
				ExitFullscreen();
			}

			if (_collections.Count == 0 || _collection == null)
			{
				EditorGUILayout.HelpBox("No string table collection found.", MessageType.Info);

				return;
			}

			if (_fullscreen)
			{
				DrawFullscreen();

				return;
			}

			HandlePageArrows();
			DrawToolbar();
			DrawAddKeyBar();
			DrawTable();
		}

		private void HandlePageArrows()
		{
			var evt = Event.current;

			// Skip when a text control is being edited so the arrows keep moving the caret.
			if (evt.type != EventType.KeyDown || EditorGUIUtility.editingTextField)
			{
				return;
			}

			if (evt.keyCode == KeyCode.LeftArrow && _page > 0)
			{
				SetPage(_page - 1);
				evt.Use();
			}
			else if (evt.keyCode == KeyCode.RightArrow && _page < PageCount() - 1)
			{
				SetPage(_page + 1);
				evt.Use();
			}
		}

		private void EnsureStyles()
		{
			if (_wrapStyle != null && _headerStyle != null && _toolbarLabelStyle != null && _pageFieldStyle != null && _keyStyle != null && _headerStyle.fontSize == 16)
			{
				return;
			}

			_wrapStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
			_noWrapStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = false };
			_headerStyle = new GUIStyle
			{
				alignment = TextAnchor.MiddleCenter,
				fontSize = 16,
				fontStyle = FontStyle.Bold,
				clipping = TextClipping.Clip,
				wordWrap = false,
				margin = new RectOffset(0, 0, 0, 0),
				padding = new RectOffset(0, 0, 0, 0),
				normal = { textColor = Color.white }
			};
			_toolbarLabelStyle = new GUIStyle(EditorStyles.miniLabel)
			{
				alignment = TextAnchor.MiddleCenter,
				stretchHeight = true,
				fixedHeight = 0f,
				normal = { textColor = Color.white }
			};
			_pageFieldStyle = EditorStyles.toolbarTextField;
			_keyStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };

			_fullscreenOnContent = new GUIContent("□", "Show full window");
			_fullscreenOffContent = new GUIContent("□", "Back to the table (Escape)");
			_deleteContent = new GUIContent("✕", "Delete this key from all languages");
		}

		#region Data

		private void RefreshCollections()
		{
			var previous = _collection;

			_collections.Clear();
			_collectionNames.Clear();

			foreach (var collection in LocalizationEditorSettings.GetStringTableCollections())
			{
				_collections.Add(collection);
				_collectionNames.Add(collection.TableCollectionName);
			}

			_collectionIndex = previous != null ? Mathf.Max(0, _collections.IndexOf(previous)) : Mathf.Clamp(_collectionIndex, 0, Mathf.Max(0, _collections.Count - 1));
			SelectCollection(_collections.Count > 0 ? _collections[_collectionIndex] : null);
		}

		private void SelectCollection(StringTableCollection collection)
		{
			var changed = collection != _collection;

			_collection = collection;
			_tables = new List<StringTable>();
			_tableLabels.Clear();

			if (collection != null)
			{
				foreach (var table in collection.StringTables)
				{
					if (table == null)
					{
						continue;
					}

					var locale = LocalizationEditorSettings.GetLocale(table.LocaleIdentifier);

					_tables.Add(table);
					_tableLabels.Add(locale != null ? locale.ToString() : table.LocaleIdentifier.Code);
				}
			}

			if (changed)
			{
				_scroll = Vector2.zero;
				_fullscreen = false;
				GUI.FocusControl(null);
			}

			RebuildRows();
		}

		private void RebuildRows()
		{
			_rows.Clear();
			_layoutWidth = 0f;

			if (_collection == null || _collection.SharedData == null)
			{
				RebuildVisible();

				return;
			}

			var hasSearch = !string.IsNullOrWhiteSpace(_search);

			foreach (var entry in _collection.SharedData.Entries)
			{
				if (hasSearch && !Matches(entry))
				{
					continue;
				}

				_rows.Add(new Row { Id = entry.Id, Key = entry.Key ?? string.Empty });
			}

			if (_sortMode == SortMode.Ascending)
			{
				_rows.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
			}
			else if (_sortMode == SortMode.Descending)
			{
				_rows.Sort((a, b) => string.Compare(b.Key, a.Key, StringComparison.OrdinalIgnoreCase));
			}

			RebuildVisible();
		}

		private int PageCount()
		{
			return _pageSize <= 0 ? 1 : Mathf.Max(1, Mathf.CeilToInt(_rows.Count / (float)_pageSize));
		}

		private void RebuildVisible()
		{
			_page = Mathf.Clamp(_page, 0, PageCount() - 1);
			_visible.Clear();
			_firstVisibleRow = -1;

			var start = _pageSize <= 0 ? 0 : _page * _pageSize;
			var end = _pageSize <= 0 ? _rows.Count : Mathf.Min(_rows.Count, start + _pageSize);

			for (var i = start; i < end; i++)
			{
				_visible.Add(_rows[i]);
			}
		}

		private void SetPage(int page)
		{
			_page = page;
			_scroll = Vector2.zero;
			GUI.FocusControl(null);
			RebuildVisible();
		}

		private bool Matches(SharedTableData.SharedTableEntry entry)
		{
			if (entry.Key != null && entry.Key.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}

			foreach (var table in _tables)
			{
				var value = table.GetEntry(entry.Id)?.Value;

				if (value != null && value.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}

			return false;
		}

		private void SetValue(StringTable table, long keyId, string value)
		{
			Undo.RecordObject(table, "Edit localization text");

			var entry = table.GetEntry(keyId);

			if (entry == null)
			{
				table.AddEntry(keyId, value);
			}
			else
			{
				entry.Value = value;
			}

			EditorUtility.SetDirty(table);
		}

		private void AddKey()
		{
			var key = _newKey.Trim();

			if (key.Length == 0)
			{
				return;
			}

			var sharedData = _collection.SharedData;

			if (sharedData.Contains(key))
			{
				ShowNotification(new GUIContent($"The key \"{key}\" already exists."));

				return;
			}

			Undo.RecordObject(sharedData, "Add localization key");

			var entry = sharedData.AddKey(key);

			EditorUtility.SetDirty(sharedData);

			_newKey = string.Empty;
			_search = string.Empty;
			_pendingScrollKeyId = entry.Id;
			GUI.FocusControl(null);
			RebuildRows();

			var index = _rows.FindIndex(r => r.Id == entry.Id);

			if (index >= 0 && _pageSize > 0)
			{
				_page = index / _pageSize;
				RebuildVisible();
			}
		}

		private void DeleteKey(Row row)
		{
			if (!EditorUtility.DisplayDialog("Delete key", $"Delete \"{row.Key}\" from every language of \"{_collection.TableCollectionName}\"?", "Delete", "Cancel"))
			{
				return;
			}

			var targets = new List<UnityEngine.Object>(_tables.Count + 1) { _collection.SharedData };

			foreach (var table in _tables)
			{
				targets.Add(table);
			}

			Undo.RecordObjects(targets.ToArray(), "Delete localization key");
			_collection.RemoveEntry(row.Id);

			foreach (var target in targets)
			{
				EditorUtility.SetDirty(target);
			}

			GUI.FocusControl(null);
			RebuildRows();
		}

		private void SaveAssets()
		{
			if (_collection == null)
			{
				return;
			}

			AssetDatabase.SaveAssetIfDirty(_collection.SharedData);

			foreach (var table in _tables)
			{
				if (table != null)
				{
					AssetDatabase.SaveAssetIfDirty(table);
				}
			}
		}

		#endregion

		#region Normal view

		private void DrawToolbar()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				EditorGUI.BeginChangeCheck();

				var index = EditorGUILayout.Popup(_collectionIndex, _collectionNames.ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(180f));

				if (EditorGUI.EndChangeCheck())
				{
					SaveAssets();
					_collectionIndex = index;
					_page = 0;
					SelectCollection(_collections[index]);
				}

				EditorGUI.BeginChangeCheck();

				var sort = (SortMode)EditorGUILayout.Popup((int)_sortMode, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(100f));

				if (EditorGUI.EndChangeCheck())
				{
					_sortMode = sort;
					_page = 0;
					RebuildRows();
				}

				GUILayout.Space(8f);

				EditorGUI.BeginChangeCheck();

				GUI.SetNextControlName(SearchControlName);

				var search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField);

				if (EditorGUI.EndChangeCheck())
				{
					_search = search;
					_page = 0;
					_scroll = Vector2.zero;
					RebuildRows();
				}

				GUILayout.Label($"{_rows.Count} / {_collection.SharedData.Entries.Count}", _toolbarLabelStyle, GUILayout.Width(70f));
				GUILayout.Label("Per page", _toolbarLabelStyle, GUILayout.Width(52f));

				EditorGUI.BeginChangeCheck();

				var size = Mathf.Max(0, EditorGUILayout.DelayedIntField(_pageSize, EditorStyles.toolbarTextField, GUILayout.Width(36f)));

				if (EditorGUI.EndChangeCheck())
				{
					_pageSize = size;
					EditorPrefs.SetInt(PageSizePrefKey, size);
					SetPage(0);
				}

				var pageCount = PageCount();

				using (new EditorGUI.DisabledScope(_page <= 0))
				{
					if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(24f)))
					{
						SetPage(_page - 1);
					}
				}

				EditorGUI.BeginChangeCheck();

				var pageNumber = EditorGUILayout.DelayedIntField(_page + 1, _pageFieldStyle, GUILayout.Width(36f));

				if (EditorGUI.EndChangeCheck())
				{
					SetPage(Mathf.Clamp(pageNumber - 1, 0, pageCount - 1));
				}

				GUILayout.Label($"/ {pageCount}", _toolbarLabelStyle, GUILayout.Width(32f));

				using (new EditorGUI.DisabledScope(_page >= pageCount - 1))
				{
					if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(24f)))
					{
						SetPage(_page + 1);
					}
				}
			}
		}

		private void DrawAddKeyBar()
		{
			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				GUILayout.Label("New key", EditorStyles.miniLabel, GUILayout.Width(55f));

				GUI.SetNextControlName("NewKeyField");
				_newKey = EditorGUILayout.TextField(_newKey, EditorStyles.toolbarTextField);

				var submit = Event.current.type == EventType.KeyDown
				             && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
				             && GUI.GetNameOfFocusedControl() == "NewKeyField";

				using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newKey)))
				{
					if (GUILayout.Button("Add", EditorStyles.toolbarButton, GUILayout.Width(50f)) || submit)
					{
						if (submit)
						{
							Event.current.Use();
						}

						AddKey();
					}
				}
			}
		}

		private void DrawTable()
		{
			var viewRect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandHeight(true));
			if (Event.current.type == EventType.Layout)
			{
				return;
			}

			var bodyHeight = viewRect.height - ColumnHeaderHeight;
			var cellWidth = CellWidthFor(viewRect.width - ScrollbarWidth);

			UpdateRowLayout(cellWidth, out var totalHeight);

			if (totalHeight <= bodyHeight)
			{
				cellWidth = CellWidthFor(viewRect.width);
				UpdateRowLayout(cellWidth, out totalHeight);
			}

			var contentWidth = KeyColumnWidth + cellWidth * _tables.Count + DeleteColumnWidth;

			var headerRect = new Rect(viewRect.x, viewRect.y, viewRect.width, ColumnHeaderHeight);
			var bodyRect = new Rect(viewRect.x, headerRect.yMax, viewRect.width, viewRect.height - headerRect.height);

			DrawColumnHeaders(headerRect, cellWidth);

			var contentRect = new Rect(0f, 0f, contentWidth, totalHeight);

			_scroll = GUI.BeginScrollView(bodyRect, _scroll, contentRect);

			var first = FindFirstVisibleRow(_scroll.y);

			if (first != _firstVisibleRow)
			{
				_firstVisibleRow = first;
				if (GUI.GetNameOfFocusedControl() != SearchControlName)
				{
					GUI.FocusControl(null);
				}
			}

			var bottom = _scroll.y + bodyRect.height;

			for (var i = first; i < _visible.Count; i++)
			{
				var row = _visible[i];

				if (row.Y > bottom)
				{
					break;
				}

				DrawRow(row, i, cellWidth);
			}

			GUI.EndScrollView();
		}

		private float CellWidthFor(float availableWidth)
		{
			return Mathf.Max(MinCellWidth, (availableWidth - KeyColumnWidth - DeleteColumnWidth) / Mathf.Max(1, _tables.Count));
		}

		private void UpdateRowLayout(float cellWidth, out float totalHeight)
		{
			var widthChanged = !Mathf.Approximately(cellWidth, _layoutWidth);

			_layoutWidth = cellWidth;

			var y = 0f;

			foreach (var row in _visible)
			{
				if (widthChanged || row.Height < 0f)
				{
					row.Height = MeasureRow(row, cellWidth);
				}

				row.Y = y;
				y += row.Height;
			}

			totalHeight = y;

			if (_pendingScrollKeyId >= 0)
			{
				foreach (var row in _visible)
				{
					if (row.Id == _pendingScrollKeyId)
					{
						_scroll.y = row.Y;

						break;
					}
				}

				_pendingScrollKeyId = -1;
			}
		}

		private float MeasureRow(Row row, float cellWidth)
		{
			var maxHeight = 0f;
			var textWidth = cellWidth - Padding * 3f - HeaderButtonWidth;

			for (var i = 0; i < _tables.Count; i++)
			{
				_measureContent.text = _tables[i].GetEntry(row.Id)?.Value ?? string.Empty;
				maxHeight = Mathf.Max(maxHeight, _wrapStyle.CalcHeight(_measureContent, textWidth));
			}

			return Mathf.Max(MinRowHeight, maxHeight + Padding * 2f);
		}

		private int FindFirstVisibleRow(float scrollY)
		{
			var low = 0;
			var high = _visible.Count - 1;
			var result = 0;

			while (low <= high)
			{
				var mid = (low + high) / 2;

				if (_visible[mid].Y + _visible[mid].Height > scrollY)
				{
					result = mid;
					high = mid - 1;
				}
				else
				{
					low = mid + 1;
				}
			}

			return result;
		}

		private void DrawColumnHeaders(Rect rect, float cellWidth)
		{
			GUI.BeginGroup(rect);
			if (Event.current.type == EventType.Repaint)
			{
				// The toolbar style has a fixed height smaller than the header, so fill the full header manually.
				EditorGUI.DrawRect(new Rect(0f, 0f, rect.width, rect.height), HeaderBackgroundColor);
			}

			GUI.Label(new Rect(-_scroll.x, 0f, KeyColumnWidth, rect.height), "Key", _headerStyle);

			for (var i = 0; i < _tables.Count; i++)
			{
				GUI.Label(new Rect(-_scroll.x + KeyColumnWidth + cellWidth * i, 0f, cellWidth, rect.height), _tableLabels[i], _headerStyle);
			}

			if (Event.current.type == EventType.Repaint)
			{
				for (var i = 0; i <= _tables.Count; i++)
				{
					EditorGUI.DrawRect(new Rect(-_scroll.x + KeyColumnWidth + cellWidth * i - 1f, 0f, 1f, rect.height), SeparatorColor);
				}
			}

			GUI.EndGroup();
		}

		private void DrawRow(Row row, int index, float cellWidth)
		{
			var rowRect = new Rect(0f, row.Y, KeyColumnWidth + cellWidth * _tables.Count + DeleteColumnWidth, row.Height);

			if (Event.current.type == EventType.Repaint)
			{
				if (index % 2 == 1)
				{
					EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 1f, 0.05f));
				}

				EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.yMax - 1f, rowRect.width, 1f), new Color(0f, 0f, 0f, 0.35f));

				for (var i = 0; i <= _tables.Count; i++)
				{
					EditorGUI.DrawRect(new Rect(KeyColumnWidth + cellWidth * i - 1f, row.Y, 1f, row.Height), SeparatorColor);
				}
			}

			var keyBody = new Rect(Padding, row.Y + Padding, KeyColumnWidth - Padding * 2f, row.Height - Padding * 2f);

			EditorGUI.SelectableLabel(keyBody, row.Key, _keyStyle);

			for (var i = 0; i < _tables.Count; i++)
			{
				DrawCell(row, i, new Rect(KeyColumnWidth + cellWidth * i, row.Y, cellWidth, row.Height));
			}

			var deleteRect = new Rect(rowRect.xMax - DeleteColumnWidth + Padding, row.Y + (row.Height - CellHeaderHeight) * 0.5f, HeaderButtonWidth, CellHeaderHeight);

			if (GUI.Button(deleteRect, _deleteContent, EditorStyles.miniButton))
			{
				DeleteKey(row);
				GUIUtility.ExitGUI();
			}
		}

		private void DrawCell(Row row, int tableIndex, Rect cellRect)
		{
			var table = _tables[tableIndex];
			var buttonRect = new Rect(cellRect.xMax - Padding - HeaderButtonWidth, cellRect.y + (cellRect.height - CellHeaderHeight) * 0.5f, HeaderButtonWidth, CellHeaderHeight);

			if (GUI.Button(buttonRect, _fullscreenOnContent, EditorStyles.miniButton))
			{
				EnterFullscreen(row.Id, tableIndex);
				GUIUtility.ExitGUI();
			}

			var body = new Rect(cellRect.x + Padding, cellRect.y + Padding, cellRect.width - Padding * 3f - HeaderButtonWidth, cellRect.height - Padding * 2f);
			var current = table.GetEntry(row.Id)?.Value ?? string.Empty;

			EditorGUI.BeginChangeCheck();

			var edited = EditorGUI.TextArea(body, current, _wrapStyle);

			if (EditorGUI.EndChangeCheck())
			{
				SetValue(table, row.Id, edited);
				row.Height = -1f;
				Repaint();
			}
		}

		#endregion

		#region Fullscreen view

		private void EnterFullscreen(long keyId, int tableIndex)
		{
			_fullscreen = true;
			_fullscreenKeyId = keyId;
			_fullscreenTableIndex = tableIndex;
			_fullscreenScroll = Vector2.zero;
			GUI.FocusControl(null);
		}

		private void ExitFullscreen()
		{
			_fullscreen = false;
			_firstVisibleRow = -1;
			GUI.FocusControl(null);

			foreach (var row in _rows)
			{
				row.Height = -1f;
			}

			Repaint();
		}

		private void DrawFullscreen()
		{
			if (_fullscreenTableIndex >= _tables.Count)
			{
				ExitFullscreen();

				return;
			}

			var table = _tables[_fullscreenTableIndex];
			var key = _collection.SharedData.GetEntry(_fullscreenKeyId)?.Key ?? string.Empty;

			using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
			{
				GUILayout.Label($"{key}  —  {_tableLabels[_fullscreenTableIndex]}", EditorStyles.boldLabel);
				GUILayout.FlexibleSpace();

				var wrap = GUILayout.Toggle(_fullscreenWordWrap, "Word wrap", EditorStyles.toolbarButton, GUILayout.Width(80f));

				if (wrap != _fullscreenWordWrap)
				{
					_fullscreenWordWrap = wrap;
					EditorPrefs.SetBool(FullscreenWrapPrefKey, wrap);
				}

				if (GUILayout.Button(_fullscreenOffContent, EditorStyles.toolbarButton, GUILayout.Width(HeaderButtonWidth + 8f)))
				{
					ExitFullscreen();
					GUIUtility.ExitGUI();
				}
			}

			var viewRect = GUILayoutUtility.GetRect(0f, 100000f, 0f, 100000f, GUILayout.ExpandHeight(true));
			var current = table.GetEntry(_fullscreenKeyId)?.Value ?? string.Empty;

			_measureContent.text = current;

			var style = _fullscreenWordWrap ? _wrapStyle : _noWrapStyle;
			Rect contentRect;

			if (_fullscreenWordWrap)
			{
				var wrapWidth = viewRect.width - ScrollbarWidth;
				var textHeight = _wrapStyle.CalcHeight(_measureContent, wrapWidth);
				contentRect = new Rect(0f, 0f, wrapWidth, Mathf.Max(viewRect.height, textHeight + 20f));
			}
			else
			{
				var size = _noWrapStyle.CalcSize(_measureContent);
				contentRect = new Rect(0f, 0f, Mathf.Max(viewRect.width - ScrollbarWidth, size.x + 20f), Mathf.Max(viewRect.height - ScrollbarWidth, size.y + 20f));
			}

			_fullscreenScroll = GUI.BeginScrollView(viewRect, _fullscreenScroll, contentRect);

			EditorGUI.BeginChangeCheck();

			GUI.SetNextControlName("FullscreenText");

			var edited = EditorGUI.TextArea(contentRect, current, style);

			if (EditorGUI.EndChangeCheck())
			{
				SetValue(table, _fullscreenKeyId, edited);
			}

			GUI.EndScrollView();
		}

		#endregion
	}
}
