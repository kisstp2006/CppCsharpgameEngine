using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EngineEditor
{
    internal enum AnimationTrackProperty
    {
        TransformPosition = 0,
        TransformRotation = 1,
        TransformScale = 2,
    }

    internal enum AnimationInterpolation
    {
        Linear = 0,
        Step = 1,
    }

    internal sealed class AnimationKeyframe
    {
        public float Time;
        public float X;
        public float Y;
        public AnimationInterpolation Interpolation = AnimationInterpolation.Linear;
    }

    internal sealed class AnimationTrack
    {
        public AnimationTrackProperty Property;
        public AnimationInterpolation DefaultInterpolation = AnimationInterpolation.Linear;
        public readonly List<AnimationKeyframe> Keys = new List<AnimationKeyframe>();

        public bool IsFloatTrack
        {
            get { return Property == AnimationTrackProperty.TransformRotation; }
        }
    }

    internal sealed class AnimationClipAsset
    {
        public int Version = 1;
        public float Duration = 1.0f;
        public bool Loop = true;
        public readonly List<AnimationTrack> Tracks = new List<AnimationTrack>();

        public static AnimationClipAsset CreateDefault()
        {
            var clip = new AnimationClipAsset();
            clip.Duration = 1.0f;
            clip.Loop = true;

            clip.Tracks.Add(new AnimationTrack { Property = AnimationTrackProperty.TransformPosition, DefaultInterpolation = AnimationInterpolation.Linear });
            clip.Tracks.Add(new AnimationTrack { Property = AnimationTrackProperty.TransformRotation, DefaultInterpolation = AnimationInterpolation.Linear });
            clip.Tracks.Add(new AnimationTrack { Property = AnimationTrackProperty.TransformScale, DefaultInterpolation = AnimationInterpolation.Linear });
            return clip;
        }

        public AnimationTrack GetOrCreateTrack(AnimationTrackProperty property)
        {
            for (int i = 0; i < Tracks.Count; ++i)
            {
                if (Tracks[i].Property == property)
                    return Tracks[i];
            }

            var created = new AnimationTrack();
            created.Property = property;
            created.DefaultInterpolation = AnimationInterpolation.Linear;
            Tracks.Add(created);
            return created;
        }

        public void SortAndClamp()
        {
            float maxKeyTime = 0.0f;
            for (int i = 0; i < Tracks.Count; ++i)
            {
                AnimationTrack track = Tracks[i];
                track.Keys.Sort((lhs, rhs) => lhs.Time.CompareTo(rhs.Time));

                if (track.Keys.Count > 0)
                {
                    float candidate = track.Keys[track.Keys.Count - 1].Time;
                    if (candidate > maxKeyTime)
                        maxKeyTime = candidate;
                }
            }

            if (Duration <= 0.0001f)
                Duration = maxKeyTime > 0.0001f ? maxKeyTime : 0.0001f;
        }

        public static bool TryLoad(string path, out AnimationClipAsset clip, out string error)
        {
            clip = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Animation load failed: path is empty.";
                return false;
            }

            if (!File.Exists(path))
            {
                error = "Animation load failed: file does not exist.";
                return false;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                error = "Animation load failed: " + ex.Message;
                return false;
            }

            var loaded = new AnimationClipAsset();
            AnimationTrack activeTrack = null;

            for (int lineIndex = 0; lineIndex < lines.Length; ++lineIndex)
            {
                string rawLine = lines[lineIndex] ?? string.Empty;
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] tokens = Tokenize(line);
                if (tokens.Length == 0)
                    continue;

                string command = tokens[0];
                int displayLine = lineIndex + 1;

                if (string.Equals(command, "anim_version", StringComparison.OrdinalIgnoreCase))
                {
                    if (tokens.Length < 2 || !int.TryParse(tokens[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out loaded.Version))
                    {
                        error = "Invalid anim_version at line " + displayLine + ".";
                        return false;
                    }

                    if (loaded.Version < 1)
                    {
                        error = "Unsupported anim_version at line " + displayLine + ".";
                        return false;
                    }

                    continue;
                }

                if (string.Equals(command, "duration", StringComparison.OrdinalIgnoreCase))
                {
                    if (tokens.Length < 2 || !TryParseFloat(tokens[1], out loaded.Duration))
                    {
                        error = "Invalid duration at line " + displayLine + ".";
                        return false;
                    }

                    continue;
                }

                if (string.Equals(command, "loop", StringComparison.OrdinalIgnoreCase))
                {
                    if (tokens.Length < 2 || !TryParseBool(tokens[1], out loaded.Loop))
                    {
                        error = "Invalid loop at line " + displayLine + ".";
                        return false;
                    }

                    continue;
                }

                if (string.Equals(command, "track", StringComparison.OrdinalIgnoreCase))
                {
                    if (activeTrack != null)
                    {
                        error = "Nested track is not allowed (line " + displayLine + ").";
                        return false;
                    }

                    if (tokens.Length < 2 || !TryParseTrackProperty(tokens[1], out AnimationTrackProperty property))
                    {
                        error = "Invalid track property at line " + displayLine + ".";
                        return false;
                    }

                    var created = new AnimationTrack();
                    created.Property = property;
                    created.DefaultInterpolation = AnimationInterpolation.Linear;

                    if (tokens.Length >= 3 && !TryParseInterpolation(tokens[2], out created.DefaultInterpolation))
                    {
                        error = "Invalid track interpolation at line " + displayLine + ".";
                        return false;
                    }

                    activeTrack = created;
                    continue;
                }

                if (string.Equals(command, "endtrack", StringComparison.OrdinalIgnoreCase))
                {
                    if (activeTrack == null)
                    {
                        error = "endtrack without track at line " + displayLine + ".";
                        return false;
                    }

                    activeTrack.Keys.Sort((lhs, rhs) => lhs.Time.CompareTo(rhs.Time));
                    loaded.Tracks.Add(activeTrack);
                    activeTrack = null;
                    continue;
                }

                if (string.Equals(command, "key", StringComparison.OrdinalIgnoreCase))
                {
                    if (activeTrack == null)
                    {
                        error = "key outside track at line " + displayLine + ".";
                        return false;
                    }

                    if (activeTrack.IsFloatTrack)
                    {
                        if (tokens.Length < 3)
                        {
                            error = "Rotation key expects key <time> <value> [interp] at line " + displayLine + ".";
                            return false;
                        }

                        if (!TryParseFloat(tokens[1], out float time) || !TryParseFloat(tokens[2], out float value))
                        {
                            error = "Invalid rotation key values at line " + displayLine + ".";
                            return false;
                        }

                        AnimationInterpolation interpolation = activeTrack.DefaultInterpolation;
                        if (tokens.Length >= 4 && !TryParseInterpolation(tokens[3], out interpolation))
                        {
                            error = "Invalid rotation key interpolation at line " + displayLine + ".";
                            return false;
                        }

                        activeTrack.Keys.Add(new AnimationKeyframe
                        {
                            Time = time,
                            X = value,
                            Y = 0.0f,
                            Interpolation = interpolation,
                        });
                    }
                    else
                    {
                        if (tokens.Length < 4)
                        {
                            error = "Vec2 key expects key <time> <x> <y> [interp] at line " + displayLine + ".";
                            return false;
                        }

                        if (!TryParseFloat(tokens[1], out float time)
                            || !TryParseFloat(tokens[2], out float x)
                            || !TryParseFloat(tokens[3], out float y))
                        {
                            error = "Invalid vec2 key values at line " + displayLine + ".";
                            return false;
                        }

                        AnimationInterpolation interpolation = activeTrack.DefaultInterpolation;
                        if (tokens.Length >= 5 && !TryParseInterpolation(tokens[4], out interpolation))
                        {
                            error = "Invalid vec2 key interpolation at line " + displayLine + ".";
                            return false;
                        }

                        activeTrack.Keys.Add(new AnimationKeyframe
                        {
                            Time = time,
                            X = x,
                            Y = y,
                            Interpolation = interpolation,
                        });
                    }

                    continue;
                }

                error = "Unknown command at line " + displayLine + ": " + command;
                return false;
            }

            if (activeTrack != null)
            {
                error = "Unclosed track at end of file.";
                return false;
            }

            loaded.SortAndClamp();
            clip = loaded;
            return true;
        }

        public static bool TrySave(string path, AnimationClipAsset clip, out string error)
        {
            error = string.Empty;
            if (clip == null)
            {
                error = "Animation save failed: clip is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Animation save failed: path is empty.";
                return false;
            }

            clip.SortAndClamp();

            try
            {
                string parentPath = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(parentPath))
                    Directory.CreateDirectory(parentPath);

                using (var writer = new StreamWriter(path, false))
                {
                    writer.WriteLine("anim_version 1");
                    writer.WriteLine("duration " + clip.Duration.ToString("0.######", CultureInfo.InvariantCulture));
                    writer.WriteLine("loop " + (clip.Loop ? "true" : "false"));
                    writer.WriteLine();

                    for (int i = 0; i < clip.Tracks.Count; ++i)
                    {
                        AnimationTrack track = clip.Tracks[i];
                        writer.WriteLine("track " + TrackPropertyToString(track.Property) + " " + InterpolationToString(track.DefaultInterpolation));

                        for (int k = 0; k < track.Keys.Count; ++k)
                        {
                            AnimationKeyframe key = track.Keys[k];
                            if (track.IsFloatTrack)
                            {
                                writer.WriteLine("key "
                                    + key.Time.ToString("0.######", CultureInfo.InvariantCulture) + " "
                                    + key.X.ToString("0.######", CultureInfo.InvariantCulture) + " "
                                    + InterpolationToString(key.Interpolation));
                            }
                            else
                            {
                                writer.WriteLine("key "
                                    + key.Time.ToString("0.######", CultureInfo.InvariantCulture) + " "
                                    + key.X.ToString("0.######", CultureInfo.InvariantCulture) + " "
                                    + key.Y.ToString("0.######", CultureInfo.InvariantCulture) + " "
                                    + InterpolationToString(key.Interpolation));
                            }
                        }

                        writer.WriteLine("endtrack");
                        writer.WriteLine();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Animation save failed: " + ex.Message;
                return false;
            }
        }

        public static string TrackPropertyToString(AnimationTrackProperty property)
        {
            switch (property)
            {
                case AnimationTrackProperty.TransformPosition:
                    return "Transform.Position";
                case AnimationTrackProperty.TransformRotation:
                    return "Transform.Rotation";
                case AnimationTrackProperty.TransformScale:
                    return "Transform.Scale";
                default:
                    return "Transform.Position";
            }
        }

        public static bool TryParseTrackProperty(string value, out AnimationTrackProperty property)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.Equals(normalized, "Transform.Position", StringComparison.OrdinalIgnoreCase))
            {
                property = AnimationTrackProperty.TransformPosition;
                return true;
            }

            if (string.Equals(normalized, "Transform.Rotation", StringComparison.OrdinalIgnoreCase))
            {
                property = AnimationTrackProperty.TransformRotation;
                return true;
            }

            if (string.Equals(normalized, "Transform.Scale", StringComparison.OrdinalIgnoreCase))
            {
                property = AnimationTrackProperty.TransformScale;
                return true;
            }

            property = AnimationTrackProperty.TransformPosition;
            return false;
        }

        public static string InterpolationToString(AnimationInterpolation interpolation)
        {
            return interpolation == AnimationInterpolation.Step ? "Step" : "Linear";
        }

        public static bool TryParseInterpolation(string value, out AnimationInterpolation interpolation)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.Equals(normalized, "Step", StringComparison.OrdinalIgnoreCase))
            {
                interpolation = AnimationInterpolation.Step;
                return true;
            }

            if (string.Equals(normalized, "Linear", StringComparison.OrdinalIgnoreCase))
            {
                interpolation = AnimationInterpolation.Linear;
                return true;
            }

            interpolation = AnimationInterpolation.Linear;
            return false;
        }

        private static string[] Tokenize(string line)
        {
            return (line ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool TryParseFloat(string token, out float value)
        {
            return float.TryParse(token,
                                  NumberStyles.Float,
                                  CultureInfo.InvariantCulture,
                                  out value)
                   && !float.IsNaN(value)
                   && !float.IsInfinity(value);
        }

        private static bool TryParseBool(string token, out bool value)
        {
            string normalized = (token ?? string.Empty).Trim();
            if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) || normalized == "1")
            {
                value = true;
                return true;
            }

            if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase) || normalized == "0")
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }
    }
}
