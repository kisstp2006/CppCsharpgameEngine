using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

using Engine;

namespace EngineEditor
{
    internal sealed class ScriptValidationSnapshot
    {
        public string[] RegisteredScriptTypes = new string[0];
        public Assembly ScriptAssembly;
        public bool IsCompiling;
        public bool HasCompileErrors;
        public string CompileSummary = "Script compilation has not run yet.";
        public string[] CompileErrorLines = new string[0];
    }

    internal sealed class ScriptTypeValidationResult
    {
        public bool IsValid;
        public string NormalizedTypeName = string.Empty;
        public string Message = string.Empty;
    }

    internal static class ScriptComponentValidation
    {
        private sealed class ScriptProjectInfo
        {
            public string ProjectRoot = string.Empty;
            public string ScriptProjectPath = string.Empty;
            public string ScriptsRootPath = string.Empty;
            public string ProjectKey = string.Empty;
        }

        private static readonly object SyncRoot = new object();

        private static string[] _cachedRegisteredScriptTypes = new string[0];
        private static Assembly _cachedScriptAssembly;
        private static string _cachedScriptAssemblyIdentity = string.Empty;
        private static DateTime _scriptTypeCacheTimestampUtc = DateTime.MinValue;

        private static string _trackedProjectKey = string.Empty;
        private static DateTime _lastSourceScanUtc = DateTime.MinValue;
        private static long _lastSourceFingerprint;
        private static DateTime _lastSourceChangeUtc = DateTime.MinValue;
        private static DateTime _lastBuildStartUtc = DateTime.MinValue;
        private static bool _hasAttemptedBuild;
        private static bool _isBuildRunning;
        private static bool _hasCompileErrors;
        private static bool _immediateBuildRequested;
        private static bool _pendingRuntimeReloadRequest;
        private static readonly List<string> _pendingCompileErrorConsoleLines = new List<string>();
        private static string _compileSummary = "Script compilation has not run yet.";
        private static string[] _compileErrorLines = new string[0];

        public static void RequestImmediateBuildForActiveProject()
        {
            ScriptProjectInfo projectInfo = ResolveScriptProjectInfo();
            if (projectInfo == null || string.IsNullOrEmpty(projectInfo.ScriptProjectPath) || !File.Exists(projectInfo.ScriptProjectPath))
                return;

            bool shouldStartBuild = false;

            lock (SyncRoot)
            {
                _immediateBuildRequested = true;

                if (!string.Equals(_trackedProjectKey, projectInfo.ProjectKey, StringComparison.OrdinalIgnoreCase))
                {
                    _trackedProjectKey = projectInfo.ProjectKey;
                    _lastSourceScanUtc = DateTime.MinValue;
                    _lastSourceFingerprint = 0;
                    _lastSourceChangeUtc = DateTime.MinValue;
                    _lastBuildStartUtc = DateTime.MinValue;
                    _hasAttemptedBuild = false;
                    _isBuildRunning = false;
                    _hasCompileErrors = false;
                    _pendingRuntimeReloadRequest = false;
                    _compileSummary = "Script compilation has not run yet.";
                    _compileErrorLines = new string[0];
                }

                if (!_isBuildRunning)
                {
                    shouldStartBuild = true;
                    _isBuildRunning = true;
                    _hasAttemptedBuild = true;
                    _immediateBuildRequested = false;
                    _lastBuildStartUtc = DateTime.UtcNow;
                    _compileSummary = "Compiling scripts...";
                    _compileErrorLines = new string[0];
                }
            }

            if (shouldStartBuild)
                StartBuildTask(projectInfo.ScriptProjectPath, projectInfo.ProjectRoot);
        }

        public static ScriptValidationSnapshot GetSnapshot()
        {
            DateTime nowUtc = DateTime.UtcNow;

            Assembly scriptAssembly = FindLoadedScriptAssembly();
            UpdateScriptTypeCache(scriptAssembly, nowUtc);

            ScriptProjectInfo projectInfo = ResolveScriptProjectInfo();
            UpdateCompilationState(projectInfo, nowUtc);
            DispatchPendingRuntimeReloadRequest();
            DispatchPendingCompileErrorLogs();

            lock (SyncRoot)
            {
                var snapshot = new ScriptValidationSnapshot();
                snapshot.RegisteredScriptTypes = _cachedRegisteredScriptTypes;
                snapshot.ScriptAssembly = _cachedScriptAssembly;
                snapshot.IsCompiling = _isBuildRunning;
                snapshot.HasCompileErrors = _hasCompileErrors;
                snapshot.CompileSummary = _compileSummary;
                snapshot.CompileErrorLines = _compileErrorLines;
                return snapshot;
            }
        }

