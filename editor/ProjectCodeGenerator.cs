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
            string content = "using Engine;\n\n" +
                             "namespace GameScripts\n" +
                             "{\n" +
                             "    public static class ScriptEntry\n" +
                             "    {\n" +
                             "        private static float _timeAccumulator;\n\n" +
                             "        public static void OnEngineStart()\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] OnEngineStart called. Template: " + StringUtilities.EscapeCSharpString(templateName) + ".\");\n" +
                             "        }\n\n" +
                             "        public static void OnEngineUpdate(float deltaTime)\n" +
                             "        {\n" +
                             "            _timeAccumulator += deltaTime;\n" +
                             "            if (_timeAccumulator >= 1.0f)\n" +
                             "            {\n" +
                             "                _timeAccumulator = 0.0f;\n" +
                             "                Debug.Log(\"[GameScripts] Tick from generated project script.\");\n" +
                             "            }\n" +
                             "        }\n\n" +
                             "        public static void OnEngineShutdown()\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] OnEngineShutdown called.\");\n" +
                             "        }\n" +
                             "    }\n\n" +
                             "    public sealed class SpinnerScript\n" +
                             "    {\n" +
                             "        private float _accumulator;\n\n" +
                             "        public void OnCreate(uint entityId)\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] SpinnerScript created for entity \" + entityId + \".\");\n" +
                             "        }\n\n" +
                             "        public void OnEnable(uint entityId)\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] SpinnerScript enabled for entity \" + entityId + \".\");\n" +
                             "        }\n\n" +
                             "        public void OnDisable(uint entityId)\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] SpinnerScript disabled for entity \" + entityId + \".\");\n" +
                             "        }\n\n" +
                             "        public void OnUpdate(uint entityId, float deltaTime)\n" +
                             "        {\n" +
                             "            _accumulator += deltaTime;\n" +
                             "            if (_accumulator >= 2.0f)\n" +
                             "            {\n" +
                             "                _accumulator = 0.0f;\n" +
                             "                Debug.Log(\"[GameScripts] SpinnerScript update on entity \" + entityId + \".\");\n" +
                             "            }\n" +
                             "        }\n\n" +
                             "        public void OnDestroy(uint entityId)\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] SpinnerScript destroyed for entity \" + entityId + \".\");\n" +
                             "        }\n" +
                             "    }\n" +
                             "}\n";

            Directory.CreateDirectory(Path.GetDirectoryName(scriptTemplatePath));
            File.WriteAllText(scriptTemplatePath, content);
        }
    }
}
