using System;
using System.IO;

namespace EngineEditor
{
    internal static class StringUtilities
    {
        public static bool IsValidProjectName(string projectName)
        {
            if (string.IsNullOrWhiteSpace(projectName))
                return false;

            if (projectName == "." || projectName == "..")
                return false;

            return projectName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        public static string BuildSafeAssemblyName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "GameScripts";

            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; ++i)
            {
                char c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    chars[i] = '_';
            }

            string cleaned = new string(chars).Trim('_');
            return string.IsNullOrEmpty(cleaned) ? "GameScripts" : cleaned;
        }

        public static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }

        public static string EscapeSolutionValue(string value)
        {
            return value.Replace("\"", "\\\"");
        }

        public static string EscapeCSharpString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        public static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