        public static bool EnsureCompiledForPlay(out string statusMessage)
        {
            statusMessage = string.Empty;

            ScriptProjectInfo projectInfo = ResolveScriptProjectInfo();
            if (projectInfo == null || string.IsNullOrEmpty(projectInfo.ScriptProjectPath) || !File.Exists(projectInfo.ScriptProjectPath))
                return true;

            DateTime nowUtc = DateTime.UtcNow;
            long sourceFingerprint = ComputeSourceFingerprint(projectInfo);
            bool shouldStartBuild = false;

            lock (SyncRoot)
            {
                if (!string.Equals(_trackedProjectKey, projectInfo.ProjectKey, StringComparison.OrdinalIgnoreCase))
                {
                    _trackedProjectKey = projectInfo.ProjectKey;
                    _lastSourceScanUtc = DateTime.MinValue;
                    _lastSourceFingerprint = 0;
                    _lastSourceChangeUtc = DateTime.MinValue;
                    _lastBuildStartUtc = DateTime.MinValue;
                    _hasAttemptedBuild = false;
                    _isBuildRunning = false;
                    _hasCompileErrors = false;
                    _pendingRuntimeReloadRequest = false;
                    _compileSummary = "Script compilation has not run yet.";
                    _compileErrorLines = new string[0];
                }

                _lastSourceScanUtc = nowUtc;
                if (!_hasAttemptedBuild)
                {
                    _lastSourceFingerprint = sourceFingerprint;
                    _lastSourceChangeUtc = nowUtc;
                }
                else if (sourceFingerprint != _lastSourceFingerprint)
                {
                    _lastSourceFingerprint = sourceFingerprint;
                    _lastSourceChangeUtc = nowUtc;
                }

                if (_isBuildRunning)
                {
                    statusMessage = "Play blocked: script compilation is still running.";
                    return false;
                }

                bool needsBuild = !_hasAttemptedBuild || _lastSourceChangeUtc > _lastBuildStartUtc;
                if (needsBuild)
                {
                    shouldStartBuild = true;
                    _isBuildRunning = true;
                    _hasAttemptedBuild = true;
                    _immediateBuildRequested = false;
                    _lastBuildStartUtc = nowUtc;
                    _compileSummary = "Compiling scripts...";
                    _compileErrorLines = new string[0];
                    statusMessage = "Play delayed: compiling scripts before entering play mode.";
                }
                else if (_hasCompileErrors)
                {
                    statusMessage = "Play blocked: fix script compile errors first.";
                    return false;
                }
            }

            if (shouldStartBuild)
            {
                StartBuildTask(projectInfo.ScriptProjectPath, projectInfo.ProjectRoot);
                return false;
            }

            return true;
        }

