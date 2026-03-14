using System;
using System.Globalization;
using System.IO;

using Engine;

namespace EngineEditor
{
    internal static class AnimationTimelineWindow
    {
        private static string _loadedClipAbsolutePath = string.Empty;
        private static AnimationClipAsset _clip;
        private static bool _clipDirty;
        private static int _selectedTrackIndex = 0;
        private static int _selectedKeyIndex = -1;
        private static bool _draggingKey;
        private static float _pixelsPerSecond = 120.0f;
        private static string _clipPathInput = string.Empty;
        private static string _pendingOpenClipPath = string.Empty;
        private static bool _assetMenuRegistered;

        private const float MinPixelsPerSecond = 24.0f;
        private const float MaxPixelsPerSecond = 720.0f;
        private const float RulerHeight = 26.0f;
        private const float TrackRowHeight = 34.0f;
        private const float KeyPickRadius = 8.0f;

        public static void RegisterAssetContextMenu()
        {
            if (_assetMenuRegistered)
                return;

            _assetMenuRegistered = true;
            AssetPanelContextMenuRegistry.Register("animation.createClip",
                                                   "Create/Animation Clip",
                                                   context => CreateAnimationClipFromContext(context.ProjectPath, context.SelectedPath),
                                                   30);
        }

        public static void OpenClipFromPath(string clipPath)
        {
            if (string.IsNullOrWhiteSpace(clipPath))
                return;

            _pendingOpenClipPath = Path.GetFullPath(clipPath);
        }

        private static void CreateAnimationClipFromContext(string projectPath, string selectedPath)
        {
            string parentPath = ResolveCreateFolder(projectPath, selectedPath);
            Directory.CreateDirectory(parentPath);

            string candidate = Path.Combine(parentPath, "NewClip.anim");
            if (File.Exists(candidate))
            {
                int suffix = 1;
                while (true)
                {
                    string next = Path.Combine(parentPath, "NewClip_" + suffix + ".anim");
                    if (!File.Exists(next))
                    {
                        candidate = next;
                        break;
                    }

                    ++suffix;
                }
            }

            AnimationClipAsset defaultClip = AnimationClipAsset.CreateDefault();
            if (!AnimationClipAsset.TrySave(candidate, defaultClip, out string error))
            {
                ProjectOperations.SetStatusMessage(error);
                return;
            }

            ProjectOperations.SetStatusMessage("Created animation clip: " + Path.GetFileName(candidate));
            OpenClipFromPath(candidate);
        }

        private static string ResolveCreateFolder(string projectPath, string selectedPath)
        {
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                if (Directory.Exists(selectedPath))
                    return selectedPath;

                if (File.Exists(selectedPath))
                {
                    string parent = Path.GetDirectoryName(selectedPath);
                    if (!string.IsNullOrWhiteSpace(parent))
                        return parent;
                }
            }

            if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
            {
                string animationsPath = Path.Combine(projectPath, "Assets", "Animations");
                return animationsPath;
            }

            return Directory.GetCurrentDirectory();
        }

        public static void Draw(float deltaTime)
        {
            if (!ImGui.Begin("Animation Timeline"))
            {
                ImGui.End();
                return;
            }

            HandlePendingOpenPath();

            uint entityId = SceneEditor.SelectedEntityId >= 0 ? (uint)SceneEditor.SelectedEntityId : 0u;
            bool hasSelection = SceneEditor.SelectedEntityId >= 0 && EntityManager.IsEntityValid(entityId);

            if (!hasSelection)
            {
                ImGui.Text("Select an entity to edit animation keyframes.");
                DrawLooseClipToolbar();
                ImGui.End();
                return;
            }

            if (!EntityManager.HasAnimator(entityId))
            {
                ImGui.Text("Selected entity has no Animator component.");
                if (ImGui.Button("Add Animator"))
                {
                    EntityManager.AddAnimator(entityId);
                    ProjectOperations.SetStatusMessage("Animator component added.");
                }

                DrawLooseClipToolbar();
                ImGui.End();
                return;
            }

            DrawAnimatorToolbar(entityId, deltaTime);
            DrawTimelineBody(entityId);
            ImGui.End();
        }

