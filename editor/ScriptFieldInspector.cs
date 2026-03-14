using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

using Engine;

namespace EngineEditor
{
    internal static class ScriptFieldInspector
    {
        private enum ScriptFieldKind
        {
            Unsupported,
            Int,
            Float,
            Bool,
            String,
            Enum,
            Vector2,
            Vector3,
            Color,
            AssetReference,
        }

        private sealed class ScriptFieldDescriptor
        {
            public FieldInfo Field;
            public ScriptFieldKind Kind;
            public int ComponentCount;
            public string[] AllowedExtensions = new string[0];
        }

        private static readonly string[] TransformDuplicateFieldNames =
        {
            "position",
            "localposition",
            "worldposition",
            "rotation",
            "localrotation",
            "worldrotation",
        };

        private static readonly Dictionary<string, ScriptFieldDescriptor[]> FieldCache = new Dictionary<string, ScriptFieldDescriptor[]>();
        private static readonly Dictionary<string, string> FieldErrors = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> AssetPickerSearchText = new Dictionary<string, string>();
        private static readonly char[] AssetExtensionSeparators = { ',', ';', '|', ' ' };
        private const int AssetPickerMaxResults = 512;
        private static string _lastScriptAssemblyIdentity = string.Empty;
        private static bool _optionsRegistered = false;
        private static bool _assetContextMenuRegistered = false;
        private static bool _hideTransformDuplicateFields = true;

        public static void InvalidateCache()
        {
            FieldCache.Clear();
            FieldErrors.Clear();
            AssetPickerSearchText.Clear();
        }

        public static void RegisterEditorOptions()
        {
            if (_optionsRegistered)
                return;

            _optionsRegistered = true;
            EditorOptionsRegistry.Register("scriptinspector.fieldfilter",
                                           "Script Inspector",
                                           "Field Visibility",
                                           DrawScriptInspectorOptions,
                                           20);
        }

        public static void RegisterAssetContextMenu()
        {
            if (_assetContextMenuRegistered)
                return;

            _assetContextMenuRegistered = true;
            AssetPanelContextMenuRegistry.Register("scriptinspector.createScript",
                                                   "Create/C# Script",
                                                   _ => AssetBrowserSystem.RequestOpenCreateScriptPopup(),
                                                   20);
        }

        private static void DrawScriptInspectorOptions()
        {
            bool hideDuplicates = _hideTransformDuplicateFields;
            if (ImGui.Checkbox("Hide transform-like script fields", ref hideDuplicates))
            {
                _hideTransformDuplicateFields = hideDuplicates;
                FieldCache.Clear();
            }

            ImGui.Text("Hides fields such as position/rotation to avoid duplicate editing with Transform.");
        }

        public static void DrawScriptFields(uint entityId, string scriptTypeName, ScriptValidationSnapshot validationSnapshot)
        {
            Assembly scriptAssembly = validationSnapshot != null ? validationSnapshot.ScriptAssembly : null;
            EnsureAssemblyCacheConsistency(scriptAssembly);

            if (scriptAssembly == null)
                return;

            Type scriptType = ResolveScriptType(scriptAssembly, scriptTypeName);
            if (scriptType == null)
                return;

            ScriptFieldDescriptor[] fields = GetVisibleFields(scriptType);
            if (fields.Length == 0)
            {
                ImGui.Text("Script fields: no attributed public fields found.");
                return;
            }

            EditorUIHelpers.DrawSectionHeader("Script Fields");

            for (int i = 0; i < fields.Length; ++i)
                DrawField(entityId, fields[i]);
        }

