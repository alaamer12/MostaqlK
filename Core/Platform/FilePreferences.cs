using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace MostaqlK.Core.Platform;

/// <summary>
/// Cross-platform, file-backed durable implementation of <see cref="IPreferences"/>.
/// Persists preferences to a structured JSON file (<c>preferences.dat</c>) in the application settings directory,
/// resolving unpackaged desktop execution limitations where Windows <c>ApplicationData.Current</c> is unavailable.
/// </summary>
public sealed class FilePreferences : IPreferences
{
    private static FilePreferences? _defaultInstance;
    private static readonly object StaticLock = new();

    private readonly string _filePath;
    private readonly object _syncLock = new();
    private readonly Dictionary<string, Dictionary<string, object?>> _containers = new(StringComparer.Ordinal);
    private bool _loaded;

    public static FilePreferences Default
    {
        get
        {
            if (_defaultInstance == null)
            {
                lock (StaticLock)
                {
                    _defaultInstance ??= new FilePreferences();
                }
            }
            return _defaultInstance;
        }
    }

    public FilePreferences(string? customFilePath = null)
    {
        _filePath = customFilePath ?? AppPaths.PreferencesFilePath;
    }

    /// <summary>
    /// Installs <see cref="FilePreferences"/> as the default backing provider for <see cref="Preferences.Default"/>.
    /// </summary>
    public static void Install(string? customFilePath = null)
    {
        var instance = customFilePath != null ? new FilePreferences(customFilePath) : Default;

        try
        {
            var setDefaultMethod = typeof(Preferences).GetMethod(
                "SetDefault",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

            if (setDefaultMethod != null)
            {
                setDefaultMethod.Invoke(null, new object?[] { instance });
                return;
            }

            var field = typeof(Preferences).GetField("defaultImplementation", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? typeof(Preferences).GetField("s_defaultImplementation", BindingFlags.NonPublic | BindingFlags.Static)
                     ?? typeof(Preferences).GetField("_defaultImplementation", BindingFlags.NonPublic | BindingFlags.Static);

            field?.SetValue(null, instance);
        }
        catch
        {
            // Best effort fallback
        }
    }

    public bool ContainsKey(string key, string? sharedName = null)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        lock (_syncLock)
        {
            EnsureLoaded();
            var containerKey = sharedName ?? string.Empty;
            return _containers.TryGetValue(containerKey, out var dict) && dict.ContainsKey(key);
        }
    }

    public void Remove(string key, string? sharedName = null)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        lock (_syncLock)
        {
            EnsureLoaded();
            var containerKey = sharedName ?? string.Empty;
            if (_containers.TryGetValue(containerKey, out var dict) && dict.Remove(key))
            {
                SaveToFile();
            }
        }
    }