        private static void DrawLooseClipToolbar()
        {
            ImGui.Separator();
            ImGui.Text("Clip Preview");
            if (string.IsNullOrWhiteSpace(_loadedClipAbsolutePath))
                ImGui.Text("No animation clip loaded.");
            else
                ImGui.Text("Loaded: " + Path.GetFileName(_loadedClipAbsolutePath));

            if (ImGui.Button("Open Clip From Disk"))
            {
                string initialPath = ProjectOperations.ActiveProjectPath;
                if (string.IsNullOrWhiteSpace(initialPath) || !Directory.Exists(initialPath))
                    initialPath = Directory.GetCurrentDirectory();

                string picked = Explorer.PickFile("Open animation clip (.anim)", initialPath);
                if (!string.IsNullOrWhiteSpace(picked))
                    OpenClipFromPath(picked);
            }
        }

        private static void DrawAnimatorToolbar(uint entityId, float deltaTime)
        {
            string clipPath = EntityManager.GetAnimatorClipPath(entityId) ?? string.Empty;
            if (!string.Equals(_clipPathInput, clipPath, StringComparison.Ordinal))
                _clipPathInput = clipPath;

            ImGui.Text("Entity: " + entityId + " (Animator)");
            ImGui.Separator();

            string updated = ImGui.InputText("Clip (.anim)", _clipPathInput ?? string.Empty);
            if (updated != null && !string.Equals(updated, _clipPathInput, StringComparison.Ordinal))
                _clipPathInput = updated;

            ImGui.SameLine();
            if (ImGui.Button("Apply Clip"))
            {
                string normalizedClipPath = NormalizeClipPathForProject(_clipPathInput);
                EntityManager.SetAnimatorClipPath(entityId, normalizedClipPath);
                _clipPathInput = normalizedClipPath;
                EnsureClipLoadedForPath(normalizedClipPath);
            }

            ImGui.SameLine();
            if (ImGui.Button("Browse"))
            {
                string initialPath = ResolveInitialPath(ProjectOperations.ActiveProjectPath);
                string picked = Explorer.PickFile("Select animation clip (.anim)", initialPath);
                if (!string.IsNullOrWhiteSpace(picked))
                {
                    string relative = ToProjectRelativePath(picked);
                    EntityManager.SetAnimatorClipPath(entityId, relative);
                    _clipPathInput = relative;
                    EnsureClipLoadedForPath(relative);
                }
            }

            ImGui.SameLine();
            if (ImGui.Button("New Clip"))
            {
                string createFolder = ResolveCreateFolder(ProjectOperations.ActiveProjectPath, ProjectOperations.ActiveProjectPath);
                Directory.CreateDirectory(createFolder);

                string candidate = Path.Combine(createFolder, "NewClip.anim");
                if (File.Exists(candidate))
                {
                    int suffix = 1;
                    while (true)
                    {
                        string next = Path.Combine(createFolder, "NewClip_" + suffix + ".anim");
                        if (!File.Exists(next))
                        {
                            candidate = next;
                            break;
                        }

                        ++suffix;
                    }
                }

                AnimationClipAsset created = AnimationClipAsset.CreateDefault();
                if (AnimationClipAsset.TrySave(candidate, created, out string saveError))
                {
                    string relativePath = ToProjectRelativePath(candidate);
                    EntityManager.SetAnimatorClipPath(entityId, relativePath);
                    _clipPathInput = relativePath;
                    _loadedClipAbsolutePath = Path.GetFullPath(candidate);
                    _clip = created;
                    _clipDirty = false;
                    _selectedTrackIndex = 0;
                    _selectedKeyIndex = -1;
                }
                else
                {
                    ProjectOperations.SetStatusMessage(saveError);
                }
            }

            clipPath = EntityManager.GetAnimatorClipPath(entityId) ?? string.Empty;
            EnsureClipLoadedForPath(clipPath);
            if (_clip == null)
            {
                ImGui.Text("No clip loaded. Assign or create a .anim clip.");
                return;
            }

            bool playing = EntityManager.GetAnimatorPlaying(entityId);
            float currentTime = EntityManager.GetAnimatorTime(entityId);
            bool loop = EntityManager.GetAnimatorLoop(entityId);
            float speed = EntityManager.GetAnimatorSpeed(entityId);
            bool applyPoseWhenStopped = EntityManager.GetAnimatorApplyPoseWhenStopped(entityId);

            if (playing)
            {
                currentTime += deltaTime * speed;
                float duration = Math.Max(0.0001f, _clip.Duration);
                if (loop)
                {
                    currentTime %= duration;
                    if (currentTime < 0.0f)
                        currentTime += duration;
                }
                else
                {
                    if (currentTime < 0.0f)
                        currentTime = 0.0f;
                    if (currentTime > duration)
                        currentTime = duration;
                }

                EntityManager.SetAnimatorTime(entityId, currentTime);
            }

            if (ImGui.Button(playing ? "Pause" : "Play"))
            {
                EntityManager.SetAnimatorPlaying(entityId, !playing);
                playing = !playing;
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                EntityManager.SetAnimatorPlaying(entityId, false);
                EntityManager.SetAnimatorTime(entityId, 0.0f);
                currentTime = 0.0f;
                playing = false;
            }

            ImGui.SameLine();
            if (ImGui.Button("Save Clip"))
                SaveLoadedClip();

            ImGui.SameLine();
            if (ImGui.Button("Reload Clip"))
                EnsureClipLoadedForPath(clipPath, true);

            ImGui.SameLine();
            if (ImGui.Button("+ Key"))
                AddKeyAtCurrentTime(entityId);

            ImGui.SameLine();
            if (ImGui.Button("Delete Key"))
                DeleteSelectedKey();

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100.0f);
            if (ImGui.InputFloat("Speed", ref speed, 0.05f))
                EntityManager.SetAnimatorSpeed(entityId, speed);

