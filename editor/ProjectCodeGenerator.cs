using System;
using System.IO;

namespace EngineEditor
{
    internal static class ProjectCodeGenerator
    {
        private const string GeneratedScriptTargetFramework = "net48";
        private const int GeneratedProjectFileVersion = 3;
        private const string GeneratedEngineVersion = "2026.03";
        private const string CSharpProjectTypeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";
        private const string LegacyScriptTemplateStartMarker = "OnEngineStart called. Template:";

        private sealed class ProjectGenerationResult
        {
            public string ScriptProjectFile = string.Empty;
            public string ScriptSolutionFile = string.Empty;
            public string AssemblyPath = string.Empty;
            public string EngineApiProject = string.Empty;
        }

        private static readonly string[] ProjectTemplates = new string[]
        {
            "Core 2D",
            "Universal 2D",
            "Universal 3D",
            "Sample",
        };

        public static string[] GetProjectTemplates()
        {
            return ProjectTemplates;
        }

        public static void GenerateProject(string projectRootPath, string projectName, string templateName)
        {
            Directory.CreateDirectory(projectRootPath);
            Directory.CreateDirectory(Path.Combine(projectRootPath, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRootPath, "Scripts"));
            Directory.CreateDirectory(Path.Combine(projectRootPath, "Scenes"));

            string engineApiProjectAbsolutePath = ResolveEngineApiProjectPath();
            var generation = GenerateManagedProjectArtifacts(projectRootPath, projectName, templateName, engineApiProjectAbsolutePath);
            string json = BuildProjectJsonContent(projectName, templateName, generation);
            File.WriteAllText(Path.Combine(projectRootPath, "project.json"), json);
        }