        public static ScriptTypeValidationResult ValidateTypeName(string scriptTypeName, ScriptValidationSnapshot snapshot)
        {
            var result = new ScriptTypeValidationResult();

            string normalizedInput = (scriptTypeName ?? string.Empty).Trim();
            if (normalizedInput.Length == 0)
            {
                result.Message = "Script type cannot be empty.";
                return result;
            }

            Assembly scriptAssembly = snapshot != null ? snapshot.ScriptAssembly : null;
            if (scriptAssembly == null)
            {
                result.Message = "Script assembly is not loaded. Build scripts first.";
                return result;
            }

            bool ambiguous = false;
            Type scriptType = ResolveScriptType(scriptAssembly, normalizedInput, out string normalizedTypeName, out ambiguous);
            if (scriptType == null)
            {
                result.Message = ambiguous
                    ? "Script type name is ambiguous. Use full namespace + class name."
                    : "Script class was not found in the loaded assembly.";
                return result;
            }

            if (!scriptType.IsClass || scriptType.IsAbstract || scriptType.IsGenericTypeDefinition)
            {
                result.Message = "Script type must be a non-abstract class.";
                return result;
            }

            bool hasOnCreateName = HasMethodNamed(scriptType, "OnCreate");
            bool hasOnUpdateName = HasMethodNamed(scriptType, "OnUpdate");
            bool hasOnDestroyName = HasMethodNamed(scriptType, "OnDestroy");

            bool validOnCreate = HasMethodWithSignature(scriptType, "OnCreate", new Type[] { typeof(uint) });
            bool validOnUpdate = HasMethodWithSignature(scriptType, "OnUpdate", new Type[] { typeof(uint), typeof(float) });
            bool validOnDestroy = HasMethodWithSignature(scriptType, "OnDestroy", new Type[] { typeof(uint) });

            if (hasOnCreateName && !validOnCreate)
            {
                result.Message = "OnCreate must be: public void OnCreate(uint entityId), or inherit MonoBehaviour and override Start().";
                return result;
            }

            if (hasOnUpdateName && !validOnUpdate)
            {
                result.Message = "OnUpdate must be: public void OnUpdate(uint entityId, float deltaTime), or inherit MonoBehaviour and override Update().";
                return result;
            }

            if (hasOnDestroyName && !validOnDestroy)
            {
                result.Message = "OnDestroy must be: public void OnDestroy(uint entityId), or inherit MonoBehaviour and override OnDestroy().";
                return result;
            }

            if (!validOnCreate && !validOnUpdate && !validOnDestroy)
            {
                result.Message = "Script class must implement a valid lifecycle (OnCreate/OnUpdate/OnDestroy) or inherit MonoBehaviour and override Start/Update/OnDestroy.";
                return result;
            }

            result.IsValid = true;
            result.NormalizedTypeName = normalizedTypeName;
            result.Message = "OK";
            return result;
        }

        private static void UpdateScriptTypeCache(Assembly scriptAssembly, DateTime nowUtc)
        {
            const double refreshIntervalSeconds = 0.75;

            string assemblyIdentity = BuildAssemblyIdentity(scriptAssembly);
            bool assemblyChanged;
            bool refreshIntervalElapsed;

            lock (SyncRoot)
            {
                assemblyChanged = !string.Equals(_cachedScriptAssemblyIdentity, assemblyIdentity, StringComparison.Ordinal);
                refreshIntervalElapsed = (nowUtc - _scriptTypeCacheTimestampUtc).TotalSeconds >= refreshIntervalSeconds;

                if (!assemblyChanged && !refreshIntervalElapsed)
                    return;
            }

            string[] discoveredTypes = CollectScriptTypeNames(scriptAssembly);

            lock (SyncRoot)
            {
                _cachedScriptAssembly = scriptAssembly;
                _cachedScriptAssemblyIdentity = assemblyIdentity;
                _scriptTypeCacheTimestampUtc = nowUtc;
                _cachedRegisteredScriptTypes = discoveredTypes;
            }
        }