        private static void DrawField(uint entityId, ScriptFieldDescriptor descriptor)
        {
            string fieldName = descriptor.Field.Name;
            string fieldKey = entityId.ToString(CultureInfo.InvariantCulture) + ":" + fieldName;
            string rawValue = EditorBridge.GetScriptFieldValue(entityId, fieldName) ?? string.Empty;

            bool changed = false;
            string newRawValue = rawValue;

            switch (descriptor.Kind)
            {
                case ScriptFieldKind.Int:
                    {
                        long current = ParseInt64(rawValue, 0L);
                        string updatedText = current.ToString(CultureInfo.InvariantCulture);
                        if (EditorUIHelpers.InputTextWithWidth(fieldName, ref updatedText, EditorUIHelpers.InspectorFieldWidth))
                        {
                            if (long.TryParse(updatedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                            {
                                newRawValue = parsed.ToString(CultureInfo.InvariantCulture);
                                changed = true;
                            }
                            else
                            {
                                FieldErrors[fieldKey] = "Invalid integer value.";
                            }
                        }
                        break;
                    }
                case ScriptFieldKind.Float:
                    {
                        float current = ParseFloat(rawValue, 0.0f);
                        float updated = current;
                        if (ImGui.InputFloat(fieldName, ref updated, 0.1f))
                        {
                            newRawValue = updated.ToString(CultureInfo.InvariantCulture);
                            changed = true;
                        }
                        break;
                    }
                case ScriptFieldKind.Bool:
                    {
                        bool current = ParseBool(rawValue, false);
                        bool updated = current;
                        if (InspectorInputs.Bool(fieldName, ref updated))
                        {
                            newRawValue = updated ? "true" : "false";
                            changed = true;
                        }
                        break;
                    }
                case ScriptFieldKind.String:
                    {
                        string updated = rawValue;
                        if (EditorUIHelpers.InputTextWithWidth(fieldName, ref updated, EditorUIHelpers.InspectorFieldWidth))
                        {
                            newRawValue = updated;
                            changed = true;
                        }
                        break;
                    }
                case ScriptFieldKind.AssetReference:
                    {
                        if (DrawAssetReferenceField(entityId, descriptor, rawValue, out string assetRawValue))
                        {
                            newRawValue = assetRawValue;
                            changed = true;
                        }
                        break;
                    }
                case ScriptFieldKind.Enum:
                    {
                        if (DrawEnumField(entityId, descriptor.Field, rawValue, out string enumRawValue))
                        {
                            newRawValue = enumRawValue;
                            changed = true;
                        }
                        break;
                    }
                case ScriptFieldKind.Vector2:
                    {
                        float[] values = ParseComponents(rawValue, 2);
                        float x = values[0];
                        float y = values[1];
                        if (InspectorInputs.Vector2(fieldName, ref x, ref y, 0.1f))
                        {
                            newRawValue = x.ToString(CultureInfo.InvariantCulture) + "," +
                                          y.ToString(CultureInfo.InvariantCulture);
                            changed = true;
                        }
                        break;
                    }
                case ScriptFieldKind.Vector3:
                    {
                        float[] values = ParseComponents(rawValue, 3);
                        float x = values[0];
                        float y = values[1];
                        float z = values[2];
                        if (InspectorInputs.Vector3(fieldName, ref x, ref y, ref z, 0.1f))
                        {
                            newRawValue = x.ToString(CultureInfo.InvariantCulture) + "," +
                                          y.ToString(CultureInfo.InvariantCulture) + "," +
                                          z.ToString(CultureInfo.InvariantCulture);
                            changed = true;
                        }
                        break;
                    }
                case ScriptFieldKind.Color:
                    {
                        float[] values = ParseComponents(rawValue, descriptor.ComponentCount);
                        bool hasAlpha = descriptor.ComponentCount >= 4;
                        float r = values[0];
                        float g = values[1];
                        float b = values[2];
                        float a = hasAlpha ? values[3] : 1.0f;

                        if (InspectorInputs.ColorNormalized(fieldName, ref r, ref g, ref b, ref a, hasAlpha))
                        {
                            if (hasAlpha)
                            {
                                newRawValue = r.ToString(CultureInfo.InvariantCulture) + "," +
                                              g.ToString(CultureInfo.InvariantCulture) + "," +
                                              b.ToString(CultureInfo.InvariantCulture) + "," +
                                              a.ToString(CultureInfo.InvariantCulture);
                            }
                            else
                            {
                                newRawValue = r.ToString(CultureInfo.InvariantCulture) + "," +
                                              g.ToString(CultureInfo.InvariantCulture) + "," +
                                              b.ToString(CultureInfo.InvariantCulture);
                            }

                            changed = true;
                        }
                        break;
                    }
                default:
                    ImGui.Text(fieldName + " (unsupported type)");
                    break;
            }

            if (changed)
            {
                if (EditorBridge.SetScriptFieldValue(entityId, fieldName, newRawValue))
                    FieldErrors.Remove(fieldKey);
                else
                    FieldErrors[fieldKey] = "Failed to apply value (type mismatch or unavailable runtime field).";
            }

            if (FieldErrors.TryGetValue(fieldKey, out string error) && !string.IsNullOrEmpty(error))
                ImGui.Text("Field error (" + fieldName + "): " + error);
        }

        private static bool DrawEnumField(uint entityId, FieldInfo field, string rawValue, out string enumRawValue)
        {
            enumRawValue = rawValue;

            Type enumType = field.FieldType;
            object selectedValue;

            if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numericValue))
            {
                selectedValue = Enum.ToObject(enumType, numericValue);
            }
            else
            {
                try
                {
                    selectedValue = Enum.Parse(enumType, rawValue, true);
                }
                catch
                {
                    selectedValue = Enum.GetValues(enumType).GetValue(0);
                }
            }

            string selectedName = selectedValue != null ? selectedValue.ToString() : "<none>";
            string popupId = "EnumSelector##" + entityId.ToString(CultureInfo.InvariantCulture) + "_" + field.Name;

            ImGui.Text(field.Name + ": " + selectedName);
            ImGui.SameLine();
            if (ImGui.Button("Select##" + popupId))
                ImGui.OpenPopup(popupId);

            if (!ImGui.BeginPopupModal(popupId))
                return false;

            EditorUIHelpers.DrawPopupHeader("Select " + field.Name);

            bool changed = false;
            string[] enumNames = Enum.GetNames(enumType);
            for (int i = 0; i < enumNames.Length; ++i)
            {
                string enumName = enumNames[i];
                bool isSelected = string.Equals(enumName, selectedName, StringComparison.Ordinal);
                if (ImGui.Selectable(enumName + "##" + popupId + "_" + i.ToString(CultureInfo.InvariantCulture), isSelected))
                {
                    try
                    {
                        object parsed = Enum.Parse(enumType, enumName, true);
                        long enumNumeric = Convert.ToInt64(parsed, CultureInfo.InvariantCulture);
                        enumRawValue = enumNumeric.ToString(CultureInfo.InvariantCulture);
                        changed = true;
                    }
                    catch
                    {
                        changed = false;
                    }

                    ImGui.CloseCurrentPopup();
                    break;
                }
            }

            if (ImGui.Button("Close##" + popupId))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
            return changed;
        }