        public static bool TryUpgradeLegacyScriptTemplate(string projectRootPath)
        {
            if (string.IsNullOrWhiteSpace(projectRootPath) || !Directory.Exists(projectRootPath))
                return false;

            string scriptTemplatePath = Path.Combine(projectRootPath, "Scripts", "ScriptEntry.cs");
            if (!File.Exists(scriptTemplatePath))
                return false;

            string existingContent;
            try
            {
                existingContent = File.ReadAllText(scriptTemplatePath);
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(existingContent))
                return false;

            bool hasLegacySpinner = existingContent.IndexOf("public sealed class SpinnerScript", StringComparison.Ordinal) >= 0;
            bool hasLegacyOnCreate = existingContent.IndexOf("public void OnCreate(uint entityId)", StringComparison.Ordinal) >= 0;
            bool hasMonoBehaviourBase = existingContent.IndexOf(": MonoBehaviour", StringComparison.Ordinal) >= 0;

            if (!(hasLegacySpinner && hasLegacyOnCreate) || hasMonoBehaviourBase)
                return false;

            string templateName = ExtractTemplateNameFromLegacyScript(existingContent);
            string updatedContent = BuildInitialScriptTemplateContent(templateName);

            try
            {
                string backupPath = scriptTemplatePath + ".legacy.bak";
                if (!File.Exists(backupPath))
                    File.WriteAllText(backupPath, existingContent);

                File.WriteAllText(scriptTemplatePath, updatedContent);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ProjectGenerationResult GenerateManagedProjectArtifacts(string projectRootPath, string projectName, string templateName, string engineApiProjectAbsolutePath)
        {
            string csprojFileName = projectName + ".csproj";
            string slnFileName = projectName + ".sln";
            string csprojPath = Path.Combine(projectRootPath, csprojFileName);
            string slnPath = Path.Combine(projectRootPath, slnFileName);

            string engineApiProjectRelativePath = string.Empty;
            if (!string.IsNullOrEmpty(engineApiProjectAbsolutePath) && File.Exists(engineApiProjectAbsolutePath))
                engineApiProjectRelativePath = StringUtilities.MakeRelativePath(projectRootPath, engineApiProjectAbsolutePath);

            string assemblyName = StringUtilities.BuildSafeAssemblyName(projectName);
            string csprojContent = BuildCsprojContent(assemblyName, engineApiProjectRelativePath);
            File.WriteAllText(csprojPath, csprojContent);

            string projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
            string engineApiProjectGuid = string.IsNullOrEmpty(engineApiProjectRelativePath)
                ? string.Empty
                : Guid.NewGuid().ToString("B").ToUpperInvariant();
            string slnContent = BuildSlnContent(projectName, csprojFileName, projectGuid, engineApiProjectRelativePath, engineApiProjectGuid);
            File.WriteAllText(slnPath, slnContent);

            string scriptTemplatePath = Path.Combine(projectRootPath, "Scripts", "ScriptEntry.cs");
            if (!File.Exists(scriptTemplatePath))
                WriteInitialScriptTemplate(scriptTemplatePath, templateName);

            return new ProjectGenerationResult
            {
                ScriptProjectFile = csprojFileName,
                ScriptSolutionFile = slnFileName,
                AssemblyPath = ("bin/Debug/" + GeneratedScriptTargetFramework + "/" + assemblyName + ".dll").Replace("\\", "/"),
                EngineApiProject = string.IsNullOrEmpty(engineApiProjectRelativePath) ? string.Empty : engineApiProjectRelativePath.Replace("\\", "/"),
            };
        }

        private static string BuildProjectJsonContent(string projectName, string templateName, ProjectGenerationResult generation)
        {
            return "{\n" +
                   "  \"name\": \"" + StringUtilities.EscapeJson(projectName) + "\",\n" +
                   "  \"template\": \"" + StringUtilities.EscapeJson(templateName) + "\",\n" +
                   "  \"version\": " + GeneratedProjectFileVersion + ",\n" +
                   "  \"engineVersion\": \"" + GeneratedEngineVersion + "\",\n" +
                   "  \"assetsRoot\": \"Assets\",\n" +
                   "  \"scriptsRoot\": \"Scripts\",\n" +
                   "  \"scenesRoot\": \"Scenes\",\n" +
                   "  \"libraryRoot\": \"Library\",\n" +
                   "  \"scriptProject\": \"" + StringUtilities.EscapeJson(generation.ScriptProjectFile) + "\",\n" +
                   "  \"scriptSolution\": \"" + StringUtilities.EscapeJson(generation.ScriptSolutionFile) + "\",\n" +
                   "  \"assemblyPath\": \"" + StringUtilities.EscapeJson(generation.AssemblyPath) + "\",\n" +
                   "  \"engineApiProject\": \"" + StringUtilities.EscapeJson(generation.EngineApiProject) + "\",\n" +
                   "  \"targetFramework\": \"" + GeneratedScriptTargetFramework + "\"\n" +
                   "}\n";
        }

        private static string BuildCsprojContent(string assemblyName, string engineApiProjectRelativePath)
        {
            string content = "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                   "  <PropertyGroup>\n" +
                   "    <TargetFramework>" + GeneratedScriptTargetFramework + "</TargetFramework>\n" +
                   "    <LangVersion>8.0</LangVersion>\n" +
                   "    <AssemblyName>" + StringUtilities.EscapeXml(assemblyName) + "</AssemblyName>\n" +
                   "    <RootNamespace>GameScripts</RootNamespace>\n" +
                   "  </PropertyGroup>\n";

            if (!string.IsNullOrWhiteSpace(engineApiProjectRelativePath))
            {
                content += "  <ItemGroup Label=\"EngineManagedApiProjectReference\">\n" +
                           "    <ProjectReference Include=\"" + StringUtilities.EscapeXml(engineApiProjectRelativePath) + "\" />\n" +
                           "  </ItemGroup>\n";
            }

            content += "</Project>\n";
            return content;
        }

        private static string BuildSlnContent(string projectName,
                                              string csprojFileName,
                                              string projectGuid,
                                              string engineApiProjectRelativePath,
                                              string engineApiProjectGuid)
        {
            string content = "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
                   "# Visual Studio Version 17\r\n" +
                   "VisualStudioVersion = 17.0.0.0\r\n" +
                   "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
                   "Project(\"" + CSharpProjectTypeGuid + "\") = \"" + StringUtilities.EscapeSolutionValue(projectName) + "\", \"" + StringUtilities.EscapeSolutionValue(csprojFileName) + "\", \"" + projectGuid + "\"\r\n" +
                   "EndProject\r\n";

            if (!string.IsNullOrWhiteSpace(engineApiProjectRelativePath) && !string.IsNullOrWhiteSpace(engineApiProjectGuid))
            {
                content += "Project(\"" + CSharpProjectTypeGuid + "\") = \"EngineManagedApi\", \"" + StringUtilities.EscapeSolutionValue(engineApiProjectRelativePath) + "\", \"" + engineApiProjectGuid + "\"\r\n" +
                           "EndProject\r\n";
            }

            content += "Global\r\n" +
                       "    GlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
                       "        Debug|Any CPU = Debug|Any CPU\r\n" +
                       "        Release|Any CPU = Release|Any CPU\r\n" +
                       "    EndGlobalSection\r\n" +
                       "    GlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
                       "        " + projectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n" +
                       "        " + projectGuid + ".Debug|Any CPU.Build.0 = Debug|Any CPU\r\n" +
                       "        " + projectGuid + ".Release|Any CPU.ActiveCfg = Release|Any CPU\r\n" +
                       "        " + projectGuid + ".Release|Any CPU.Build.0 = Release|Any CPU\r\n";

            if (!string.IsNullOrWhiteSpace(engineApiProjectGuid))
            {
                content += "        " + engineApiProjectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n" +
                           "        " + engineApiProjectGuid + ".Debug|Any CPU.Build.0 = Debug|Any CPU\r\n" +
                           "        " + engineApiProjectGuid + ".Release|Any CPU.ActiveCfg = Release|Any CPU\r\n" +
                           "        " + engineApiProjectGuid + ".Release|Any CPU.Build.0 = Release|Any CPU\r\n";
            }

            content += "    EndGlobalSection\r\n" +
                       "    GlobalSection(SolutionProperties) = preSolution\r\n" +
                       "        HideSolutionNode = FALSE\r\n" +
                       "    EndGlobalSection\r\n" +
                       "EndGlobal\r\n";

            return content;
        }

        private static string ResolveEngineApiProjectPath()
        {
            string candidate = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "EngineManagedApi.csproj");
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : string.Empty;
        }

        private static void WriteInitialScriptTemplate(string scriptTemplatePath, string templateName)
        {
            string content = BuildInitialScriptTemplateContent(templateName);

            Directory.CreateDirectory(Path.GetDirectoryName(scriptTemplatePath));
            File.WriteAllText(scriptTemplatePath, content);
        }

        private static string BuildInitialScriptTemplateContent(string templateName)
        {
            return "using Engine;\n\n"
                + "namespace GameScripts\n"
                + "{\n"
               + "    public sealed class SpinnerScript : MonoBehaviour\n"
               + "    {\n"
               + "        private float _logAccumulator;\n\n"
               + "        // Called once when the script instance is created.\n"
               + "        protected override void Start()\n"
               + "        {\n"
               + "            Debug.Log(\"[GameScripts] SpinnerScript started on '\" + gameObject.name + \"'.\");\n"
               + "        }\n\n"
               + "        // Called every frame while the script is enabled.\n"
               + "        protected override void Update()\n"
               + "        {\n"
               + "            _logAccumulator += Time.deltaTime;\n"
               + "            if (_logAccumulator >= 1.0f)\n"
               + "            {\n"
               + "                _logAccumulator = 0.0f;\n"
               + "                Debug.Log(\"[GameScripts] SpinnerScript Update tick on '\" + gameObject.name + \"'.\");\n"
               + "            }\n"
               + "        }\n"
               + "    }\n"
               + "}\n";
        }

        private static string ExtractTemplateNameFromLegacyScript(string scriptContent)
        {
            if (string.IsNullOrWhiteSpace(scriptContent))
                return "Universal 2D";

            int markerStart = scriptContent.IndexOf(LegacyScriptTemplateStartMarker, StringComparison.Ordinal);
            if (markerStart < 0)
                return "Universal 2D";

            int valueStart = markerStart + LegacyScriptTemplateStartMarker.Length;
            int valueEnd = scriptContent.IndexOf('.', valueStart);
            if (valueEnd <= valueStart)
                return "Universal 2D";

            string parsed = scriptContent.Substring(valueStart, valueEnd - valueStart).Trim();
            return string.IsNullOrWhiteSpace(parsed) ? "Universal 2D" : parsed;
        }
    }
}
