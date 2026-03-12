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
                             "        public static void OnEngineStart()\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] OnEngineStart called. Template: " + StringUtilities.EscapeCSharpString(templateName) + ".\");\n" +
                             "        }\n\n" +
                             "        public static void OnEngineUpdate(float deltaTime)\n" +
                             "        {\n" +
                             "            _ = deltaTime;\n" +
                             "        }\n\n" +
                             "        public static void OnEngineShutdown()\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] OnEngineShutdown called.\");\n" +
                             "        }\n" +
                             "    }\n\n" +
                             "    public sealed class SpinnerScript : MonoBehaviour\n" +
                             "    {\n" +
                             "        public float moveSpeed = 280.0f;\n" +
                             "        private float _logAccumulator;\n\n" +
                             "        protected override void Start()\n" +
                             "        {\n" +
                             "            Debug.Log(\"[GameScripts] SpinnerScript started on '\" + gameObject.name + \"'.\");\n" +
                             "        }\n\n" +
                             "        protected override void Update()\n" +
                             "        {\n" +
                             "            float dt = Time.deltaTime;\n" +
                             "            float moveX = 0.0f;\n" +
                             "            float moveY = 0.0f;\n\n" +
                             "            if (Input.GetKey(KeyCode.W)) moveY += moveSpeed * dt;\n" +
                             "            if (Input.GetKey(KeyCode.S)) moveY -= moveSpeed * dt;\n" +
                             "            if (Input.GetKey(KeyCode.A)) moveX -= moveSpeed * dt;\n" +
                             "            if (Input.GetKey(KeyCode.D)) moveX += moveSpeed * dt;\n\n" +
                             "            if (moveX == 0.0f && moveY == 0.0f)\n" +
                             "                return;\n\n" +
                             "            transform.position = transform.position + new Vector3(moveX, moveY, 0.0f);\n" +
                             "            _logAccumulator += dt;\n" +
                             "            if (_logAccumulator >= 0.2f)\n" +
                             "            {\n" +
                             "                _logAccumulator = 0.0f;\n" +
                             "                Debug.Log(\"[GameScripts] Spinner moved to \" + transform.position + \".\");\n" +
                             "            }\n" +
                             "        }\n" +
                             "    }\n" +
                             "}\n";

            Directory.CreateDirectory(Path.GetDirectoryName(scriptTemplatePath));
            File.WriteAllText(scriptTemplatePath, content);
        }
    }
}