            ImGui.SameLine();
            if (ImGui.Checkbox("Loop", ref loop))
                EntityManager.SetAnimatorLoop(entityId, loop);

            ImGui.SameLine();
            if (ImGui.Checkbox("Preview Pose", ref applyPoseWhenStopped))
                EntityManager.SetAnimatorApplyPoseWhenStopped(entityId, applyPoseWhenStopped);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(100.0f);
            if (ImGui.InputFloat("Zoom", ref _pixelsPerSecond, 4.0f))
            {
                if (_pixelsPerSecond < MinPixelsPerSecond)
                    _pixelsPerSecond = MinPixelsPerSecond;
                if (_pixelsPerSecond > MaxPixelsPerSecond)
                    _pixelsPerSecond = MaxPixelsPerSecond;
            }

            string dirtyFlag = _clipDirty ? "*" : string.Empty;
            ImGui.Text("Clip: " + Path.GetFileName(_loadedClipAbsolutePath) + dirtyFlag + "  Time: " + currentTime.ToString("0.###", CultureInfo.InvariantCulture));

            if (_selectedTrackIndex >= 0 && _selectedTrackIndex < _clip.Tracks.Count)
            {
                AnimationTrack selectedTrack = _clip.Tracks[_selectedTrackIndex];
                if (_selectedKeyIndex >= 0 && _selectedKeyIndex < selectedTrack.Keys.Count)
                {
                    AnimationKeyframe selectedKey = selectedTrack.Keys[_selectedKeyIndex];

                    ImGui.Separator();
                    ImGui.Text("Selected Keyframe");

                    float keyTime = selectedKey.Time;
                    if (ImGui.InputFloat("Key Time", ref keyTime, 0.01f))
                    {
                        if (keyTime < 0.0f)
                            keyTime = 0.0f;

                        selectedKey.Time = keyTime;
                        selectedTrack.Keys[_selectedKeyIndex] = selectedKey;
                        selectedTrack.Keys.Sort((lhs, rhs) => lhs.Time.CompareTo(rhs.Time));
                        _selectedKeyIndex = FindNearestKeyIndex(selectedTrack, keyTime);
                        EntityManager.SetAnimatorTime(entityId, keyTime);
                        EntityManager.SetAnimatorPlaying(entityId, false);
                        _clipDirty = true;
                    }

                    if (selectedTrack.IsFloatTrack)
                    {
                        float value = selectedKey.X;
                        if (ImGui.InputFloat("Value", ref value, 0.1f))
                        {
                            selectedKey.X = value;
                            selectedTrack.Keys[_selectedKeyIndex] = selectedKey;
                            _clipDirty = true;
                        }
                    }
                    else
                    {
                        float valueX = selectedKey.X;
                        float valueY = selectedKey.Y;
                        if (ImGui.InputFloat("Value X", ref valueX, 0.1f))
                        {
                            selectedKey.X = valueX;
                            selectedTrack.Keys[_selectedKeyIndex] = selectedKey;
                            _clipDirty = true;
                        }

                        if (ImGui.InputFloat("Value Y", ref valueY, 0.1f))
                        {
                            selectedKey.Y = valueY;
                            selectedTrack.Keys[_selectedKeyIndex] = selectedKey;
                            _clipDirty = true;
                        }
                    }

                    bool keyLinear = selectedKey.Interpolation == AnimationInterpolation.Linear;
                    if (ImGui.SelectableNoClose("Linear##KeyInterp", keyLinear))
                    {
                        selectedKey.Interpolation = AnimationInterpolation.Linear;
                        selectedTrack.Keys[_selectedKeyIndex] = selectedKey;
                        _clipDirty = true;
                    }

                    bool keyStep = selectedKey.Interpolation == AnimationInterpolation.Step;
                    if (ImGui.SelectableNoClose("Step##KeyInterp", keyStep))
                    {
                        selectedKey.Interpolation = AnimationInterpolation.Step;
                        selectedTrack.Keys[_selectedKeyIndex] = selectedKey;
                        _clipDirty = true;
                    }
                }
            }
        }

        private static void DrawTimelineBody(uint entityId)
        {
            if (_clip == null)
                return;

            float totalWidth = ImGui.GetContentRegionAvailX();
            float totalHeight = ImGui.GetContentRegionAvailY();
            if (totalWidth < 100.0f)
                totalWidth = 100.0f;
            if (totalHeight < 180.0f)
                totalHeight = 180.0f;

            const float leftPanelWidth = 220.0f;
            float rightPanelWidth = totalWidth - leftPanelWidth - 8.0f;
            if (rightPanelWidth < 220.0f)
                rightPanelWidth = 220.0f;

            if (ImGui.BeginChild("##TimelineTracks", leftPanelWidth, totalHeight, true))
            {
                for (int trackIndex = 0; trackIndex < _clip.Tracks.Count; ++trackIndex)
                {
                    AnimationTrack track = _clip.Tracks[trackIndex];
                    string label = AnimationClipAsset.TrackPropertyToString(track.Property)
                                   + " (" + track.Keys.Count + ")";
                    bool selected = trackIndex == _selectedTrackIndex;
                    if (ImGui.Selectable(label + "##TimelineTrack" + trackIndex, selected))
                    {
                        _selectedTrackIndex = trackIndex;
                        if (_selectedKeyIndex >= track.Keys.Count)
                            _selectedKeyIndex = -1;
                    }
                }
            }

            ImGui.EndChild();
            ImGui.SameLine();

            if (!ImGui.BeginChild("##TimelineCanvas", rightPanelWidth, totalHeight, true))
            {
                ImGui.EndChild();
                return;
            }

            float canvasX = ImGui.GetCursorScreenPosX();
            float canvasY = ImGui.GetCursorScreenPosY();
            float canvasWidth = rightPanelWidth;
            float canvasHeight = totalHeight;

            ImGui.InvisibleButton("##TimelineCanvasCapture", canvasWidth, canvasHeight);
            bool canvasHovered = ImGui.IsItemHovered();

            float duration = Math.Max(0.0001f, _clip.Duration);
            float pixelsPerSecond = Math.Max(MinPixelsPerSecond, _pixelsPerSecond);
            float visibleDuration = Math.Max(duration, canvasWidth / pixelsPerSecond);

            DrawTimelineGrid(canvasX, canvasY, canvasWidth, canvasHeight, duration, visibleDuration);
            DrawTimelineTracks(canvasX, canvasY + RulerHeight, canvasWidth, canvasHeight - RulerHeight, duration);

            float time = EntityManager.GetAnimatorTime(entityId);
            float cursorX = canvasX + ((time / duration) * canvasWidth);
            ImGui.DrawLine(cursorX, canvasY, cursorX, canvasY + canvasHeight, 1.0f, 1.0f, 1.0f, 0.95f, 1.5f);

            HandleTimelineMouse(entityId, canvasHovered, canvasX, canvasY, canvasWidth, canvasHeight, duration);

            ImGui.EndChild();
        }

        private static void DrawTimelineGrid(float canvasX,
                                             float canvasY,
                                             float canvasWidth,
                                             float canvasHeight,
                                             float duration,
                                             float visibleDuration)
        {
            ImGui.DrawRect(canvasX, canvasY, canvasWidth, canvasHeight, 0.28f, 0.28f, 0.30f, 1.0f, 1.0f);
            ImGui.DrawRect(canvasX, canvasY, canvasWidth, RulerHeight, 0.20f, 0.22f, 0.28f, 1.0f, 1.0f);

            int verticalLines = (int)Math.Max(4.0f, visibleDuration);
            for (int i = 0; i <= verticalLines; ++i)
            {
                float t = i / (float)verticalLines;
                float x = canvasX + (t * canvasWidth);
                float alpha = (i % 5 == 0) ? 0.50f : 0.22f;
                ImGui.DrawLine(x, canvasY, x, canvasY + canvasHeight, 0.45f, 0.45f, 0.48f, alpha, 1.0f);
            }
        }

        private static void DrawTimelineTracks(float startX, float startY, float width, float height, float duration)
        {
            if (_clip == null)
                return;

            for (int trackIndex = 0; trackIndex < _clip.Tracks.Count; ++trackIndex)
            {
                float rowY = startY + (trackIndex * TrackRowHeight);
                if (rowY > startY + height)
                    break;

                bool selectedTrack = trackIndex == _selectedTrackIndex;
                float rowAlpha = selectedTrack ? 0.42f : 0.20f;
                ImGui.DrawRect(startX, rowY, width, TrackRowHeight, 0.26f, 0.26f, 0.30f, rowAlpha, 1.0f);

                AnimationTrack track = _clip.Tracks[trackIndex];
                float centerY = rowY + (TrackRowHeight * 0.5f);
                ImGui.DrawLine(startX, centerY, startX + width, centerY, 0.34f, 0.34f, 0.38f, 0.85f, 1.0f);

                for (int keyIndex = 0; keyIndex < track.Keys.Count; ++keyIndex)
                {
                    AnimationKeyframe key = track.Keys[keyIndex];
                    float normalizedTime = duration > 0.0001f ? (key.Time / duration) : 0.0f;
                    if (normalizedTime < 0.0f)
                        normalizedTime = 0.0f;
                    if (normalizedTime > 1.0f)
                        normalizedTime = 1.0f;

                    float keyX = startX + (normalizedTime * width);
                    bool selected = selectedTrack && keyIndex == _selectedKeyIndex;

                    float r = selected ? 1.0f : 0.85f;
                    float g = selected ? 0.86f : 0.84f;
                    float b = selected ? 0.24f : 0.86f;
                    float a = selected ? 1.0f : 0.92f;

                    ImGui.DrawLine(keyX - 4.0f, centerY, keyX + 4.0f, centerY, r, g, b, a, 2.0f);
                    ImGui.DrawLine(keyX, centerY - 4.0f, keyX, centerY + 4.0f, r, g, b, a, 2.0f);
                }
            }
        }

        private static void HandleTimelineMouse(uint entityId,
                                                bool hovered,
                                                float canvasX,
                                                float canvasY,
                                                float canvasWidth,
                                                float canvasHeight,
                                                float duration)
        {
            if (_clip == null)
                return;

            float mouseX = Input.GetMousePosX();
            float mouseY = Input.GetMousePosY();

            bool leftPressed = Input.GetMouseButtonDown(MouseButton.Left);
            bool leftHeld = Input.GetMouseButton(MouseButton.Left);
            bool leftReleased = Input.GetMouseButtonUp(MouseButton.Left);

            if (leftPressed && hovered)
            {
                if (mouseY <= canvasY + RulerHeight)
                {
                    float normalized = (mouseX - canvasX) / Math.Max(1.0f, canvasWidth);
                    if (normalized < 0.0f)
                        normalized = 0.0f;
                    if (normalized > 1.0f)
                        normalized = 1.0f;

                    float newTime = duration * normalized;
                    EntityManager.SetAnimatorPlaying(entityId, false);
                    EntityManager.SetAnimatorTime(entityId, newTime);
                    return;
                }

                int hitTrackIndex = -1;
                int hitKeyIndex = -1;
                float bestDistance = float.MaxValue;

                for (int trackIndex = 0; trackIndex < _clip.Tracks.Count; ++trackIndex)
                {
                    float rowY = canvasY + RulerHeight + (trackIndex * TrackRowHeight);
                    float rowBottom = rowY + TrackRowHeight;
                    if (mouseY < rowY || mouseY > rowBottom)
                        continue;

                    hitTrackIndex = trackIndex;
                    AnimationTrack track = _clip.Tracks[trackIndex];
                    float centerY = rowY + (TrackRowHeight * 0.5f);

                    for (int keyIndex = 0; keyIndex < track.Keys.Count; ++keyIndex)
                    {
                        AnimationKeyframe key = track.Keys[keyIndex];
                        float normalizedTime = duration > 0.0001f ? (key.Time / duration) : 0.0f;
                        if (normalizedTime < 0.0f)
                            normalizedTime = 0.0f;
                        if (normalizedTime > 1.0f)
                            normalizedTime = 1.0f;

                        float keyX = canvasX + (normalizedTime * canvasWidth);
                        float dx = keyX - mouseX;
                        float dy = centerY - mouseY;
                        float distanceSq = (dx * dx) + (dy * dy);
                        if (distanceSq <= (KeyPickRadius * KeyPickRadius) && distanceSq < bestDistance)
                        {
                            bestDistance = distanceSq;
                            hitKeyIndex = keyIndex;
                        }
                    }
                }

                if (hitTrackIndex >= 0)
                {
                    _selectedTrackIndex = hitTrackIndex;
                    _selectedKeyIndex = hitKeyIndex;
                    _draggingKey = hitKeyIndex >= 0;

                    if (_selectedKeyIndex >= 0)
                    {
                        AnimationTrack track = _clip.Tracks[_selectedTrackIndex];
                        AnimationKeyframe key = track.Keys[_selectedKeyIndex];
                        EntityManager.SetAnimatorPlaying(entityId, false);
                        EntityManager.SetAnimatorTime(entityId, key.Time);
                    }
                }
            }

            if (_draggingKey && leftHeld && _selectedTrackIndex >= 0 && _selectedTrackIndex < _clip.Tracks.Count && _selectedKeyIndex >= 0)
            {
                AnimationTrack track = _clip.Tracks[_selectedTrackIndex];
                if (_selectedKeyIndex < track.Keys.Count)
                {
                    float normalized = (mouseX - canvasX) / Math.Max(1.0f, canvasWidth);
                    if (normalized < 0.0f)
                        normalized = 0.0f;
                    if (normalized > 1.0f)
                        normalized = 1.0f;

                    float newTime = duration * normalized;
                    track.Keys[_selectedKeyIndex].Time = newTime;
                    track.Keys.Sort((lhs, rhs) => lhs.Time.CompareTo(rhs.Time));

                    int newIndex = FindNearestKeyIndex(track, newTime);
                    _selectedKeyIndex = newIndex;
                    _clipDirty = true;

                    EntityManager.SetAnimatorPlaying(entityId, false);
                    EntityManager.SetAnimatorTime(entityId, newTime);
                }
            }

            if (leftReleased)
                _draggingKey = false;
        }

        private static void AddKeyAtCurrentTime(uint entityId)
        {
            if (_clip == null || _selectedTrackIndex < 0 || _selectedTrackIndex >= _clip.Tracks.Count)
                return;

            AnimationTrack track = _clip.Tracks[_selectedTrackIndex];
            float time = EntityManager.GetAnimatorTime(entityId);
            float x = 0.0f;
            float y = 0.0f;

            switch (track.Property)
            {
                case AnimationTrackProperty.TransformPosition:
                    if (!EntityManager.GetTransform(entityId, out x, out y, out float ignoredWidth, out float ignoredHeight))
                        return;

                    _ = ignoredWidth;
                    _ = ignoredHeight;
                    break;
                case AnimationTrackProperty.TransformRotation:
                    x = EntityManager.GetTransformRotation(entityId);
                    y = 0.0f;
                    break;
                case AnimationTrackProperty.TransformScale:
                    if (!EntityManager.GetTransform(entityId, out float ignoredX, out float ignoredY, out x, out y))
                        return;

                    _ = ignoredX;
                    _ = ignoredY;
                    break;
            }

            var key = new AnimationKeyframe();
            key.Time = time;
            key.X = x;
            key.Y = y;
            key.Interpolation = track.DefaultInterpolation;

            track.Keys.Add(key);
            track.Keys.Sort((lhs, rhs) => lhs.Time.CompareTo(rhs.Time));
            _selectedKeyIndex = FindNearestKeyIndex(track, time);
            _clipDirty = true;
        }

        private static void DeleteSelectedKey()
        {
            if (_clip == null || _selectedTrackIndex < 0 || _selectedTrackIndex >= _clip.Tracks.Count || _selectedKeyIndex < 0)
                return;

            AnimationTrack track = _clip.Tracks[_selectedTrackIndex];
            if (_selectedKeyIndex >= track.Keys.Count)
                return;

            track.Keys.RemoveAt(_selectedKeyIndex);
            if (_selectedKeyIndex >= track.Keys.Count)
                _selectedKeyIndex = track.Keys.Count - 1;

            _clipDirty = true;
        }

        private static int FindNearestKeyIndex(AnimationTrack track, float time)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < track.Keys.Count; ++i)
            {
                float distance = Math.Abs(track.Keys[i].Time - time);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }

        private static void SaveLoadedClip()
        {
            if (_clip == null || string.IsNullOrWhiteSpace(_loadedClipAbsolutePath))
                return;

            if (!AnimationClipAsset.TrySave(_loadedClipAbsolutePath, _clip, out string error))
            {
                ProjectOperations.SetStatusMessage(error);
                return;
            }

            _clipDirty = false;
            ProjectOperations.SetStatusMessage("Saved animation clip: " + Path.GetFileName(_loadedClipAbsolutePath));
        }

        private static void HandlePendingOpenPath()
        {
            if (string.IsNullOrWhiteSpace(_pendingOpenClipPath))
                return;

            string pending = _pendingOpenClipPath;
            _pendingOpenClipPath = string.Empty;
            EnsureClipLoadedFromAbsolutePath(pending, true);
        }

        private static void EnsureClipLoadedForPath(string clipPath, bool forceReload = false)
        {
            string absolute = ResolveClipAbsolutePath(clipPath);
            if (string.IsNullOrWhiteSpace(absolute))
                return;

            EnsureClipLoadedFromAbsolutePath(absolute, forceReload);
        }

        private static void EnsureClipLoadedFromAbsolutePath(string absolutePath, bool forceReload)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
                return;

            string normalized = Path.GetFullPath(absolutePath);
            if (!forceReload && string.Equals(_loadedClipAbsolutePath, normalized, StringComparison.OrdinalIgnoreCase) && _clip != null)
                return;

            if (!AnimationClipAsset.TryLoad(normalized, out AnimationClipAsset loaded, out string error))
            {
                _clip = null;
                _loadedClipAbsolutePath = string.Empty;
                _selectedTrackIndex = 0;
                _selectedKeyIndex = -1;
                _clipDirty = false;
                if (!string.IsNullOrWhiteSpace(error))
                    ProjectOperations.SetStatusMessage(error);
                return;
            }

            _clip = loaded;
            _loadedClipAbsolutePath = normalized;
            _selectedTrackIndex = 0;
            _selectedKeyIndex = -1;
            _clipDirty = false;
            ProjectOperations.SetStatusMessage("Loaded animation clip: " + Path.GetFileName(normalized));
        }

        private static string ResolveClipAbsolutePath(string clipPath)
        {
            if (string.IsNullOrWhiteSpace(clipPath))
                return string.Empty;

            string trimmed = clipPath.Trim();
            if (Path.IsPathRooted(trimmed))
                return Path.GetFullPath(trimmed);

            string projectPath = ProjectOperations.ActiveProjectPath;
            if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
                return Path.GetFullPath(Path.Combine(projectPath, trimmed));

            return Path.GetFullPath(trimmed);
        }

        private static string NormalizeClipPathForProject(string clipPath)
        {
            string absolute = ResolveClipAbsolutePath(clipPath);
            if (string.IsNullOrWhiteSpace(absolute))
                return string.Empty;

            return ToProjectRelativePath(absolute);
        }

        private static string ToProjectRelativePath(string absolutePath)
        {
            string projectPath = ProjectOperations.ActiveProjectPath;
            if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
            {
                string relative = StringUtilities.MakeRelativePath(projectPath, absolutePath);
                if (!string.IsNullOrWhiteSpace(relative) && !relative.StartsWith("..", StringComparison.Ordinal))
                    return relative.Replace('\\', '/');
            }

            return Path.GetFullPath(absolutePath).Replace('\\', '/');
        }

        private static string ResolveInitialPath(string projectPath)
        {
            if (!string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath))
                return projectPath;

            return Directory.GetCurrentDirectory();
        }

        public static bool TryManipulateSelectedKeyframeWithGizmo(uint entityId,
                                                                  float viewportX,
                                                                  float viewportY,
                                                                  float viewportWidth,
                                                                  float viewportHeight,
                                                                  float cameraX,
                                                                  float cameraY,
                                                                  float cameraZoom,
                                                                  bool snapEnabled,
                                                                  float snapStep,
                                                                  ref float x,
                                                                  ref float y,
                                                                  ref float width,
                                                                  ref float height,
                                                                  ref float rotationDegrees)
        {
            if (_clip == null)
                return false;

            if (_selectedTrackIndex < 0 || _selectedTrackIndex >= _clip.Tracks.Count || _selectedKeyIndex < 0)
                return false;

            if (SceneEditor.SelectedEntityId < 0 || (uint)SceneEditor.SelectedEntityId != entityId)
                return false;

            AnimationTrack track = _clip.Tracks[_selectedTrackIndex];
            if (_selectedKeyIndex >= track.Keys.Count)
                return false;

            AnimationKeyframe key = track.Keys[_selectedKeyIndex];
            bool changed = false;

            if (track.Property == AnimationTrackProperty.TransformPosition)
            {
                float gizmoX = x;
                float gizmoY = y;
                changed = ImGuizmo.Manipulate2DTranslate(viewportX,
                                                         viewportY,
                                                         viewportWidth,
                                                         viewportHeight,
                                                         cameraX,
                                                         cameraY,
                                                         cameraZoom,
                                                         ref gizmoX,
                                                         ref gizmoY,
                                                         width,
                                                         height);

                if (changed)
                {
                    if (snapEnabled)
                    {
                        gizmoX = SnapValue(gizmoX, snapStep);
                        gizmoY = SnapValue(gizmoY, snapStep);
                    }

                    x = gizmoX;
                    y = gizmoY;
                    key.X = gizmoX;
                    key.Y = gizmoY;
                }
            }
            else if (track.Property == AnimationTrackProperty.TransformRotation)
            {
                float gizmoX = x;
                float gizmoY = y;
                float gizmoRotation = rotationDegrees;
                changed = ImGuizmo.Manipulate2DRotate(viewportX,
                                                      viewportY,
                                                      viewportWidth,
                                                      viewportHeight,
                                                      cameraX,
                                                      cameraY,
                                                      cameraZoom,
                                                      ref gizmoX,
                                                      ref gizmoY,
                                                      ref gizmoRotation,
                                                      width,
                                                      height);
                if (changed)
                {
                    x = gizmoX;
                    y = gizmoY;
                    rotationDegrees = gizmoRotation;
                    key.X = gizmoRotation;
                }
            }
            else if (track.Property == AnimationTrackProperty.TransformScale)
            {
                float gizmoX = x;
                float gizmoY = y;
                float gizmoWidth = width;
                float gizmoHeight = height;
                changed = ImGuizmo.Manipulate2DScale(viewportX,
                                                     viewportY,
                                                     viewportWidth,
                                                     viewportHeight,
                                                     cameraX,
                                                     cameraY,
                                                     cameraZoom,
                                                     ref gizmoX,
                                                     ref gizmoY,
                                                     ref gizmoWidth,
                                                     ref gizmoHeight,
                                                     rotationDegrees);
                if (changed)
                {
                    if (snapEnabled)
                    {
                        gizmoWidth = Math.Max(0.0001f, SnapValue(gizmoWidth, snapStep));
                        gizmoHeight = Math.Max(0.0001f, SnapValue(gizmoHeight, snapStep));
                    }

                    x = gizmoX;
                    y = gizmoY;
                    width = gizmoWidth;
                    height = gizmoHeight;
                    key.X = gizmoWidth;
                    key.Y = gizmoHeight;
                }
            }

            if (changed)
            {
                track.Keys[_selectedKeyIndex] = key;
                EntityManager.SetAnimatorPlaying(entityId, false);
                EntityManager.SetAnimatorTime(entityId, key.Time);
                _clipDirty = true;
            }

            return true;
        }

        private static float SnapValue(float value, float step)
        {
            if (step <= 0.0001f)
                return value;

            float scaled = value / step;
            if (scaled >= 0.0f)
                return (float)Math.Floor(scaled + 0.5f) * step;

            return (float)Math.Ceiling(scaled - 0.5f) * step;
        }
    }
}