    public void Clear(string? sharedName = null)
    {
        lock (_syncLock)
        {
            EnsureLoaded();
            var containerKey = sharedName ?? string.Empty;
            if (_containers.TryGetValue(containerKey, out var dict))
            {
                dict.Clear();
                SaveToFile();
            }
        }
    }

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        lock (_syncLock)
        {
            EnsureLoaded();
            var containerKey = sharedName ?? string.Empty;
            if (!_containers.TryGetValue(containerKey, out var dict))
            {
                dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                _containers[containerKey] = dict;
            }

            if (value is null)
            {
                dict.Remove(key);
            }
            else
            {
                dict[key] = value;
            }

            SaveToFile();
        }
    }

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        lock (_syncLock)
        {
            EnsureLoaded();
            var containerKey = sharedName ?? string.Empty;
            if (!_containers.TryGetValue(containerKey, out var dict) || !dict.TryGetValue(key, out var rawValue) || rawValue == null)
            {
                return defaultValue;
            }

            return ConvertValue<T>(rawValue, defaultValue);
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;

        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var containerProp in doc.RootElement.EnumerateObject())
                        {
                            var containerName = containerProp.Name;
                            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
                            _containers[containerName] = dict;

                            if (containerProp.Value.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var item in containerProp.Value.EnumerateObject())
                                {
                                    dict[item.Name] = item.Value.Clone();
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // If the preferences file is corrupted or unreadable, start fresh with empty store
            _containers.Clear();
        }
        finally
        {
            _loaded = true;
        }
    }

    private void SaveToFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(_containers, jsonOptions);

            var tempFilePath = _filePath + ".tmp";
            File.WriteAllText(tempFilePath, json);

            try
            {
                File.Move(tempFilePath, _filePath, overwrite: true);
            }
            catch
            {
                File.WriteAllText(_filePath, json);
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
        catch
        {
            // Do not crash if disk write temporarily fails
        }
    }

    private static T ConvertValue<T>(object rawValue, T defaultValue)
    {
        try
        {
            var targetType = typeof(T);

            if (rawValue is T directMatch)
            {
                return directMatch;
            }

            if (rawValue is JsonElement elem)
            {
                return ConvertJsonElement<T>(elem, defaultValue);
            }

            if (targetType == typeof(bool))
            {
                if (rawValue is bool b) return (T)(object)b;
                if (bool.TryParse(rawValue.ToString(), out var parsedBool)) return (T)(object)parsedBool;
                return defaultValue;
            }

            if (targetType == typeof(int))
            {
                if (rawValue is int i) return (T)(object)i;
                if (rawValue is long l) return (T)(object)(int)l;
                if (int.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedInt)) return (T)(object)parsedInt;
                return defaultValue;
            }

            if (targetType == typeof(long))
            {
                if (rawValue is long l) return (T)(object)l;
                if (rawValue is int i) return (T)(object)(long)i;
                if (long.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedLong)) return (T)(object)parsedLong;
                return defaultValue;
            }

            if (targetType == typeof(double))
            {
                if (rawValue is double d) return (T)(object)d;
                if (rawValue is float f) return (T)(object)(double)f;
                if (rawValue is int i) return (T)(object)(double)i;
                if (rawValue is long l) return (T)(object)(double)l;
                if (double.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDouble)) return (T)(object)parsedDouble;
                return defaultValue;
            }

            if (targetType == typeof(float))
            {
                if (rawValue is float f) return (T)(object)f;
                if (rawValue is double d) return (T)(object)(float)d;
                if (rawValue is int i) return (T)(object)(float)i;
                if (float.TryParse(rawValue.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedFloat)) return (T)(object)parsedFloat;
                return defaultValue;
            }

            if (targetType == typeof(string))
            {
                return (T)(object)rawValue.ToString()!;
            }

            if (targetType == typeof(DateTime))
            {
                if (rawValue is DateTime dt) return (T)(object)dt;
                if (DateTime.TryParse(rawValue.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDt)) return (T)(object)parsedDt;
                return defaultValue;
            }

            return (T)Convert.ChangeType(rawValue, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static T ConvertJsonElement<T>(JsonElement elem, T defaultValue)
    {
        var targetType = typeof(T);

        if (targetType == typeof(bool))
        {
            if (elem.ValueKind == JsonValueKind.True) return (T)(object)true;
            if (elem.ValueKind == JsonValueKind.False) return (T)(object)false;
            if (elem.ValueKind == JsonValueKind.String && bool.TryParse(elem.GetString(), out var b)) return (T)(object)b;
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var num)) return (T)(object)(num != 0);
            return defaultValue;
        }

        if (targetType == typeof(int))
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt32(out var i)) return (T)(object)i;
            if (elem.ValueKind == JsonValueKind.String && int.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedI)) return (T)(object)parsedI;
            return defaultValue;
        }

        if (targetType == typeof(long))
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt64(out var l)) return (T)(object)l;
            if (elem.ValueKind == JsonValueKind.String && long.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedL)) return (T)(object)parsedL;
            return defaultValue;
        }

        if (targetType == typeof(double))
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetDouble(out var d)) return (T)(object)d;
            if (elem.ValueKind == JsonValueKind.String && double.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedD)) return (T)(object)parsedD;
            return defaultValue;
        }

        if (targetType == typeof(float))
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetSingle(out var f)) return (T)(object)f;
            if (elem.ValueKind == JsonValueKind.String && float.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedF)) return (T)(object)parsedF;
            return defaultValue;
        }

        if (targetType == typeof(string))
        {
            if (elem.ValueKind == JsonValueKind.String) return (T)(object)elem.GetString()!;
            return (T)(object)elem.ToString();
        }

        if (targetType == typeof(DateTime))
        {
            if (elem.ValueKind == JsonValueKind.String && elem.TryGetDateTime(out var dt)) return (T)(object)dt;
            if (elem.ValueKind == JsonValueKind.String && DateTime.TryParse(elem.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDt)) return (T)(object)parsedDt;
            return defaultValue;
        }

        try
        {
            var raw = elem.GetRawText();
            var deserialized = JsonSerializer.Deserialize<T>(raw);
            return deserialized ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
}
