using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window that exports all player-facing text to CSV and reimports
/// changes. Covers PlaceableObject descriptions, ScriptableObject definitions,
/// and hardcoded UI strings.
///
/// CSV format: Type,Source,Field,Key,Text
///   Type   = category (Item, Book, Drink, Hint, etc.)
///   Source = asset path or scene object path
///   Field  = serialized field name
///   Key    = unique identifier for reimport matching
///   Text   = the player-facing string
/// </summary>
public class TextExporter : EditorWindow
{
    private string _lastExportPath;
    private string _lastImportPath;
    private Vector2 _scroll;
    private string _statusMessage;
    private int _exportCount;
    private int _importCount;

    [MenuItem("Tools/Iris/Text Export-Import")]
    public static void ShowWindow()
    {
        GetWindow<TextExporter>("Text Export/Import");
    }

    private void OnGUI()
    {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.LabelField("Player-Facing Text Export/Import", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "Export: scans all scene objects and assets for player-facing text.\n" +
            "Import: reads a CSV and updates matching fields. Back up first!",
            MessageType.Info);

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Export All Text to CSV", GUILayout.Height(30)))
        {
            string path = EditorUtility.SaveFilePanel("Export Text CSV",
                Application.dataPath, "iris_text_export", "csv");
            if (!string.IsNullOrEmpty(path))
            {
                _lastExportPath = path;
                _exportCount = ExportCSV(path);
                _statusMessage = $"Exported {_exportCount} entries to {Path.GetFileName(path)}";
            }
        }

        EditorGUILayout.Space(4);

        if (GUILayout.Button("Import CSV and Apply Changes", GUILayout.Height(30)))
        {
            string path = EditorUtility.OpenFilePanel("Import Text CSV",
                Application.dataPath, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                _lastImportPath = path;
                _importCount = ImportCSV(path);
                _statusMessage = $"Updated {_importCount} entries from {Path.GetFileName(path)}";
            }
        }

        if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    // ═══════════════════════════════════════════════════════════
    // Export
    // ═══════════════════════════════════════════════════════════

    private int ExportCSV(string path)
    {
        var entries = new List<TextEntry>();

        // 1. Scene PlaceableObjects
        foreach (var po in Object.FindObjectsByType<PlaceableObject>(FindObjectsSortMode.None))
        {
            string desc = po.ItemDescription;
            if (string.IsNullOrEmpty(desc) || desc == po.gameObject.name) continue;
            string objPath = GetScenePath(po.transform);
            entries.Add(new TextEntry("Item", objPath, "_itemDescription",
                $"item:{objPath}", desc));
        }

        // 2. ScriptableObject assets
        ScanAssets<BookDefinition>(entries, "Book", new[] {
            ("title", "title"), ("author", "author")
        });
        ScanArrayAssets<BookDefinition>(entries, "BookPage", "pageTexts");

        ScanAssets<DrinkIngredientDefinition>(entries, "DrinkIngredient", new[] {
            ("ingredientName", "ingredientName")
        });
        ScanAssets<DrinkRecipeDefinition>(entries, "DrinkRecipe", new[] {
            ("recipeName", "recipeName")
        });
        ScanAssets<GlassDefinition>(entries, "Glass", new[] {
            ("glassName", "glassName")
        });
        ScanAssets<DrinkGarnishDefinition>(entries, "Garnish", new[] {
            ("garnishName", "garnishName")
        });
        ScanAssets<DatePersonalDefinition>(entries, "DateAd", new[] {
            ("characterName", "characterName"), ("adText", "adText")
        });
        ScanAssets<CommercialAdDefinition>(entries, "CommercialAd", new[] {
            ("businessName", "businessName"), ("adText", "adText")
        });
        ScanAssets<RecordDefinition>(entries, "Record", new[] {
            ("trackName", "trackName"), ("artistName", "artistName")
        });
        ScanAssets<PlantDefinition>(entries, "Plant", new[] {
            ("plantName", "plantName")
        });
        ScanAssets<SpillDefinition>(entries, "Spill", new[] {
            ("displayName", "displayName")
        });
        ScanAssets<HintDefinition>(entries, "Hint", new[] {
            ("hintContent", "hintContent"), ("hintPrompt", "hintPrompt")
        });
        ScanAssets<MessBlueprint>(entries, "Mess", new[] {
            ("messName", "messName"), ("description", "description")
        });
        ScanAssets<InfoBitDefinition>(entries, "InfoBit", new[] {
            ("title", "title"), ("description", "description")
        });
        ScanAssets<OutfitDefinition>(entries, "Outfit", new[] {
            ("outfitName", "outfitName")
        });
        ScanAssets<PerfumeDefinition>(entries, "Perfume", new[] {
            ("perfumeName", "perfumeName")
        });

        // 3. Hardcoded dialogue and reaction lines
        // Date reactions
        AddHardcoded(entries, "DateReaction", "DateReactionUI.cs", "s_likeTexts",
            new[] { "Loves it!", "Really into this!", "Great taste!" });
        AddHardcoded(entries, "DateReaction", "DateReactionUI.cs", "s_neutralTexts",
            new[] { "Hmm, okay.", "Not bad.", "Sure." });
        AddHardcoded(entries, "DateReaction", "DateReactionUI.cs", "s_dislikeTexts",
            new[] { "Not a fan...", "Yikes.", "Hard pass." });

        // Phase transition dialogue
        AddHardcoded(entries, "PhaseDialogue", "DateSessionManager.cs", "s_prePhase2Lines",
            new[] { "Why don't we go to the kitchen?", "I could use a drink..." });
        AddHardcoded(entries, "PhaseDialogue", "DateSessionManager.cs", "s_postPhase2Lines",
            new[] { "Make me something good!", "What are you pouring?" });
        AddHardcoded(entries, "PhaseDialogue", "DateSessionManager.cs", "s_prePhase3Lines",
            new[] { "Let's sit down for a bit.", "Show me the living room!" });
        AddHardcoded(entries, "PhaseDialogue", "DateSessionManager.cs", "s_postPhase3Lines",
            new[] { "Nice place you've got here...", "Let me look around." });

        // Entrance judgment lines
        AddHardcoded(entries, "EntranceDialogue", "EntranceJudgmentSequence.cs", "s_noMusicLines",
            GetStaticStringArray("Assets/Scripts/Dating/EntranceJudgmentSequence.cs", "s_noMusicLines"));
        AddHardcoded(entries, "EntranceDialogue", "EntranceJudgmentSequence.cs", "s_dirtyKitchenLines",
            GetStaticStringArray("Assets/Scripts/Dating/EntranceJudgmentSequence.cs", "s_dirtyKitchenLines"));
        AddHardcoded(entries, "EntranceDialogue", "EntranceJudgmentSequence.cs", "s_dirtyLivingRoomLines",
            GetStaticStringArray("Assets/Scripts/Dating/EntranceJudgmentSequence.cs", "s_dirtyLivingRoomLines"));
        AddHardcoded(entries, "EntranceDialogue", "EntranceJudgmentSequence.cs", "s_dirtyEntranceLines",
            GetStaticStringArray("Assets/Scripts/Dating/EntranceJudgmentSequence.cs", "s_dirtyEntranceLines"));

        // Weather dialogue
        AddHardcoded(entries, "WeatherDialogue", "NemaWeatherDialogue.cs", "s_lines",
            GetStaticStringArray("Assets/Scripts/Dating/NemaWeatherDialogue.cs", "s_lines"));

        // 4. Scene ReactableTag tags (what items are tagged as)
        foreach (var tag in Object.FindObjectsByType<ReactableTag>(FindObjectsSortMode.None))
        {
            if (tag == null) continue;
            string objPath = GetScenePath(tag.transform);
            string tags = string.Join(", ", tag.Tags);
            if (!string.IsNullOrEmpty(tags))
                entries.Add(new TextEntry("ReactableTag", objPath, "tags",
                    $"tag:{objPath}", tags));
        }

        // 5. LocalizationTable entries
        foreach (var guid in AssetDatabase.FindAssets("t:LocalizationTable"))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(assetPath);
            if (table == null) continue;
            var so = new SerializedObject(table);
            var entriesProp = so.FindProperty("entries");
            if (entriesProp == null) continue;
            for (int i = 0; i < entriesProp.arraySize; i++)
            {
                var elem = entriesProp.GetArrayElementAtIndex(i);
                var keyProp = elem.FindPropertyRelative("key");
                var valProp = elem.FindPropertyRelative("value");
                if (keyProp == null || valProp == null) continue;
                entries.Add(new TextEntry("Localization", assetPath, "value",
                    $"loc:{keyProp.stringValue}", valProp.stringValue));
            }
        }

        // Write CSV
        var sb = new StringBuilder();
        sb.AppendLine("Type,Source,Field,Key,Text");
        foreach (var e in entries)
            sb.AppendLine($"{Escape(e.type)},{Escape(e.source)},{Escape(e.field)},{Escape(e.key)},{Escape(e.text)}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[TextExporter] Exported {entries.Count} text entries to {path}");
        return entries.Count;
    }