        private static bool DrawAssetReferenceField(uint entityId,
                                                    ScriptFieldDescriptor descriptor,
                                                    string rawValue,
                                                    out string assetRawValue)
        {
            assetRawValue = rawValue ?? string.Empty;

            string fieldName = descriptor.Field.Name;
            string popupId = "AssetPicker##" + entityId.ToString(CultureInfo.InvariantCulture) + "_" + fieldName;
            string searchKey = entityId.ToString(CultureInfo.InvariantCulture) + ":" + fieldName;

            string display = string.IsNullOrWhiteSpace(assetRawValue) ? "<none>" : assetRawValue;
            ImGui.Text(fieldName + ": " + display);
            ImGui.SameLine();

            if (ImGui.Button("Select##" + popupId))
                ImGui.OpenPopup(popupId);

            ImGui.SameLine();
            if (ImGui.Button("Clear##" + popupId))
            {
                if (!string.IsNullOrEmpty(assetRawValue))
                {
                    assetRawValue = string.Empty;
                    return true;
                }
            }

            if (!ImGui.BeginPopupModal(popupId))
                return false;

            EditorUIHelpers.DrawPopupHeader("Select Asset for " + fieldName);

            string searchText;
            if (!AssetPickerSearchText.TryGetValue(searchKey, out searchText))
                searchText = string.Empty;

            if (EditorUIHelpers.InputTextWithWidth("Search##" + popupId,
                                                   ref searchText,
                                                   EditorUIHelpers.CompactPopupFieldWidth))
            {
                AssetPickerSearchText[searchKey] = searchText;
            }

            string projectPath = ProjectOperations.ActiveProjectPath;
            string assetsRoot = ResolveProjectAssetsRoot(projectPath);
            string[] options = CollectAssetCandidates(projectPath,
                                                      assetsRoot,
                                                      descriptor.AllowedExtensions,
                                                      searchText);

            if (options.Length == 0)
            {
                ImGui.Text("No matching files found.");
            }
            else
            {
                if (ImGui.BeginChild("##" + popupId + "_List", 0.0f, 260.0f, true))
                {
                    for (int i = 0; i < options.Length; ++i)
                    {
                        string candidate = options[i];
                        bool selected = string.Equals(candidate, assetRawValue, StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(candidate + "##" + popupId + "_" + i.ToString(CultureInfo.InvariantCulture), selected))
                        {
                            assetRawValue = candidate;
                            ImGui.CloseCurrentPopup();
                            ImGui.EndPopup();
                            return true;
                        }
                    }
                }

                ImGui.EndChild();
            }

            ImGui.Separator();
            if (ImGui.Button("Close##" + popupId))
                ImGui.CloseCurrentPopup();

            ImGui.EndPopup();
            return false;
        }

