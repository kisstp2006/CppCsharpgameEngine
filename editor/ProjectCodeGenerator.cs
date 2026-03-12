using System;
using System.IO;
using System.Text;

namespace EngineEditor
{
    internal static class ProjectCodeGenerator
    {
        private const string GeneratedScriptTargetFramework = "net48";
        private const int GeneratedProjectFileVersion = 3;
        private const int GeneratedSceneFileVersion = 4;
        private const string GeneratedEngineVersion = "2026.03";
        private const string CSharpProjectTypeGuid = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";
        private const string LegacyScriptTemplateStartMarker = "OnEngineStart called. Template:";
        private const string TemplateDebugMonitor = "Debug Monitor";
        private const string TemplateSpriteInputController = "Sprite Input Controller";
        private const string TemplateEventLogger = "Event Logger";
        private const string TemplateMinimalEmpty = "Minimal Empty";
        private const string TemplateInputDebugHybrid = "Input + Debug Hybrid";

        private sealed class ProjectGenerationResult
        {
            public string ScriptProjectFile = string.Empty;
            public string ScriptSolutionFile = string.Empty;
            public string AssemblyPath = string.Empty;
            public string EngineApiProject = string.Empty;
        }

        private static readonly string[] ProjectTemplates = new string[]
        {
            TemplateDebugMonitor,
            TemplateSpriteInputController,
            TemplateEventLogger,
            TemplateMinimalEmpty,
            TemplateInputDebugHybrid,
        };

        public static string[] GetProjectTemplates()
        {
            return ProjectTemplates;
        }

        public static void GenerateProject(string projectRootPath,
                                           string projectName,
                                           string templateName,
                                           bool generateStarterContent,
                                           bool generateStarterScene)
        {
            Directory.CreateDirectory(projectRootPath);
            Directory.CreateDirectory(Path.Combine(projectRootPath, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRootPath, "Scripts"));
            Directory.CreateDirectory(Path.Combine(projectRootPath, "Scenes"));

            string engineApiProjectAbsolutePath = ResolveEngineApiProjectPath();
            var generation = GenerateManagedProjectArtifacts(projectRootPath,
                                                             projectName,
                                                             templateName,
                                                             engineApiProjectAbsolutePath,
                                                             generateStarterContent);
            string json = BuildProjectJsonContent(projectName, templateName, generation);
            File.WriteAllText(Path.Combine(projectRootPath, "project.json"), json);

            if (generateStarterScene)
                WriteStarterSceneTemplate(projectRootPath, templateName, generateStarterContent);
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

        private static ProjectGenerationResult GenerateManagedProjectArtifacts(string projectRootPath,
                                                                               string projectName,
                                                                               string templateName,
                                                                               string engineApiProjectAbsolutePath,
                                                                               bool generateStarterContent)
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

            if (generateStarterContent)
            {
                string scriptTemplatePath = Path.Combine(projectRootPath, "Scripts", ResolveTemplateScriptFileName(templateName));
                if (!File.Exists(scriptTemplatePath))
                    WriteInitialScriptTemplate(scriptTemplatePath, templateName);
            }

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
            string resolvedTemplate = ResolveTemplateName(templateName);
            switch (resolvedTemplate)
            {
                case TemplateDebugMonitor:
                    return BuildDebugMonitorScriptTemplate();
                case TemplateSpriteInputController:
                    return BuildSpriteInputControllerScriptTemplate();
                case TemplateEventLogger:
                    return BuildEventLoggerScriptTemplate();
                case TemplateMinimalEmpty:
                    return BuildMinimalEmptyScriptTemplate();
                case TemplateInputDebugHybrid:
                    return BuildInputDebugHybridScriptTemplate();
                default:
                    return BuildDebugMonitorScriptTemplate();
            }
        }

