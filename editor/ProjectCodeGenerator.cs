using System;
using System.IO;

namespace EngineEditor
{
    internal static class ProjectCodeGenerator
    {
        private const string GeneratedScriptTargetFramework = "net48";

        private sealed class ProjectGenerationResult
        {
            public string ScriptProjectFile = string.Empty;
            public string ScriptSolutionFile = string.Empty;
            public string AssemblyPath = string.Empty;
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

            var generation = GenerateManagedProjectArtifacts(projectRootPath, projectName, templateName);
            string json = BuildProjectJsonContent(projectName, templateName, generation);
            File.WriteAllText(Path.Combine(projectRootPath, "project.json"), json);
        }

        private static ProjectGenerationResult GenerateManagedProjectArtifacts(string projectRootPath, string projectName, string templateName)
        {
            string csprojFileName = projectName + ".csproj";
            string slnFileName = projectName + ".sln";
            string csprojPath = Path.Combine(projectRootPath, csprojFileName);
            string slnPath = Path.Combine(projectRootPath, slnFileName);

            string assemblyName = StringUtilities.BuildSafeAssemblyName(projectName);
            string csprojContent = BuildCsprojContent(assemblyName);
            File.WriteAllText(csprojPath, csprojContent);

            string projectGuid = Guid.NewGuid().ToString("B").ToUpperInvariant();
            string slnContent = BuildSlnContent(projectName, csprojFileName, projectGuid);
            File.WriteAllText(slnPath, slnContent);

            string scriptTemplatePath = Path.Combine(projectRootPath, "Scripts", "ScriptEntry.cs");
            if (!File.Exists(scriptTemplatePath))
                WriteInitialScriptTemplate(scriptTemplatePath, templateName);

            return new ProjectGenerationResult
            {
                ScriptProjectFile = csprojFileName,
                ScriptSolutionFile = slnFileName,
                AssemblyPath = ("bin/Debug/" + GeneratedScriptTargetFramework + "/" + assemblyName + ".dll").Replace("\\", "/"),
            };
        }

        private static string BuildProjectJsonContent(string projectName, string templateName, ProjectGenerationResult generation)
        {
            return "{\n" +
                   "  \"name\": \"" + StringUtilities.EscapeJson(projectName) + "\",\n" +
                   "  \"template\": \"" + StringUtilities.EscapeJson(templateName) + "\",\n" +
                   "  \"version\": 1,\n" +
                   "  \"assetsRoot\": \"Assets\",\n" +
                   "  \"scriptsRoot\": \"Scripts\",\n" +
                   "  \"scenesRoot\": \"Scenes\",\n" +
                   "  \"libraryRoot\": \"Library\",\n" +
                   "  \"scriptProject\": \"" + StringUtilities.EscapeJson(generation.ScriptProjectFile) + "\",\n" +
                   "  \"scriptSolution\": \"" + StringUtilities.EscapeJson(generation.ScriptSolutionFile) + "\",\n" +
                   "  \"assemblyPath\": \"" + StringUtilities.EscapeJson(generation.AssemblyPath) + "\",\n" +
                   "  \"targetFramework\": \"" + GeneratedScriptTargetFramework + "\"\n" +
                   "}\n";
        }

        private static string BuildCsprojContent(string assemblyName)
        {
            return "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                   "  <PropertyGroup>\n" +
                   "    <TargetFramework>" + GeneratedScriptTargetFramework + "</TargetFramework>\n" +
                   "    <LangVersion>8.0</LangVersion>\n" +
                   "    <AssemblyName>" + StringUtilities.EscapeXml(assemblyName) + "</AssemblyName>\n" +
                   "    <RootNamespace>GameScripts</RootNamespace>\n" +
                   "  </PropertyGroup>\n" +
                   "</Project>\n";
        }