        private static void UpdateCompilationState(ScriptProjectInfo projectInfo, DateTime nowUtc)
        {
            if (projectInfo == null || string.IsNullOrEmpty(projectInfo.ScriptProjectPath) || !File.Exists(projectInfo.ScriptProjectPath))
            {
                lock (SyncRoot)
                {
                    _isBuildRunning = false;
                    _hasCompileErrors = false;
                    _pendingRuntimeReloadRequest = false;
                    _compileSummary = "Script project not found, compile check skipped.";
                    _compileErrorLines = new string[0];
                }
                return;
            }

            const double sourceScanIntervalSeconds = 0.75;
            const double buildDebounceSeconds = 0.5;
            const double buildCooldownSeconds = 1.0;

            bool shouldScanSources = false;
            bool shouldStartBuild = false;
            long sourceFingerprint = 0;

            lock (SyncRoot)
            {
                if (!string.Equals(_trackedProjectKey, projectInfo.ProjectKey, StringComparison.OrdinalIgnoreCase))
                {
                    _trackedProjectKey = projectInfo.ProjectKey;
                    _lastSourceScanUtc = DateTime.MinValue;
                    _lastSourceFingerprint = 0;
                    _lastSourceChangeUtc = DateTime.MinValue;
                    _lastBuildStartUtc = DateTime.MinValue;
                    _hasAttemptedBuild = false;
                    _isBuildRunning = false;
                    _hasCompileErrors = false;
                    _pendingRuntimeReloadRequest = false;
                    _compileSummary = "Script compilation has not run yet.";
                    _compileErrorLines = new string[0];
                }

                shouldScanSources = (nowUtc - _lastSourceScanUtc).TotalSeconds >= sourceScanIntervalSeconds;
            }

            if (shouldScanSources)
            {
                sourceFingerprint = ComputeSourceFingerprint(projectInfo);

                lock (SyncRoot)
                {
                    _lastSourceScanUtc = nowUtc;
                    if (!_hasAttemptedBuild)
                    {
                        _lastSourceFingerprint = sourceFingerprint;
                        _lastSourceChangeUtc = nowUtc;
                    }
                    else if (sourceFingerprint != _lastSourceFingerprint)
                    {
                        _lastSourceFingerprint = sourceFingerprint;
                        _lastSourceChangeUtc = nowUtc;
                    }
                }
            }

            lock (SyncRoot)
            {
                if (!_isBuildRunning)
                {
                    if (_immediateBuildRequested)
                    {
                        shouldStartBuild = true;
                        _isBuildRunning = true;
                        _hasAttemptedBuild = true;
                        _immediateBuildRequested = false;
                        _lastBuildStartUtc = nowUtc;
                        _compileSummary = "Compiling scripts...";
                        _compileErrorLines = new string[0];
                    }

                    if (!shouldStartBuild)
                    {
                        bool firstBuildNeeded = !_hasAttemptedBuild;
                        bool debounceElapsed = (nowUtc - _lastSourceChangeUtc).TotalSeconds >= buildDebounceSeconds;
                        bool cooldownElapsed = (nowUtc - _lastBuildStartUtc).TotalSeconds >= buildCooldownSeconds;
                        bool hasPendingSourceChange = _hasAttemptedBuild && _lastSourceChangeUtc > _lastBuildStartUtc;

                        if ((firstBuildNeeded || (hasPendingSourceChange && debounceElapsed)) && cooldownElapsed)
                        {
                            shouldStartBuild = true;
                            _isBuildRunning = true;
                            _hasAttemptedBuild = true;
                            _lastBuildStartUtc = nowUtc;
                            _compileSummary = "Compiling scripts...";
                            _compileErrorLines = new string[0];
                        }
                    }
                }
            }

            if (shouldStartBuild)
                StartBuildTask(projectInfo.ScriptProjectPath, projectInfo.ProjectRoot);
        }

        private static void StartBuildTask(string scriptProjectPath, string projectRoot)
        {
            System.Threading.Tasks.Task.Run(() => RunBuild(scriptProjectPath, projectRoot));
        }

        private static void RunBuild(string scriptProjectPath, string projectRoot)
        {
            int exitCode = -1;
            string combinedOutput;

            try
            {
                var startInfo = new ProcessStartInfo();
                startInfo.FileName = "dotnet";
                startInfo.Arguments = "build \"" + scriptProjectPath + "\" -c Debug -nologo -t:Rebuild";
                startInfo.WorkingDirectory = string.IsNullOrEmpty(projectRoot) ? Directory.GetCurrentDirectory() : projectRoot;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        throw new InvalidOperationException("Failed to start dotnet build process.");

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                    combinedOutput = (stdout ?? string.Empty) + "\n" + (stderr ?? string.Empty);
                }
            }
            catch (Exception ex)
            {
                combinedOutput = ex.Message;
            }

            string[] errorLines = ExtractBuildErrors(combinedOutput);
            bool hasErrors = exitCode != 0;

            string summary;
            if (exitCode == 0)
            {
                summary = "Compilation OK at " + DateTime.Now.ToString("HH:mm:ss") + ".";
            }
            else if (errorLines.Length > 0)
            {
                summary = "Compilation failed with " + errorLines.Length + " error(s).";
            }
            else
            {
                summary = "Compilation failed. Check script project output.";
                errorLines = new string[] { combinedOutput.Trim() };
            }

