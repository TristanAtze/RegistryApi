using Microsoft.Win32;
using System.Text.Json;
using System.Security;

namespace RegistryApi
{
    public class RegistryApiException : Exception
    {
        public string? KeyPath { get; }
        public string? ValueName { get; }

        public RegistryApiException(string message) : base(message) { }
        
        public RegistryApiException(string message, Exception innerException) : base(message, innerException) { }
        
        public RegistryApiException(string message, string? keyPath, string? valueName = null) : base(message)
        {
            KeyPath = keyPath;
            ValueName = valueName;
        }
    }

    public class RegistryAccessDeniedException : RegistryApiException
    {
        public RegistryAccessDeniedException(string keyPath) 
            : base($"Access denied to registry key: {keyPath}", keyPath) { }
    }

    public class RegistryKeyNotFoundException : RegistryApiException
    {
        public RegistryKeyNotFoundException(string keyPath) 
            : base($"Registry key not found: {keyPath}", keyPath) { }
    }

    public class RegistryValueNotFoundException : RegistryApiException
    {
        public RegistryValueNotFoundException(string keyPath, string valueName) 
            : base($"Registry value '{valueName}' not found in key: {keyPath}", keyPath, valueName) { }
    }

    public class InvalidRegistryPathException : RegistryApiException
    {
        public InvalidRegistryPathException(string keyPath) 
            : base($"Invalid registry path format: {keyPath}", keyPath) { }
    }
    public enum RegistryValueType
    {
        String,
        DWord,
        QWord,
        Binary,
        MultiString,
        ExpandString
    }

    public class RegistryValue
    {
        public string Name { get; set; } = string.Empty;
        public object? Value { get; set; }
        public RegistryValueType Type { get; set; }
    }

    public class RegistryKeyInfo
    {
        public string Path { get; set; } = string.Empty;
        public List<string> SubKeys { get; set; } = new();
        public List<RegistryValue> Values { get; set; } = new();
        public DateTime LastWriteTime { get; set; }
    }

    public class RegistryManager
    {
        private readonly Dictionary<string, RegistryKey> _rootKeys = new()
        {
            { "HKEY_CLASSES_ROOT", Registry.ClassesRoot },
            { "HKCR", Registry.ClassesRoot },
            { "HKEY_CURRENT_USER", Registry.CurrentUser },
            { "HKCU", Registry.CurrentUser },
            { "HKEY_LOCAL_MACHINE", Registry.LocalMachine },
            { "HKLM", Registry.LocalMachine },
            { "HKEY_USERS", Registry.Users },
            { "HKU", Registry.Users },
            { "HKEY_CURRENT_CONFIG", Registry.CurrentConfig },
            { "HKCC", Registry.CurrentConfig }
        };

        private void ValidateKeyPath(string keyPath)
        {
            if (string.IsNullOrWhiteSpace(keyPath))
                throw new InvalidRegistryPathException("Registry path cannot be null or empty");

            var parts = keyPath.Split('\\', 2);
            var rootKeyName = parts[0].ToUpperInvariant();

            if (!_rootKeys.ContainsKey(rootKeyName))
                throw new InvalidRegistryPathException($"Invalid root key: {rootKeyName}");

            if (keyPath.Contains("//") || keyPath.Contains("\\\\"))
                throw new InvalidRegistryPathException("Registry path contains invalid double slashes");
        }

        private void ValidateValueName(string valueName)
        {
            if (valueName == null)
                throw new ArgumentNullException(nameof(valueName));

            // Registry value names cannot contain certain characters
            var invalidChars = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            if (valueName.IndexOfAny(invalidChars) >= 0)
                throw new ArgumentException($"Value name contains invalid characters: {valueName}");
        }