        private static string ExtractTemplateNameFromLegacyScript(string scriptContent)
        {
            if (string.IsNullOrWhiteSpace(scriptContent))
                return TemplateDebugMonitor;

            int markerStart = scriptContent.IndexOf(LegacyScriptTemplateStartMarker, StringComparison.Ordinal);
            if (markerStart < 0)
                return TemplateDebugMonitor;

            int valueStart = markerStart + LegacyScriptTemplateStartMarker.Length;
            int valueEnd = scriptContent.IndexOf('.', valueStart);
            if (valueEnd <= valueStart)
                return TemplateDebugMonitor;

            string parsed = scriptContent.Substring(valueStart, valueEnd - valueStart).Trim();
            return string.IsNullOrWhiteSpace(parsed) ? TemplateDebugMonitor : parsed;
        }

        private static void WriteStarterSceneTemplate(string projectRootPath,
                                                      string templateName,
                                                      bool includeScriptComponent)
        {
            string scenePath = Path.Combine(projectRootPath, "Scenes", "Main.scene.json");
            if (File.Exists(scenePath))
                return;

            string sceneJson = BuildStarterSceneJsonContent(templateName, includeScriptComponent);
            File.WriteAllText(scenePath, sceneJson);
        }

        private static string BuildStarterSceneJsonContent(string templateName, bool includeScriptComponent)
        {
            string resolvedTemplate = ResolveTemplateName(templateName);
            string actorName = ResolveTemplateActorName(resolvedTemplate);
            string scriptClassName = ResolveTemplateScriptClassName(resolvedTemplate);
            bool includeSprite = resolvedTemplate == TemplateSpriteInputController ||
                                 resolvedTemplate == TemplateInputDebugHybrid;

            var json = new StringBuilder();
            json.Append("{\n");
            json.Append("  \"sceneVersion\": ").Append(GeneratedSceneFileVersion).Append(",\n");
            json.Append("  \"entities\": [\n");
            json.Append("    {\n");
            json.Append("      \"id\": 1,\n");
            json.Append("      \"name\": \"Main Camera\",\n");
            json.Append("      \"tag\": \"Untagged\",\n");
            json.Append("      \"layer\": 0,\n");
            json.Append("      \"active\": true,\n");
            json.Append("      \"static\": false,\n");
            json.Append("      \"components\": {\n");
            json.Append("        \"transform\": {\n");
            json.Append("          \"x\": 0.0,\n");
            json.Append("          \"y\": 0.0,\n");
            json.Append("          \"width\": 100.0,\n");
            json.Append("          \"height\": 100.0\n");
            json.Append("        },\n");
            json.Append("        \"camera\": {\n");
            json.Append("          \"x\": 0.0,\n");
            json.Append("          \"y\": 0.0,\n");
            json.Append("          \"zoom\": 1.0\n");
            json.Append("        }\n");
            json.Append("      }\n");
            json.Append("    },\n");
            json.Append("    {\n");
            json.Append("      \"id\": 2,\n");
            json.Append("      \"name\": \"").Append(StringUtilities.EscapeJson(actorName)).Append("\",\n");
            json.Append("      \"tag\": \"Untagged\",\n");
            json.Append("      \"layer\": 0,\n");
            json.Append("      \"active\": true,\n");
            json.Append("      \"static\": false,\n");
            json.Append("      \"components\": {\n");
            json.Append("        \"transform\": {\n");
            json.Append("          \"x\": 0.0,\n");
            json.Append("          \"y\": 0.0,\n");
            json.Append("          \"width\": 96.0,\n");
            json.Append("          \"height\": 96.0\n");
            json.Append("        }");

            if (includeSprite)
            {
                json.Append(",\n");
                json.Append("        \"sprite\": {\n");
                json.Append("          \"textureAssetHandle\": 0,\n");
                json.Append("          \"textureAssetPath\": \"\",\n");
                json.Append("          \"centered\": true,\n");
                json.Append("          \"offsetX\": 0.0,\n");
                json.Append("          \"offsetY\": 0.0,\n");
                json.Append("          \"flipH\": false,\n");
                json.Append("          \"flipV\": false,\n");
                json.Append("          \"hframes\": 1,\n");
                json.Append("          \"vframes\": 1,\n");
                json.Append("          \"frame\": 0,\n");
                json.Append("          \"regionEnabled\": false,\n");
                json.Append("          \"regionX\": 0.0,\n");
                json.Append("          \"regionY\": 0.0,\n");
                json.Append("          \"regionWidth\": 0.0,\n");
                json.Append("          \"regionHeight\": 0.0,\n");
                json.Append("          \"fallbackColor\": 4294967295\n");
                json.Append("        }");
            }

            if (includeScriptComponent)
            {
                json.Append(",\n");
                json.Append("        \"script\": {\n");
                json.Append("          \"classNamespace\": \"GameScripts\",\n");
                json.Append("          \"className\": \"").Append(StringUtilities.EscapeJson(scriptClassName)).Append("\",\n");
                json.Append("          \"enabled\": true,\n");
                json.Append("          \"serializedFieldState\": \"\"\n");
                json.Append("        }");
            }

            json.Append("\n");
            json.Append("      }\n");
            json.Append("    }\n");
            json.Append("  ]\n");
            json.Append("}\n");

            return json.ToString();
        }