            lock (SyncRoot)
            {
                _isBuildRunning = false;
                _hasCompileErrors = hasErrors;
                if (!hasErrors)
                {
                    _pendingRuntimeReloadRequest = true;
                    _cachedScriptAssembly = null;
                    _cachedScriptAssemblyIdentity = string.Empty;
                    _cachedRegisteredScriptTypes = new string[0];
                    _scriptTypeCacheTimestampUtc = DateTime.MinValue;
                }
                _compileSummary = summary;
                _compileErrorLines = TrimErrorLines(errorLines, 6);

                if (hasErrors)
                {
                    _pendingCompileErrorConsoleLines.Add("[ScriptCompile] " + summary);
                    string[] linesToLog = TrimErrorLines(errorLines, 24);
                    for (int i = 0; i < linesToLog.Length; ++i)
                        _pendingCompileErrorConsoleLines.Add("[ScriptCompile] " + linesToLog[i]);
                }
            }
        }

        private static void DispatchPendingRuntimeReloadRequest()
        {
            bool shouldRequest;
            lock (SyncRoot)
            {
                shouldRequest = _pendingRuntimeReloadRequest;
                if (shouldRequest)
                    _pendingRuntimeReloadRequest = false;
            }

            if (!shouldRequest)
                return;

            try
            {
                EditorBridge.RequestScriptAssemblyReload();
            }
            catch
            {
                lock (SyncRoot)
                {
                    _pendingRuntimeReloadRequest = true;
                }
            }
        }

