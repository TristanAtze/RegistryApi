# RegistryApi

Eine umfassende Windows Registry Management Library für .NET, die sichere und effiziente Operationen zum Lesen, Schreiben und Verwalten von Registry-Schlüsseln und -Werten bereitstellt.

## Features

### Kernfunktionalitäten
- **Registry-Werte lesen und schreiben** - Unterstützt alle Registry-Datentypen (String, DWord, QWord, Binary, MultiString, ExpandString)
- **Registry-Schlüssel erstellen und löschen** - Mit rekursiven Optionen
- **Registry-Schlüssel und -Werte suchen** - Mit Wildcard-Unterstützung
- **Registry-Backup und -Wiederherstellung** - JSON- und Registry-basierte Backups
- **Umfassende Fehlerbehandlung** - Spezifische Exceptions für verschiedene Fehlertypen

### Unterstützte Registry-Hives
- HKEY_CLASSES_ROOT (HKCR)
- HKEY_CURRENT_USER (HKCU)
- HKEY_LOCAL_MACHINE (HKLM)
- HKEY_USERS (HKU)
- HKEY_CURRENT_CONFIG (HKCC)

## Installation

```bash
dotnet add package RegistryApi
```

## Verwendung

### Basis-Setup

```csharp
using RegistryApi;

var registryManager = new RegistryManager();
```

### Registry-Werte lesen

```csharp
// Sicheres Lesen mit Standardwerten
string appName = registryManager.ReadValue<string>(
    "HKCU\\Software\\MyApp", 
    "ApplicationName", 
    "DefaultApp"
);

// Striktes Lesen (wirft Exception bei Fehlern)
try
{
    int timeout = registryManager.ReadValueStrict<int>(
        "HKLM\\Software\\MyApp", 
        "Timeout"
    );
}
catch (RegistryKeyNotFoundException ex)
{
    Console.WriteLine($"Schlüssel nicht gefunden: {ex.KeyPath}");
}
```

### Registry-Werte schreiben

```csharp
// Sicheres Schreiben (gibt bool zurück)
bool success = registryManager.WriteValue(
    "HKCU\\Software\\MyApp", 
    "Version", 
    "1.0.0", 
    RegistryValueType.String
);

// Striktes Schreiben (wirft Exception bei Fehlern)
registryManager.WriteValueStrict(
    "HKCU\\Software\\MyApp", 
    "InstallCount", 
    1, 
    RegistryValueType.DWord
);
```

### Registry-Schlüssel verwalten

```csharp
// Schlüssel erstellen
registryManager.CreateKey("HKCU\\Software\\MyApp\\Settings");

// Schlüssel prüfen
if (registryManager.KeyExists("HKCU\\Software\\MyApp"))
{
    Console.WriteLine("Schlüssel existiert");
}

// Schlüssel löschen (rekursiv)
registryManager.DeleteKey("HKCU\\Software\\MyApp", recursive: true);
```

### Registry durchsuchen

```csharp
// Unterschlüssel auflisten
var subKeys = registryManager.EnumerateSubKeys("HKLM\\Software", recursive: false);

// Nach Werten suchen (mit Wildcards)
var results = registryManager.SearchValues(
    "HKCU\\Software", 
    "Install*", 
    recursive: true
);

// Nach Schlüsseln suchen
var keys = registryManager.SearchKeys(
    "HKLM\\Software", 
    "*Microsoft*", 
    recursive: true
);
```

### Backup und Wiederherstellung

```csharp
// JSON-Backup erstellen
registryManager.BackupKeyToJson(
    "HKCU\\Software\\MyApp", 
    @"C:\backup\myapp-backup.json"
);

// Aus JSON wiederherstellen
registryManager.RestoreKeyFromJson(
    @"C:\backup\myapp-backup.json", 
    overwrite: true
);

// Registry-zu-Registry Backup
registryManager.BackupKeyToRegistry(
    "HKCU\\Software\\MyApp", 
    "HKCU\\Software\\MyApp_Backup"
);

// Aus Registry-Backup wiederherstellen
registryManager.RestoreKeyFromRegistry(
    "HKCU\\Software\\MyApp_Backup", 
    "HKCU\\Software\\MyApp", 
    overwrite: true
);
```

### Erweiterte Operationen

```csharp
// Detaillierte Schlüssel-Informationen abrufen
var keyInfo = registryManager.GetKeyInfo("HKLM\\Software\\Microsoft");
if (keyInfo != null)
{
    Console.WriteLine($"Schlüssel: {keyInfo.Path}");
    Console.WriteLine($"Unterschlüssel: {keyInfo.SubKeys.Count}");
    Console.WriteLine($"Werte: {keyInfo.Values.Count}");
}

// Alle Werte eines Schlüssels abrufen
var allValues = registryManager.GetAllValues(
    "HKCU\\Software\\MyApp", 
    includeSubKeys: true
);

foreach (var kvp in allValues)
{
    Console.WriteLine($"{kvp.Key} = {kvp.Value}");
}
```

## Fehlerbehandlung

Die Library bietet spezifische Exception-Typen für verschiedene Fehlersituationen:

```csharp
try
{
    var value = registryManager.ReadValueStrict<string>("HKLM\\Invalid\\Path", "Value");
}
catch (RegistryKeyNotFoundException ex)
{
    Console.WriteLine($"Schlüssel nicht gefunden: {ex.KeyPath}");
}
catch (RegistryAccessDeniedException ex)
{
    Console.WriteLine($"Zugriff verweigert: {ex.KeyPath}");
}
catch (RegistryValueNotFoundException ex)
{
    Console.WriteLine($"Wert '{ex.ValueName}' nicht gefunden in: {ex.KeyPath}");
}
catch (InvalidRegistryPathException ex)
{
    Console.WriteLine($"Ungültiger Registry-Pfad: {ex.KeyPath}");
}
```

## Registry-Datentypen

Die Library unterstützt alle Standard-Registry-Datentypen:

```csharp
// String-Wert
registryManager.WriteValue("HKCU\\Test", "StringValue", "Hello", RegistryValueType.String);

// DWORD (32-bit Integer)
registryManager.WriteValue("HKCU\\Test", "DWordValue", 42, RegistryValueType.DWord);

// QWORD (64-bit Integer)  
registryManager.WriteValue("HKCU\\Test", "QWordValue", 123456789L, RegistryValueType.QWord);

// Binary-Daten
var binaryData = new byte[] { 0x01, 0x02, 0x03, 0x04 };
registryManager.WriteValue("HKCU\\Test", "BinaryValue", binaryData, RegistryValueType.Binary);

// Multi-String (String-Array)
var strings = new[] { "String1", "String2", "String3" };
registryManager.WriteValue("HKCU\\Test", "MultiString", strings, RegistryValueType.MultiString);

// Expandable String (mit Umgebungsvariablen)
registryManager.WriteValue("HKCU\\Test", "ExpandString", "%USERPROFILE%\\Documents", RegistryValueType.ExpandString);
```

## Systemanforderungen

- .NET 6.0 oder höher
- Windows-Betriebssystem
- Angemessene Registry-Berechtigungen für die gewünschten Operationen

## Lizenz

MIT License

## Beiträge

Beiträge sind willkommen! Bitte erstellen Sie einen Issue oder Pull Request auf GitHub.