        private static string ResolveTemplateName(string templateName)
        {
            if (string.IsNullOrWhiteSpace(templateName))
                return TemplateDebugMonitor;

            for (int i = 0; i < ProjectTemplates.Length; ++i)
            {
                if (string.Equals(ProjectTemplates[i], templateName, StringComparison.OrdinalIgnoreCase))
                    return ProjectTemplates[i];
            }

            return TemplateDebugMonitor;
        }

        private static string ResolveTemplateActorName(string templateName)
        {
            switch (ResolveTemplateName(templateName))
            {
                case TemplateSpriteInputController:
                    return "Player";
                case TemplateEventLogger:
                    return "EventLogger";
                case TemplateMinimalEmpty:
                    return "EmptyActor";
                case TemplateInputDebugHybrid:
                    return "HybridActor";
                case TemplateDebugMonitor:
                default:
                    return "DebugMonitor";
            }
        }

        private static string ResolveTemplateScriptClassName(string templateName)
        {
            switch (ResolveTemplateName(templateName))
            {
                case TemplateSpriteInputController:
                    return "SpriteInputControllerScript";
                case TemplateEventLogger:
                    return "EventLoggerScript";
                case TemplateMinimalEmpty:
                    return "MinimalEmptyScript";
                case TemplateInputDebugHybrid:
                    return "InputDebugHybridScript";
                case TemplateDebugMonitor:
                default:
                    return "DebugMonitorScript";
            }
        }

        private static string ResolveTemplateScriptFileName(string templateName)
        {
            return ResolveTemplateScriptClassName(templateName) + ".cs";
        }