        private RegistryKey OpenRegistryKeyWithValidation(string keyPath, bool writable, bool throwOnNotFound = true)
        {
            ValidateKeyPath(keyPath);
            
            var (rootKey, subKeyPath) = ParseRegistryPath(keyPath);
            if (rootKey == null) 
                throw new InvalidRegistryPathException(keyPath);

            try
            {
                var key = string.IsNullOrEmpty(subKeyPath) 
                    ? rootKey 
                    : rootKey.OpenSubKey(subKeyPath, writable);

                if (key == null && throwOnNotFound)
                    throw new RegistryKeyNotFoundException(keyPath);

                return key!;
            }
            catch (SecurityException ex)
            {
                throw new RegistryAccessDeniedException(keyPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new RegistryAccessDeniedException(keyPath);
            }
        }

        public T? ReadValue<T>(string keyPath, string valueName, T? defaultValue = default)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return defaultValue;

                var value = key.GetValue(valueName, defaultValue);
                return value is T result ? result : defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public T ReadValueStrict<T>(string keyPath, string valueName)
        {
            ValidateValueName(valueName);
            
            using var key = OpenRegistryKeyWithValidation(keyPath, false);
            var value = key.GetValue(valueName);
            
            if (value == null)
                throw new RegistryValueNotFoundException(keyPath, valueName);

            if (value is not T result)
                throw new InvalidCastException($"Cannot convert registry value '{valueName}' of type {value.GetType()} to {typeof(T)}");

            return result;
        }

        public bool WriteValue(string keyPath, string valueName, object value, RegistryValueType valueType = RegistryValueType.String)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, true);
                if (key == null) return false;

                var registryValueKind = ConvertToRegistryValueKind(valueType);
                key.SetValue(valueName, value, registryValueKind);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void WriteValueStrict(string keyPath, string valueName, object value, RegistryValueType valueType = RegistryValueType.String)
        {
            ValidateValueName(valueName);
            ArgumentNullException.ThrowIfNull(value);

            using var key = OpenRegistryKeyWithValidation(keyPath, true);
            var registryValueKind = ConvertToRegistryValueKind(valueType);
            
            try
            {
                key.SetValue(valueName, value, registryValueKind);
            }
            catch (ArgumentException ex)
            {
                throw new RegistryApiException($"Failed to write value '{valueName}' to key '{keyPath}': {ex.Message}", ex);
            }
        }

        public bool DeleteValue(string keyPath, string valueName)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, true);
                if (key == null) return false;