        private static string BuildSlnContent(string projectName, string csprojFileName, string projectGuid)
        {
            return "Microsoft Visual Studio Solution File, Format Version 12.00\r\n" +
                   "# Visual Studio Version 17\r\n" +
                   "VisualStudioVersion = 17.0.0.0\r\n" +
                   "MinimumVisualStudioVersion = 10.0.40219.1\r\n" +
                   "Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"" + StringUtilities.EscapeSolutionValue(projectName) + "\", \"" + StringUtilities.EscapeSolutionValue(csprojFileName) + "\", \"" + projectGuid + "\"\r\n" +
                   "EndProject\r\n" +
                   "Global\r\n" +
                   "    GlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n" +
                   "        Debug|Any CPU = Debug|Any CPU\r\n" +
                   "        Release|Any CPU = Release|Any CPU\r\n" +
                   "    EndGlobalSection\r\n" +
                   "    GlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n" +
                   "        " + projectGuid + ".Debug|Any CPU.ActiveCfg = Debug|Any CPU\r\n" +
                   "        " + projectGuid + ".Debug|Any CPU.Build.0 = Debug|Any CPU\r\n" +
                   "        " + projectGuid + ".Release|Any CPU.ActiveCfg = Release|Any CPU\r\n" +
                   "        " + projectGuid + ".Release|Any CPU.Build.0 = Release|Any CPU\r\n" +
                   "    EndGlobalSection\r\n" +
                   "    GlobalSection(SolutionProperties) = preSolution\r\n" +
                   "        HideSolutionNode = FALSE\r\n" +
                   "    EndGlobalSection\r\n" +
                   "EndGlobal\r\n";
        }

        private static void WriteInitialScriptTemplate(string scriptTemplatePath, string templateName)
        {
            string content = "using System;\n\n" +
                             "namespace GameScripts\n" +
                             "{\n" +
                             "    public static class ScriptEntry\n" +
                             "    {\n" +
                             "        private static float _timeAccumulator;\n\n" +
                             "        public static void OnEngineStart()\n" +
                             "        {\n" +
                             "            Console.WriteLine(\"[GameScripts] OnEngineStart called. Template: " + StringUtilities.EscapeCSharpString(templateName) + ".\");\n" +
                             "        }\n\n" +
                             "        public static void OnEngineUpdate(float deltaTime)\n" +
                             "        {\n" +
                             "            _timeAccumulator += deltaTime;\n" +
                             "            if (_timeAccumulator >= 1.0f)\n" +
                             "            {\n" +
                             "                _timeAccumulator = 0.0f;\n" +
                             "                Console.WriteLine(\"[GameScripts] Tick from generated project script.\");\n" +
                             "            }\n" +
                             "        }\n\n" +
                             "        public static void OnEngineShutdown()\n" +
                             "        {\n" +
                             "            Console.WriteLine(\"[GameScripts] OnEngineShutdown called.\");\n" +
                             "        }\n" +
                             "    }\n\n" +
                             "    public sealed class SpinnerScript\n" +
                             "    {\n" +
                             "        private float _accumulator;\n\n" +
                             "        public void OnCreate(uint entityId)\n" +
                             "        {\n" +
                             "            Console.WriteLine(\"[GameScripts] SpinnerScript created for entity \" + entityId + \".\");\n" +
                             "        }\n\n" +
                             "        public void OnEnable(uint entityId)\n" +
                             "        {\n" +
                             "            Console.WriteLine(\"[GameScripts] SpinnerScript enabled for entity \" + entityId + \".\");\n" +
                             "        }\n\n" +
                             "        public void OnDisable(uint entityId)\n" +
                             "        {\n" +
                             "            Console.WriteLine(\"[GameScripts] SpinnerScript disabled for entity \" + entityId + \".\");\n" +
                             "        }\n\n" +
                             "        public void OnUpdate(uint entityId, float deltaTime)\n" +
                             "        {\n" +
                             "            _accumulator += deltaTime;\n" +
                             "            if (_accumulator >= 2.0f)\n" +
                             "            {\n" +
                             "                _accumulator = 0.0f;\n" +
                             "                Console.WriteLine(\"[GameScripts] SpinnerScript update on entity \" + entityId + \".\");\n" +
                             "            }\n" +
                             "        }\n\n" +
                             "        public void OnDestroy(uint entityId)\n" +
                             "        {\n" +
                             "            Console.WriteLine(\"[GameScripts] SpinnerScript destroyed for entity \" + entityId + \".\");\n" +
                             "        }\n" +
                             "    }\n" +
                             "}\n";

            Directory.CreateDirectory(Path.GetDirectoryName(scriptTemplatePath));
            File.WriteAllText(scriptTemplatePath, content);
        }
    }
}