        private static string BuildDebugMonitorScriptTemplate()
        {
            return "using Engine;\n\n"
                + "namespace GameScripts\n"
                + "{\n"
                + "    public sealed class DebugMonitorScript : MonoBehaviour\n"
                + "    {\n"
                + "        private float _sampleSeconds;\n"
                + "        private int _sampleFrames;\n\n"
                + "        protected override void Start()\n"
                + "        {\n"
                + "            Debug.Log(\"[Starter] Debug monitor initialized on '\" + gameObject.name + \"'.\");\n"
                + "        }\n\n"
                + "        protected override void Update()\n"
                + "        {\n"
                + "            _sampleSeconds += Time.deltaTime;\n"
                + "            ++_sampleFrames;\n\n"
                + "            if (_sampleSeconds >= 1.0f)\n"
                + "            {\n"
                + "                float fps = _sampleFrames / _sampleSeconds;\n"
                + "                Debug.Log(\"[Starter] FPS: \" + fps.ToString(\"0.0\"));\n"
                + "                _sampleSeconds = 0.0f;\n"
                + "                _sampleFrames = 0;\n"
                + "            }\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static string BuildSpriteInputControllerScriptTemplate()
        {
            return "using Engine;\n\n"
                + "namespace GameScripts\n"
                + "{\n"
                + "    public sealed class SpriteInputControllerScript : MonoBehaviour\n"
                + "    {\n"
                + "        private float _speed = 260.0f;\n\n"
                + "        protected override void Start()\n"
                + "        {\n"
                + "            Debug.Log(\"[Starter] WASD movement is active for '\" + gameObject.name + \"'.\");\n"
                + "        }\n\n"
                + "        protected override void Update()\n"
                + "        {\n"
                + "            Vector3 move = new Vector3(0.0f, 0.0f, 0.0f);\n"
                + "            if (Input.GetKey(KeyCode.A))\n"
                + "                move.x -= 1.0f;\n"
                + "            if (Input.GetKey(KeyCode.D))\n"
                + "                move.x += 1.0f;\n"
                + "            if (Input.GetKey(KeyCode.S))\n"
                + "                move.y -= 1.0f;\n"
                + "            if (Input.GetKey(KeyCode.W))\n"
                + "                move.y += 1.0f;\n\n"
                + "            if (move.x == 0.0f && move.y == 0.0f)\n"
                + "                return;\n\n"
                + "            Vector3 position = transform.position;\n"
                + "            position.x += move.x * _speed * Time.deltaTime;\n"
                + "            position.y += move.y * _speed * Time.deltaTime;\n"
                + "            transform.position = position;\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static string BuildEventLoggerScriptTemplate()
        {
            return "using Engine;\n\n"
                + "namespace GameScripts\n"
                + "{\n"
                + "    public sealed class EventLoggerScript : MonoBehaviour\n"
                + "    {\n"
                + "        protected override void Start()\n"
                + "        {\n"
                + "            Debug.Log(\"[Starter] Start on '\" + gameObject.name + \"'.\");\n"
                + "        }\n\n"
                + "        protected override void OnEnable()\n"
                + "        {\n"
                + "            Debug.Log(\"[Starter] OnEnable on '\" + gameObject.name + \"'.\");\n"
                + "        }\n\n"
                + "        protected override void OnDisable()\n"
                + "        {\n"
                + "            Debug.Log(\"[Starter] OnDisable on '\" + gameObject.name + \"'.\");\n"
                + "        }\n\n"
                + "        protected override void OnDestroy()\n"
                + "        {\n"
                + "            Debug.Log(\"[Starter] OnDestroy on '\" + gameObject.name + \"'.\");\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static string BuildMinimalEmptyScriptTemplate()
        {
            return "using Engine;\n\n"
                + "namespace GameScripts\n"
                + "{\n"
                + "    public sealed class MinimalEmptyScript : MonoBehaviour\n"
                + "    {\n"
                + "    }\n"
                + "}\n";
        }

        private static string BuildInputDebugHybridScriptTemplate()
        {
            return "using Engine;\n\n"
                + "namespace GameScripts\n"
                + "{\n"
                + "    public sealed class InputDebugHybridScript : MonoBehaviour\n"
                + "    {\n"
                + "        private float _speed = 220.0f;\n"
                + "        private float _logTimer;\n\n"
                + "        protected override void Start()\n"
                + "        {\n"
                + "            Debug.Log(\"[Starter] Hybrid input+debug script ready.\");\n"
                + "        }\n\n"
                + "        protected override void Update()\n"
                + "        {\n"
                + "            Vector3 move = new Vector3(0.0f, 0.0f, 0.0f);\n"
                + "            if (Input.GetKey(KeyCode.A))\n"
                + "                move.x -= 1.0f;\n"
                + "            if (Input.GetKey(KeyCode.D))\n"
                + "                move.x += 1.0f;\n"
                + "            if (Input.GetKey(KeyCode.S))\n"
                + "                move.y -= 1.0f;\n"
                + "            if (Input.GetKey(KeyCode.W))\n"
                + "                move.y += 1.0f;\n\n"
                + "            if (move.x != 0.0f || move.y != 0.0f)\n"
                + "            {\n"
                + "                Vector3 position = transform.position;\n"
                + "                position.x += move.x * _speed * Time.deltaTime;\n"
                + "                position.y += move.y * _speed * Time.deltaTime;\n"
                + "                transform.position = position;\n"
                + "            }\n\n"
                + "            _logTimer += Time.deltaTime;\n"
                + "            if (_logTimer >= 1.0f)\n"
                + "            {\n"
                + "                _logTimer = 0.0f;\n"
                + "                Vector3 current = transform.position;\n"
                + "                Debug.Log(\"[Starter] Position: \" + current.ToString());\n"
                + "            }\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }
    }
}