                key.DeleteValue(valueName, false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool CreateKey(string keyPath)
        {
            try
            {
                var (rootKey, subKeyPath) = ParseRegistryPath(keyPath);
                if (rootKey == null) return false;

                using var key = rootKey.CreateSubKey(subKeyPath);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteKey(string keyPath, bool recursive = false)
        {
            try
            {
                var (rootKey, subKeyPath) = ParseRegistryPath(keyPath);
                if (rootKey == null) return false;

                if (recursive)
                    rootKey.DeleteSubKeyTree(subKeyPath, false);
                else
                    rootKey.DeleteSubKey(subKeyPath, false);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool KeyExists(string keyPath)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public bool ValueExists(string keyPath, string valueName)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return false;

                return key.GetValueNames().Contains(valueName);
            }
            catch
            {
                return false;
            }
        }

        private RegistryKey? OpenRegistryKey(string keyPath, bool writable)
        {
            var (rootKey, subKeyPath) = ParseRegistryPath(keyPath);
            if (rootKey == null) return null;

            return string.IsNullOrEmpty(subKeyPath) 
                ? rootKey 
                : rootKey.OpenSubKey(subKeyPath, writable);
        }

        private (RegistryKey? rootKey, string subKeyPath) ParseRegistryPath(string fullPath)
        {
            var parts = fullPath.Split('\\', 2);
            var rootKeyName = parts[0].ToUpperInvariant();

            if (!_rootKeys.TryGetValue(rootKeyName, out var rootKey))
                return (null, string.Empty);

            var subKeyPath = parts.Length > 1 ? parts[1] : string.Empty;
            return (rootKey, subKeyPath);
        }

        public RegistryKeyInfo? GetKeyInfo(string keyPath)
        {
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return null;

                var info = new RegistryKeyInfo
                {
                    Path = keyPath,
                    SubKeys = key.GetSubKeyNames().ToList(),
                    Values = new List<RegistryValue>()
                };

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName);
                    var valueKind = key.GetValueKind(valueName);
                    
                    info.Values.Add(new RegistryValue
                    {
                        Name = valueName,
                        Value = value,
                        Type = ConvertFromRegistryValueKind(valueKind)
                    });
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        public List<string> EnumerateSubKeys(string keyPath, bool recursive = false)
        {
            var subKeys = new List<string>();
            
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return subKeys;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    var fullSubKeyPath = $"{keyPath}\\{subKeyName}";
                    subKeys.Add(fullSubKeyPath);

                    if (recursive)
                    {
                        subKeys.AddRange(EnumerateSubKeys(fullSubKeyPath, true));
                    }
                }
            }
            catch
            {
                // Ignore access denied or other errors for individual keys
            }

            return subKeys;
        }

        public List<RegistryValue> SearchValues(string keyPath, string searchPattern, bool recursive = false)
        {
            var results = new List<RegistryValue>();

            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return results;

                foreach (var valueName in key.GetValueNames())
                {
                    if (IsMatch(valueName, searchPattern))
                    {
                        var value = key.GetValue(valueName);
                        var valueKind = key.GetValueKind(valueName);
                        
                        results.Add(new RegistryValue
                        {
                            Name = $"{keyPath}\\{valueName}",
                            Value = value,
                            Type = ConvertFromRegistryValueKind(valueKind)
                        });
                    }
                }

                if (recursive)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        var subKeyPath = $"{keyPath}\\{subKeyName}";
                        results.AddRange(SearchValues(subKeyPath, searchPattern, true));
                    }
                }
            }
            catch
            {
                // Ignore access denied or other errors for individual keys
            }

            return results;
        }

        public List<string> SearchKeys(string rootPath, string searchPattern, bool recursive = false)
        {
            var results = new List<string>();

            try
            {
                using var key = OpenRegistryKey(rootPath, false);
                if (key == null) return results;

                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    var fullSubKeyPath = $"{rootPath}\\{subKeyName}";
                    
                    if (IsMatch(subKeyName, searchPattern))
                    {
                        results.Add(fullSubKeyPath);
                    }

                    if (recursive)
                    {
                        results.AddRange(SearchKeys(fullSubKeyPath, searchPattern, true));
                    }
                }
            }
            catch
            {
                // Ignore access denied or other errors for individual keys
            }

            return results;
        }

        public Dictionary<string, object> GetAllValues(string keyPath, bool includeSubKeys = false)
        {
            var values = new Dictionary<string, object>();

            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return values;

                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName);
                    if (value != null)
                    {
                        values[valueName] = value;
                    }
                }

                if (includeSubKeys)
                {
                    foreach (var subKeyName in key.GetSubKeyNames())
                    {
                        var subKeyPath = $"{keyPath}\\{subKeyName}";
                        var subKeyValues = GetAllValues(subKeyPath, true);
                        
                        foreach (var kvp in subKeyValues)
                        {
                            values[$"{subKeyName}\\{kvp.Key}"] = kvp.Value;
                        }
                    }
                }
            }
            catch
            {
                // Ignore access denied or other errors
            }

            return values;
        }

        private bool IsMatch(string text, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;
            
            // Simple wildcard matching
            if (pattern.Contains('*'))
            {
                var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            
            return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static RegistryValueType ConvertFromRegistryValueKind(RegistryValueKind valueKind)
        {
            return valueKind switch
            {
                RegistryValueKind.String => RegistryValueType.String,
                RegistryValueKind.DWord => RegistryValueType.DWord,
                RegistryValueKind.QWord => RegistryValueType.QWord,
                RegistryValueKind.Binary => RegistryValueType.Binary,
                RegistryValueKind.MultiString => RegistryValueType.MultiString,
                RegistryValueKind.ExpandString => RegistryValueType.ExpandString,
                _ => RegistryValueType.String
            };
        }

        public bool BackupKeyToJson(string keyPath, string filePath)
        {
            try
            {
                var keyInfo = GetKeyInfo(keyPath);
                if (keyInfo == null) return false;

                var backup = CreateBackupData(keyPath);
                var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });

                File.WriteAllText(filePath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RestoreKeyFromJson(string filePath, bool overwrite = false)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var backup = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                
                if (backup == null) return false;

                return RestoreFromBackupData(backup, overwrite);
            }
            catch
            {
                return false;
            }
        }

        public bool BackupKeyToRegistry(string sourceKeyPath, string backupKeyPath)
        {
            try
            {
                if (KeyExists(backupKeyPath))
                {
                    DeleteKey(backupKeyPath, true);
                }

                return CopyKey(sourceKeyPath, backupKeyPath);
            }
            catch
            {
                return false;
            }
        }

        public bool RestoreKeyFromRegistry(string backupKeyPath, string targetKeyPath, bool overwrite = false)
        {
            try
            {
                if (!overwrite && KeyExists(targetKeyPath))
                {
                    return false;
                }

                if (KeyExists(targetKeyPath))
                {
                    DeleteKey(targetKeyPath, true);
                }

                return CopyKey(backupKeyPath, targetKeyPath);
            }
            catch
            {
                return false;
            }
        }

        private Dictionary<string, object> CreateBackupData(string keyPath)
        {
            var backup = new Dictionary<string, object>();
            
            try
            {
                using var key = OpenRegistryKey(keyPath, false);
                if (key == null) return backup;

                backup["_KeyPath"] = keyPath;
                backup["_BackupDate"] = DateTime.Now.ToString("O");

                // Backup values
                var values = new Dictionary<string, object>();
                foreach (var valueName in key.GetValueNames())
                {
                    var value = key.GetValue(valueName);
                    var valueKind = key.GetValueKind(valueName);
                    
                    values[valueName] = new Dictionary<string, object>
                    {
                        ["Value"] = value ?? string.Empty,
                        ["Type"] = valueKind.ToString()
                    };
                }
                backup["Values"] = values;

                // Backup subkeys recursively
                var subKeys = new Dictionary<string, object>();
                foreach (var subKeyName in key.GetSubKeyNames())
                {
                    var subKeyPath = $"{keyPath}\\{subKeyName}";
                    subKeys[subKeyName] = CreateBackupData(subKeyPath);
                }
                backup["SubKeys"] = subKeys;
            }
            catch
            {
                // Ignore errors for individual keys
            }

            return backup;
        }

        private bool RestoreFromBackupData(Dictionary<string, object> backup, bool overwrite)
        {
            try
            {
                if (!backup.TryGetValue("_KeyPath", out var keyPathObj) || keyPathObj is not string keyPath)
                    return false;

                if (!overwrite && KeyExists(keyPath))
                    return false;

                // Create the key if it doesn't exist
                if (!KeyExists(keyPath))
                {
                    CreateKey(keyPath);
                }

                // Restore values
                if (backup.TryGetValue("Values", out var valuesObj) && valuesObj is JsonElement valuesElement)
                {
                    foreach (var valueProperty in valuesElement.EnumerateObject())
                    {
                        var valueName = valueProperty.Name;
                        var valueData = valueProperty.Value;
                        
                        if (valueData.TryGetProperty("Value", out var valueElement) &&
                            valueData.TryGetProperty("Type", out var typeElement))
                        {
                            var value = valueElement.GetRawText().Trim('"');
                            var typeString = typeElement.GetString();
                            
                            if (Enum.TryParse<RegistryValueKind>(typeString, out var valueKind))
                            {
                                var registryType = ConvertFromRegistryValueKind(valueKind);
                                WriteValue(keyPath, valueName, value, registryType);
                            }
                        }
                    }
                }

                // Restore subkeys recursively
                if (backup.TryGetValue("SubKeys", out var subKeysObj) && subKeysObj is JsonElement subKeysElement)
                {
                    foreach (var subKeyProperty in subKeysElement.EnumerateObject())
                    {
                        var subKeyData = JsonSerializer.Deserialize<Dictionary<string, object>>(subKeyProperty.Value.GetRawText());
                        if (subKeyData != null)
                        {
                            RestoreFromBackupData(subKeyData, overwrite);
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool CopyKey(string sourceKeyPath, string targetKeyPath)
        {
            try
            {
                using var sourceKey = OpenRegistryKey(sourceKeyPath, false);
                if (sourceKey == null) return false;

                // Create target key
                if (!CreateKey(targetKeyPath)) return false;

                // Copy all values
                foreach (var valueName in sourceKey.GetValueNames())
                {
                    var value = sourceKey.GetValue(valueName);
                    var valueKind = sourceKey.GetValueKind(valueName);
                    var valueType = ConvertFromRegistryValueKind(valueKind);
                    
                    WriteValue(targetKeyPath, valueName, value ?? string.Empty, valueType);
                }

                // Copy all subkeys recursively
                foreach (var subKeyName in sourceKey.GetSubKeyNames())
                {
                    var sourceSubKeyPath = $"{sourceKeyPath}\\{subKeyName}";
                    var targetSubKeyPath = $"{targetKeyPath}\\{subKeyName}";
                    
                    CopyKey(sourceSubKeyPath, targetSubKeyPath);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static RegistryValueKind ConvertToRegistryValueKind(RegistryValueType valueType)
        {
            return valueType switch
            {
                RegistryValueType.String => RegistryValueKind.String,
                RegistryValueType.DWord => RegistryValueKind.DWord,
                RegistryValueType.QWord => RegistryValueKind.QWord,
                RegistryValueType.Binary => RegistryValueKind.Binary,
                RegistryValueType.MultiString => RegistryValueKind.MultiString,
                RegistryValueType.ExpandString => RegistryValueKind.ExpandString,
                _ => RegistryValueKind.String
            };
        }
    }
}