        private static string ResolveProjectAssetsRoot(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                return string.Empty;

            string defaultAssetsRoot = Path.Combine(projectPath, "Assets");
            string projectJsonPath = Path.Combine(projectPath, "project.json");
            if (!File.Exists(projectJsonPath))
                return defaultAssetsRoot;

            try
            {
                string json = File.ReadAllText(projectJsonPath);
                string assetsRoot = ExtractJsonString(json, "assetsRoot");
                if (!string.IsNullOrWhiteSpace(assetsRoot))
                {
                    string candidate = Path.GetFullPath(Path.Combine(projectPath, assetsRoot));
                    if (Directory.Exists(candidate))
                        return candidate;
                }
            }
            catch
            {
            }

            return defaultAssetsRoot;
        }

        private static string[] CollectAssetCandidates(string projectPath,
                                                       string assetsRoot,
                                                       string[] allowedExtensions,
                                                       string searchText)
        {
            if (string.IsNullOrWhiteSpace(projectPath) ||
                string.IsNullOrWhiteSpace(assetsRoot) ||
                !Directory.Exists(assetsRoot))
            {
                return new string[0];
            }

            string normalizedProjectPath;
            try
            {
                normalizedProjectPath = Path.GetFullPath(projectPath);
            }
            catch
            {
                normalizedProjectPath = projectPath;
            }

            string normalizedSearch = (searchText ?? string.Empty).Trim();
            var results = new List<string>();

            string[] files;
            try
            {
                files = Directory.GetFiles(assetsRoot, "*", SearchOption.AllDirectories);
            }
            catch
            {
                return new string[0];
            }

            for (int i = 0; i < files.Length; ++i)
            {
                string absolutePath = files[i];
                if (!MatchesExtensionFilter(absolutePath, allowedExtensions))
                    continue;

                string relativePath = NormalizeProjectRelativeAssetPath(normalizedProjectPath, absolutePath);
                if (string.IsNullOrWhiteSpace(relativePath))
                    continue;

                if (!string.IsNullOrEmpty(normalizedSearch) &&
                    relativePath.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                results.Add(relativePath);
                if (results.Count >= AssetPickerMaxResults)
                    break;
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results.ToArray();
        }

        private static bool MatchesExtensionFilter(string filePath, string[] allowedExtensions)
        {
            if (allowedExtensions == null || allowedExtensions.Length == 0)
                return true;

            string extension = Path.GetExtension(filePath);
            if (string.IsNullOrWhiteSpace(extension))
                return false;

            for (int i = 0; i < allowedExtensions.Length; ++i)
            {
                if (string.Equals(extension, allowedExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string NormalizeProjectRelativeAssetPath(string projectPath, string absolutePath)
        {
            try
            {
                string relativePath = StringUtilities.MakeRelativePath(projectPath, absolutePath);
                return (relativePath ?? string.Empty).Replace('\\', '/');
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            string needle = "\"" + key + "\"";
            int keyPos = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyPos < 0)
                return string.Empty;

            int colonPos = json.IndexOf(':', keyPos + needle.Length);
            if (colonPos < 0)
                return string.Empty;

            int firstQuotePos = json.IndexOf('"', colonPos + 1);
            if (firstQuotePos < 0)
                return string.Empty;

            var valueChars = new List<char>();
            bool escaped = false;

            for (int i = firstQuotePos + 1; i < json.Length; ++i)
            {
                char c = json[i];
                if (escaped)
                {
                    switch (c)
                    {
                        case 'n':
                            valueChars.Add('\n');
                            break;
                        case 'r':
                            valueChars.Add('\r');
                            break;
                        case 't':
                            valueChars.Add('\t');
                            break;
                        default:
                            valueChars.Add(c);
                            break;
                    }

                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                    return new string(valueChars.ToArray());

                valueChars.Add(c);
            }

            return string.Empty;
        }

        private static ScriptFieldDescriptor[] GetVisibleFields(Type scriptType)
        {
            string cacheKey = BuildTypeCacheKey(scriptType);
            if (FieldCache.TryGetValue(cacheKey, out ScriptFieldDescriptor[] cached))
                return cached;

            FieldInfo[] fields = scriptType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            var visible = new List<ScriptFieldDescriptor>();

            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo field = fields[i];
                if (field == null)
                    continue;

                if (!HasVisibleAttribute(field))
                    continue;

                if (_hideTransformDuplicateFields && IsTransformDuplicateFieldName(field.Name))
                    continue;

                ScriptFieldKind kind = DetermineFieldKind(field, out int componentCount, out string[] allowedExtensions);
                if (kind == ScriptFieldKind.Unsupported)
                    continue;

                visible.Add(new ScriptFieldDescriptor
                {
                    Field = field,
                    Kind = kind,
                    ComponentCount = componentCount,
                    AllowedExtensions = allowedExtensions,
                });
            }

            ScriptFieldDescriptor[] result = visible.ToArray();
            FieldCache[cacheKey] = result;
            return result;
        }

        private static void EnsureAssemblyCacheConsistency(Assembly scriptAssembly)
        {
            string currentIdentity = BuildAssemblyIdentity(scriptAssembly);
            if (string.Equals(_lastScriptAssemblyIdentity, currentIdentity, StringComparison.Ordinal))
                return;

            InvalidateCache();
            _lastScriptAssemblyIdentity = currentIdentity;
        }

        private static string BuildTypeCacheKey(Type scriptType)
        {
            if (scriptType == null)
                return string.Empty;

            string typeName = scriptType.FullName ?? scriptType.Name ?? string.Empty;
            string assemblyIdentity = BuildAssemblyIdentity(scriptType.Assembly);
            return assemblyIdentity + "|" + typeName;
        }

        private static string BuildAssemblyIdentity(Assembly assembly)
        {
            if (assembly == null)
                return string.Empty;

            string fullName = assembly.FullName ?? string.Empty;
            string location = string.Empty;
            string moduleVersionId = string.Empty;

            try
            {
                location = assembly.Location ?? string.Empty;
            }
            catch
            {
                location = string.Empty;
            }

            try
            {
                Module manifestModule = assembly.ManifestModule;
                if (manifestModule != null)
                    moduleVersionId = manifestModule.ModuleVersionId.ToString("D", CultureInfo.InvariantCulture);
            }
            catch
            {
                moduleVersionId = string.Empty;
            }

            return fullName + "|" + location + "|" + moduleVersionId;
        }

        private static bool HasVisibleAttribute(FieldInfo field)
        {
            object[] attributes = field.GetCustomAttributes(false);
            for (int i = 0; i < attributes.Length; ++i)
            {
                object attribute = attributes[i];
                if (attribute == null)
                    continue;

                string attributeName = attribute.GetType().Name;
                if (string.Equals(attributeName, "EditorFieldAttribute", StringComparison.Ordinal) ||
                    string.Equals(attributeName, "EditorField", StringComparison.Ordinal) ||
                    string.Equals(attributeName, "SerializeFieldAttribute", StringComparison.Ordinal) ||
                    string.Equals(attributeName, "SerializeField", StringComparison.Ordinal) ||
                    string.Equals(attributeName, "AssetPickerAttribute", StringComparison.Ordinal) ||
                    string.Equals(attributeName, "AssetPicker", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTransformDuplicateFieldName(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName))
                return false;

            string normalized = NormalizeFieldName(fieldName);
            for (int i = 0; i < TransformDuplicateFieldNames.Length; ++i)
            {
                if (string.Equals(normalized, TransformDuplicateFieldNames[i], StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string NormalizeFieldName(string fieldName)
        {
            char[] source = fieldName.ToLowerInvariant().ToCharArray();
            char[] filtered = new char[source.Length];
            int writeIndex = 0;

            for (int i = 0; i < source.Length; ++i)
            {
                char c = source[i];
                if (c == '_' || c == '-' || c == ' ')
                    continue;

                filtered[writeIndex++] = c;
            }

            return new string(filtered, 0, writeIndex);
        }

        private static ScriptFieldKind DetermineFieldKind(FieldInfo field, out int componentCount, out string[] allowedExtensions)
        {
            componentCount = 0;
            allowedExtensions = new string[0];
            if (field == null)
                return ScriptFieldKind.Unsupported;

            Type fieldType = field.FieldType;
            if (fieldType == typeof(int) ||
                fieldType == typeof(uint) ||
                fieldType == typeof(short) ||
                fieldType == typeof(ushort) ||
                fieldType == typeof(byte) ||
                fieldType == typeof(sbyte) ||
                fieldType == typeof(long) ||
                fieldType == typeof(ulong))
            {
                return ScriptFieldKind.Int;
            }

            if (fieldType == typeof(float) || fieldType == typeof(double))
                return ScriptFieldKind.Float;

            if (fieldType == typeof(bool))
                return ScriptFieldKind.Bool;

            if (fieldType == typeof(string))
            {
                if (TryResolveAssetExtensions(field, fieldType, out string[] stringExtensions))
                {
                    allowedExtensions = stringExtensions;
                    return ScriptFieldKind.AssetReference;
                }

                return ScriptFieldKind.String;
            }

            if (fieldType.IsEnum)
                return ScriptFieldKind.Enum;

            if (TryResolveAssetExtensions(field, fieldType, out string[] typeExtensions))
            {
                allowedExtensions = typeExtensions;
                return ScriptFieldKind.AssetReference;
            }

            if (TryMatchColorType(fieldType, out componentCount))
                return ScriptFieldKind.Color;

            if (TryMatchVector3Type(fieldType))
            {
                componentCount = 3;
                return ScriptFieldKind.Vector3;
            }

            if (TryMatchVector2Type(fieldType))
            {
                componentCount = 2;
                return ScriptFieldKind.Vector2;
            }

            return ScriptFieldKind.Unsupported;
        }

        private static bool TryResolveAssetExtensions(FieldInfo field, Type fieldType, out string[] extensions)
        {
            extensions = new string[0];
            if (field == null || fieldType == null)
                return false;

            bool attributeDefined = TryReadAssetPickerExtensions(field, out string[] attributeExtensions);
            if (attributeDefined)
            {
                extensions = attributeExtensions;
                return true;
            }

            if (!HasPathLikeStringField(fieldType))
                return false;

            if (TryReadTypeLevelAssetExtensions(fieldType, out string[] typeExtensions))
                extensions = typeExtensions;

            return true;
        }

        private static bool TryReadAssetPickerExtensions(FieldInfo field, out string[] extensions)
        {
            extensions = new string[0];
            object[] attributes = field.GetCustomAttributes(false);

            for (int i = 0; i < attributes.Length; ++i)
            {
                object attribute = attributes[i];
                if (attribute == null)
                    continue;

                Type attributeType = attribute.GetType();
                if (!string.Equals(attributeType.Name, "AssetPickerAttribute", StringComparison.Ordinal) &&
                    !string.Equals(attributeType.Name, "AssetPicker", StringComparison.Ordinal))
                {
                    continue;
                }

                string raw = ReadStringMember(attribute, "extensions");
                if (string.IsNullOrWhiteSpace(raw))
                    raw = ReadStringMember(attribute, "Extensions");

                extensions = ParseExtensions(raw);
                return true;
            }

            return false;
        }

        private static bool TryReadTypeLevelAssetExtensions(Type fieldType, out string[] extensions)
        {
            extensions = new string[0];

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            string[] candidateNames =
            {
                "EditorFileExtensions",
                "FileExtensions",
                "Extensions",
            };

            for (int i = 0; i < candidateNames.Length; ++i)
            {
                string name = candidateNames[i];
                FieldInfo staticField = fieldType.GetField(name, flags);
                if (staticField != null && staticField.FieldType == typeof(string))
                {
                    string value = staticField.GetValue(null) as string;
                    extensions = ParseExtensions(value);
                    return true;
                }

                PropertyInfo staticProperty = fieldType.GetProperty(name, flags);
                if (staticProperty != null && staticProperty.PropertyType == typeof(string) && staticProperty.GetGetMethod(true) != null)
                {
                    string value = staticProperty.GetValue(null, null) as string;
                    extensions = ParseExtensions(value);
                    return true;
                }
            }

            return false;
        }

        private static bool HasPathLikeStringField(Type fieldType)
        {
            if (fieldType == null || fieldType == typeof(string) || fieldType.IsPrimitive || fieldType.IsEnum)
                return false;

            FieldInfo[] fields = fieldType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo candidate = fields[i];
                if (candidate.FieldType != typeof(string))
                    continue;

                string normalized = NormalizeFieldName(candidate.Name);
                if (normalized == "path" || normalized == "assetpath" || normalized == "filepath")
                    return true;
            }

            return false;
        }

        private static string ReadStringMember(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return string.Empty;

            Type targetType = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo field = targetType.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(string))
                return field.GetValue(target) as string ?? string.Empty;

            PropertyInfo property = targetType.GetProperty(memberName, flags);
            if (property != null && property.PropertyType == typeof(string) && property.GetGetMethod(true) != null)
                return property.GetValue(target, null) as string ?? string.Empty;

            return string.Empty;
        }

        private static string[] ParseExtensions(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return new string[0];

            string[] parts = raw.Split(AssetExtensionSeparators, StringSplitOptions.RemoveEmptyEntries);
            var normalized = new List<string>();

            for (int i = 0; i < parts.Length; ++i)
            {
                string token = parts[i].Trim().ToLowerInvariant();
                if (token.Length == 0)
                    continue;

                if (token[0] != '.')
                    token = "." + token;

                if (!normalized.Contains(token))
                    normalized.Add(token);
            }

            return normalized.ToArray();
        }

        private static bool TryMatchVector2Type(Type fieldType)
        {
            return HasFloatField(fieldType, "x") &&
                   HasFloatField(fieldType, "y") &&
                   !HasFloatField(fieldType, "z") &&
                   !HasFloatField(fieldType, "r");
        }

        private static bool TryMatchVector3Type(Type fieldType)
        {
            return HasFloatField(fieldType, "x") &&
                   HasFloatField(fieldType, "y") &&
                   HasFloatField(fieldType, "z") &&
                   !HasFloatField(fieldType, "r");
        }

        private static bool TryMatchColorType(Type fieldType, out int componentCount)
        {
            componentCount = 0;
            if (!HasFloatField(fieldType, "r") || !HasFloatField(fieldType, "g") || !HasFloatField(fieldType, "b"))
                return false;

            componentCount = HasFloatField(fieldType, "a") ? 4 : 3;
            return true;
        }

        private static bool HasFloatField(Type fieldType, string fieldName)
        {
            FieldInfo[] fields = fieldType.GetFields(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo field = fields[i];
                if (!string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (field.FieldType == typeof(float))
                    return true;
            }

            return false;
        }

        private static Type ResolveScriptType(Assembly scriptAssembly, string scriptTypeName)
        {
            if (scriptAssembly == null || string.IsNullOrEmpty(scriptTypeName))
                return null;

            string normalizedName = scriptTypeName.Trim();
            if (normalizedName.Length == 0)
                return null;

            Type exact = scriptAssembly.GetType(normalizedName, false);
            if (exact != null)
                return exact;

            if (normalizedName.IndexOf('.') < 0)
            {
                Type gameScriptsType = scriptAssembly.GetType("GameScripts." + normalizedName, false);
                if (gameScriptsType != null)
                    return gameScriptsType;
            }

            Type[] types;
            try
            {
                types = scriptAssembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                return null;
            }

            if (types == null)
                return null;

            for (int i = 0; i < types.Length; ++i)
            {
                Type candidate = types[i];
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.FullName, normalizedName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            return null;
        }

        private static long ParseInt64(string text, long defaultValue)
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
                return parsed;

            return defaultValue;
        }

        private static float ParseFloat(string text, float defaultValue)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return parsed;

            return defaultValue;
        }

        private static bool ParseBool(string text, bool defaultValue)
        {
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1")
                return true;
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || text == "0")
                return false;
            return defaultValue;
        }

        private static float[] ParseComponents(string rawValue, int expectedCount)
        {
            var values = new float[expectedCount];
            if (string.IsNullOrEmpty(rawValue))
                return values;

            string[] parts = rawValue.Split(',');
            int readCount = parts.Length;
            if (readCount > expectedCount)
                readCount = expectedCount;

            for (int i = 0; i < readCount; ++i)
                values[i] = ParseFloat(parts[i].Trim(), 0.0f);

            return values;
        }
    }
}