    // ═══════════════════════════════════════════════════════════
    // Import
    // ═══════════════════════════════════════════════════════════

    private int ImportCSV(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length < 2) return 0;

        // Parse CSV rows (skip header)
        var rows = new List<TextEntry>();
        for (int i = 1; i < lines.Length; i++)
        {
            var fields = ParseCSVLine(lines[i]);
            if (fields.Length < 5) continue;
            rows.Add(new TextEntry(fields[0], fields[1], fields[2], fields[3], fields[4]));
        }

        int updated = 0;

        // Apply item descriptions to scene objects
        foreach (var row in rows)
        {
            if (row.type == "Item" && row.key.StartsWith("item:"))
            {
                string objPath = row.key.Substring(5);
                var go = GameObject.Find(objPath);
                if (go == null) continue;
                var po = go.GetComponent<PlaceableObject>();
                if (po == null) continue;
                var so = new SerializedObject(po);
                var prop = so.FindProperty(row.field);
                if (prop != null && prop.propertyType == SerializedPropertyType.String
                    && prop.stringValue != row.text)
                {
                    prop.stringValue = row.text;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(po);
                    updated++;
                }
            }
            else if (row.key.StartsWith("loc:"))
            {
                // Localization table entries
                string locKey = row.key.Substring(4);
                foreach (var guid in AssetDatabase.FindAssets("t:LocalizationTable"))
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (assetPath != row.source) continue;
                    var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(assetPath);
                    if (table == null) continue;
                    var so = new SerializedObject(table);
                    var entries = so.FindProperty("entries");
                    if (entries == null) continue;
                    for (int j = 0; j < entries.arraySize; j++)
                    {
                        var elem = entries.GetArrayElementAtIndex(j);
                        var kp = elem.FindPropertyRelative("key");
                        var vp = elem.FindPropertyRelative("value");
                        if (kp != null && kp.stringValue == locKey && vp != null
                            && vp.stringValue != row.text)
                        {
                            vp.stringValue = row.text;
                            so.ApplyModifiedProperties();
                            EditorUtility.SetDirty(table);
                            updated++;
                        }
                    }
                }
            }
            else
            {
                // ScriptableObject assets
                var asset = AssetDatabase.LoadAssetAtPath<Object>(row.source);
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                var prop = so.FindProperty(row.field);
                if (prop != null && prop.propertyType == SerializedPropertyType.String
                    && prop.stringValue != row.text)
                {
                    prop.stringValue = row.text;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(asset);
                    updated++;
                }
            }
        }

        if (updated > 0)
            AssetDatabase.SaveAssets();

        Debug.Log($"[TextExporter] Imported {updated} changes from {path}");
        return updated;
    }

    // ═══════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════

    private void ScanAssets<T>(List<TextEntry> entries, string type,
        (string fieldName, string serializedName)[] fields) where T : ScriptableObject
    {
        foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null) continue;
            var so = new SerializedObject(asset);
            foreach (var (fieldName, serializedName) in fields)
            {
                var prop = so.FindProperty(serializedName);
                if (prop == null || prop.propertyType != SerializedPropertyType.String) continue;
                string val = prop.stringValue;
                if (string.IsNullOrEmpty(val)) continue;
                entries.Add(new TextEntry(type, assetPath, serializedName,
                    $"{type.ToLower()}:{asset.name}:{fieldName}", val));
            }
        }
    }

    private void ScanArrayAssets<T>(List<TextEntry> entries, string type,
        string arrayFieldName) where T : ScriptableObject
    {
        foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null) continue;
            var so = new SerializedObject(asset);
            var arr = so.FindProperty(arrayFieldName);
            if (arr == null || !arr.isArray) continue;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var elem = arr.GetArrayElementAtIndex(i);
                if (elem.propertyType != SerializedPropertyType.String) continue;
                string val = elem.stringValue;
                if (string.IsNullOrEmpty(val)) continue;
                entries.Add(new TextEntry(type, assetPath, $"{arrayFieldName}[{i}]",
                    $"{type.ToLower()}:{asset.name}:page{i}", val));
            }
        }
    }

    private static string GetScenePath(Transform t)
    {
        var parts = new List<string>();
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return "/" + string.Join("/", parts);
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private static string[] ParseCSVLine(string line)
    {
        var fields = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            if (line[i] == '"')
            {
                // Quoted field
                i++;
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        { sb.Append('"'); i += 2; }
                        else
                        { i++; break; }
                    }
                    else
                    { sb.Append(line[i]); i++; }
                }
                fields.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                int comma = line.IndexOf(',', i);
                if (comma < 0)
                { fields.Add(line.Substring(i)); i = line.Length; }
                else
                { fields.Add(line.Substring(i, comma - i)); i = comma + 1; }
            }
        }
        return fields.ToArray();
    }

    private static void AddHardcoded(List<TextEntry> entries, string type, string source,
        string field, string[] lines)
    {
        if (lines == null) return;
        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i])) continue;
            entries.Add(new TextEntry(type, source, $"{field}[{i}]",
                $"{type.ToLower()}:{source}:{field}[{i}]", lines[i]));
        }
    }

    /// <summary>
    /// Parse a static string array from a C# source file by regex.
    /// Crude but works for simple single-line array initializers.
    /// </summary>
    private static string[] GetStaticStringArray(string relPath, string fieldName)
    {
        string fullPath = System.IO.Path.Combine(Application.dataPath, "..", relPath);
        if (!File.Exists(fullPath)) return new string[0];

        string content = File.ReadAllText(fullPath);
        // Match: fieldName = { "...", "...", ... };
        var match = System.Text.RegularExpressions.Regex.Match(content,
            fieldName + @"\s*=\s*\{([^}]+)\}");
        if (!match.Success) return new string[0];

        var strMatches = System.Text.RegularExpressions.Regex.Matches(
            match.Groups[1].Value, "\"([^\"]+)\"");
        var result = new string[strMatches.Count];
        for (int i = 0; i < strMatches.Count; i++)
            result[i] = strMatches[i].Groups[1].Value;
        return result;
    }

    private struct TextEntry
    {
        public string type, source, field, key, text;
        public TextEntry(string t, string s, string f, string k, string txt)
        { type = t; source = s; field = f; key = k; text = txt; }
    }
}