        private static void DispatchPendingCompileErrorLogs()
        {
            string[] lines;
            lock (SyncRoot)
            {
                if (_pendingCompileErrorConsoleLines.Count == 0)
                    return;

                lines = _pendingCompileErrorConsoleLines.ToArray();
                _pendingCompileErrorConsoleLines.Clear();
            }

            for (int i = 0; i < lines.Length; ++i)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    Engine.Debug.LogError(line);
                }
                catch
                {
                    lock (SyncRoot)
                    {
                        _pendingCompileErrorConsoleLines.Add(line);
                    }

                    break;
                }
            }
        }

        private static string[] ExtractBuildErrors(string output)
        {
            if (string.IsNullOrEmpty(output))
                return new string[0];

            string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var errors = new List<string>();

            for (int i = 0; i < lines.Length; ++i)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0)
                    continue;

                bool looksLikeError = trimmed.IndexOf(": error ", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      trimmed.IndexOf(" error CS", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!looksLikeError)
                    continue;

                errors.Add(trimmed);
            }

            return errors.ToArray();
        }

        private static string[] TrimErrorLines(string[] lines, int maxLines)
        {
            if (lines == null || lines.Length == 0)
                return new string[0];

            int count = lines.Length;
            if (count > maxLines)
                count = maxLines;

            var trimmed = new string[count];
            for (int i = 0; i < count; ++i)
                trimmed[i] = lines[i];

            return trimmed;
        }

        private static long ComputeSourceFingerprint(ScriptProjectInfo projectInfo)
        {
            var files = new List<string>();

            try
            {
                if (!string.IsNullOrEmpty(projectInfo.ScriptsRootPath) && Directory.Exists(projectInfo.ScriptsRootPath))
                {
                    string[] sourceFiles = Directory.GetFiles(projectInfo.ScriptsRootPath, "*.cs", SearchOption.AllDirectories);
                    for (int i = 0; i < sourceFiles.Length; ++i)
                    {
                        string candidate = sourceFiles[i];
                        if (IsIgnoredSourcePath(candidate))
                            continue;

                        files.Add(candidate);
                    }
                }

                if (!string.IsNullOrEmpty(projectInfo.ScriptProjectPath) && File.Exists(projectInfo.ScriptProjectPath))
                    files.Add(projectInfo.ScriptProjectPath);
            }
            catch
            {
                // Ignore IO errors and return the last known fingerprint behavior.
            }

            if (files.Count == 0)
                return 0;

            files.Sort(StringComparer.OrdinalIgnoreCase);

            long fingerprint = 17;
            for (int i = 0; i < files.Count; ++i)
            {
                string filePath = files[i];
                long ticks = 0;
                long fileLength = 0;
                int contentSignature = 0;

                try
                {
                    ticks = File.GetLastWriteTimeUtc(filePath).Ticks;
                }
                catch
                {
                    ticks = 0;
                }

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    fileLength = fileInfo.Exists ? fileInfo.Length : 0;
                }
                catch
                {
                    fileLength = 0;
                }

                contentSignature = ComputeFileContentSignature(filePath);

                fingerprint = unchecked(fingerprint * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(filePath));
                fingerprint = unchecked(fingerprint * 31 + ticks.GetHashCode());
                fingerprint = unchecked(fingerprint * 31 + fileLength.GetHashCode());
                fingerprint = unchecked(fingerprint * 31 + contentSignature);
            }

            return fingerprint;
        }

        private static int ComputeFileContentSignature(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return 0;

            const int maxBytes = 4096;
            const int fnvPrime = 16777619;
            const int fnvOffsetBasis = unchecked((int)2166136261);

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                int hash = fnvOffsetBasis;

                int length = bytes.Length;
                int headLength = length < maxBytes ? length : maxBytes / 2;
                int tailStart = length <= maxBytes ? headLength : length - (maxBytes - headLength);

                for (int i = 0; i < headLength; ++i)
                    hash = unchecked((hash ^ bytes[i]) * fnvPrime);

                for (int i = tailStart; i < length; ++i)
                    hash = unchecked((hash ^ bytes[i]) * fnvPrime);

                hash = unchecked((hash ^ length) * fnvPrime);
                return hash;
            }
            catch
            {
                return 0;
            }
        }

        private static bool IsIgnoredSourcePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return true;

            string normalized = filePath.Replace('\\', '/');
            return normalized.IndexOf("/obj/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("/bin/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("/.vs/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ScriptProjectInfo ResolveScriptProjectInfo()
        {
            string projectRoot = ProjectOperations.ActiveProjectPath;
            if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
                projectRoot = Directory.GetCurrentDirectory();

            var info = new ScriptProjectInfo();
            info.ProjectRoot = projectRoot;

            string projectJsonPath = Path.Combine(projectRoot, "project.json");
            string json = string.Empty;
            if (File.Exists(projectJsonPath))
            {
                try
                {
                    json = File.ReadAllText(projectJsonPath);
                }
                catch
                {
                    json = string.Empty;
                }
            }

            string scriptProjectRelative = ExtractJsonString(json, "scriptProject");
            if (!string.IsNullOrEmpty(scriptProjectRelative))
            {
                string candidate = Path.Combine(projectRoot, scriptProjectRelative);
                if (File.Exists(candidate))
                    info.ScriptProjectPath = Path.GetFullPath(candidate);
            }

            if (string.IsNullOrEmpty(info.ScriptProjectPath))
            {
                string workspaceScriptsProject = Path.Combine(Directory.GetCurrentDirectory(), "scripts", "GameScripts.csproj");
                if (File.Exists(workspaceScriptsProject))
                    info.ScriptProjectPath = Path.GetFullPath(workspaceScriptsProject);
            }

            if (string.IsNullOrEmpty(info.ScriptProjectPath))
            {
                string[] projectFiles = new string[0];
                try
                {
                    projectFiles = Directory.GetFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    projectFiles = new string[0];
                }

                if (projectFiles.Length > 0)
                    info.ScriptProjectPath = Path.GetFullPath(projectFiles[0]);
            }

            string scriptsRootRelative = ExtractJsonString(json, "scriptsRoot");
            if (!string.IsNullOrEmpty(scriptsRootRelative))
            {
                string candidate = Path.Combine(projectRoot, scriptsRootRelative);
                if (Directory.Exists(candidate))
                    info.ScriptsRootPath = Path.GetFullPath(candidate);
            }

            if (string.IsNullOrEmpty(info.ScriptsRootPath))
            {
                string scriptsUpper = Path.Combine(projectRoot, "Scripts");
                if (Directory.Exists(scriptsUpper))
                    info.ScriptsRootPath = scriptsUpper;
            }

            if (string.IsNullOrEmpty(info.ScriptsRootPath))
            {
                string scriptsLower = Path.Combine(projectRoot, "scripts");
                if (Directory.Exists(scriptsLower))
                    info.ScriptsRootPath = scriptsLower;
            }

            if (string.IsNullOrEmpty(info.ScriptsRootPath) && !string.IsNullOrEmpty(info.ScriptProjectPath))
                info.ScriptsRootPath = Path.GetDirectoryName(info.ScriptProjectPath);

            info.ProjectKey = (info.ProjectRoot ?? string.Empty) + "|" + (info.ScriptProjectPath ?? string.Empty);
            return info;
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
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

        private static Assembly FindLoadedScriptAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            Assembly selectedAssembly = null;

            for (int i = 0; i < assemblies.Length; ++i)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null || assembly.IsDynamic)
                    continue;

                Type scriptEntry = assembly.GetType("GameScripts.ScriptEntry", false);
                if (scriptEntry != null)
                    selectedAssembly = assembly;
            }

            return selectedAssembly;
        }

        private static string BuildAssemblyIdentity(Assembly assembly)
        {
            if (assembly == null)
                return string.Empty;

            string location;
            try
            {
                location = assembly.Location;
            }
            catch
            {
                location = string.Empty;
            }

            return assembly.FullName + "|" + location;
        }

        private static string[] CollectScriptTypeNames(Assembly scriptAssembly)
        {
            if (scriptAssembly == null)
                return new string[0];

            Type[] types = GetAssemblyTypes(scriptAssembly);
            Type scriptEntry = scriptAssembly.GetType("GameScripts.ScriptEntry", false);
            var discoveredNames = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < types.Length; ++i)
            {
                Type type = types[i];
                if (type == null || !type.IsClass || type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;

                if (scriptEntry != null && type == scriptEntry)
                    continue;

                if (!HasAnyValidLifecycleMethod(type))
                    continue;

                string fullName = type.FullName;
                if (string.IsNullOrEmpty(fullName) || !seenNames.Add(fullName))
                    continue;

                discoveredNames.Add(fullName);
            }

            discoveredNames.Sort(StringComparer.OrdinalIgnoreCase);
            return discoveredNames.ToArray();
        }

        private static Type ResolveScriptType(Assembly assembly, string typeName, out string normalizedTypeName, out bool ambiguous)
        {
            normalizedTypeName = string.Empty;
            ambiguous = false;

            if (assembly == null || string.IsNullOrEmpty(typeName))
                return null;

            Type[] allTypes = GetAssemblyTypes(assembly);

            if (typeName.IndexOf('.') >= 0)
            {
                Type exact = assembly.GetType(typeName, false);
                if (exact != null)
                {
                    normalizedTypeName = exact.FullName ?? typeName;
                    return exact;
                }

                for (int i = 0; i < allTypes.Length; ++i)
                {
                    Type candidate = allTypes[i];
                    if (candidate == null)
                        continue;

                    if (string.Equals(candidate.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedTypeName = candidate.FullName ?? typeName;
                        return candidate;
                    }
                }

                return null;
            }

            string gameScriptsName = "GameScripts." + typeName;
            Type gameScriptsType = assembly.GetType(gameScriptsName, false);
            if (gameScriptsType != null)
            {
                normalizedTypeName = gameScriptsType.FullName ?? gameScriptsName;
                return gameScriptsType;
            }

            var bySimpleName = new List<Type>();
            for (int i = 0; i < allTypes.Length; ++i)
            {
                Type candidate = allTypes[i];
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.Name, typeName, StringComparison.OrdinalIgnoreCase))
                    bySimpleName.Add(candidate);
            }

            if (bySimpleName.Count == 1)
            {
                Type resolved = bySimpleName[0];
                normalizedTypeName = resolved.FullName ?? typeName;
                return resolved;
            }

            if (bySimpleName.Count > 1)
                ambiguous = true;

            return null;
        }

        private static Type[] GetAssemblyTypes(Assembly assembly)
        {
            if (assembly == null)
                return new Type[0];

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types ?? new Type[0];
            }
            catch
            {
                return new Type[0];
            }
        }

        private static bool HasAnyValidLifecycleMethod(Type type)
        {
            return HasMethodWithSignature(type, "OnCreate", new Type[] { typeof(uint) }) ||
                   HasMethodWithSignature(type, "OnUpdate", new Type[] { typeof(uint), typeof(float) }) ||
                   HasMethodWithSignature(type, "OnDestroy", new Type[] { typeof(uint) });
        }

        private static bool HasMethodNamed(Type type, string methodName)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            for (int i = 0; i < methods.Length; ++i)
            {
                if (string.Equals(methods[i].Name, methodName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasMethodWithSignature(Type type, string methodName, Type[] parameterTypes)
        {
            MethodInfo method = type.GetMethod(methodName,
                                               BindingFlags.Instance | BindingFlags.Public,
                                               null,
                                               parameterTypes,
                                               null);
            if (method == null)
                return false;

            return method.ReturnType == typeof(void);
        }
    }
}
