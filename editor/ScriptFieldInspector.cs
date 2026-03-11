using System;
using System.Collections.Generic;
using System.Globalization;
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
        }

        private sealed class ScriptFieldDescriptor
        {
            public FieldInfo Field;
            public ScriptFieldKind Kind;
            public int ComponentCount;
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
        private static bool _optionsRegistered = false;
        private static bool _assetContextMenuRegistered = false;
        private static bool _hideTransformDuplicateFields = true;

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
                                                   _ => AssetPanel.RequestOpenCreateScriptPopup(),
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
            if (validationSnapshot == null || validationSnapshot.ScriptAssembly == null)
                return;

            Type scriptType = ResolveScriptType(validationSnapshot.ScriptAssembly, scriptTypeName);
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
                        if (descriptor.ComponentCount >= 4)
                        {
                            float r = values[0];
                            float g = values[1];
                            float b = values[2];
                            float a = values[3];
                            if (InspectorInputs.Quaternion(fieldName, ref r, ref g, ref b, ref a, 0.01f))
                            {
                                newRawValue = r.ToString(CultureInfo.InvariantCulture) + "," +
                                              g.ToString(CultureInfo.InvariantCulture) + "," +
                                              b.ToString(CultureInfo.InvariantCulture) + "," +
                                              a.ToString(CultureInfo.InvariantCulture);
                                changed = true;
                            }
                        }
                        else
                        {
                            float r = values[0];
                            float g = values[1];
                            float b = values[2];
                            if (InspectorInputs.Vector3(fieldName, ref r, ref g, ref b, 0.01f))
                            {
                                newRawValue = r.ToString(CultureInfo.InvariantCulture) + "," +
                                              g.ToString(CultureInfo.InvariantCulture) + "," +
                                              b.ToString(CultureInfo.InvariantCulture);
                                changed = true;
                            }
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

        private static ScriptFieldDescriptor[] GetVisibleFields(Type scriptType)
        {
            string cacheKey = scriptType.Assembly.FullName + "|" + scriptType.FullName;
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

                ScriptFieldKind kind = DetermineFieldKind(field.FieldType, out int componentCount);
                if (kind == ScriptFieldKind.Unsupported)
                    continue;

                visible.Add(new ScriptFieldDescriptor
                {
                    Field = field,
                    Kind = kind,
                    ComponentCount = componentCount,
                });
            }

            ScriptFieldDescriptor[] result = visible.ToArray();
            FieldCache[cacheKey] = result;
            return result;
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
                    string.Equals(attributeName, "SerializeField", StringComparison.Ordinal))
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

        private static ScriptFieldKind DetermineFieldKind(Type fieldType, out int componentCount)
        {
            componentCount = 0;
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
                return ScriptFieldKind.String;

            if (fieldType.IsEnum)
                return ScriptFieldKind.Enum;

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
