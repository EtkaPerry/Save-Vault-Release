# SaveVault Extension Development Guide

## Overview

SaveVault supports Lua-based extensions that can modify the application's behavior and appearance without requiring direct code changes to the main application. Extensions now have comprehensive access to UI modification, translation services, event handling, and much more.

## Extension Structure

Each extension is a folder with the following structure:
```
extension-folder/
├── manifest.json    # Extension metadata and configuration
├── main.lua        # Main extension script
└── README.md       # Optional documentation
```

### Manifest.json Structure

```json
{
  "id": "your.extension.id",
  "name": "Extension Name",
  "version": "1.0.0",
  "description": "Extension description",
  "author": "Your Name",
  "category": "Official|Fixes|Localization|Theming|Other",
  "main": "main.lua",
  "minimumVersion": "0.2.8",
  "tags": ["theme", "dark", "utility"],
  "permissions": ["network", "files", "clipboard", "backups", "games"],
  "createdDate": "2024-12-19T00:00:00Z",
  "updatedDate": "2024-12-19T00:00:00Z"
}
```

### Permissions

`permissions` declares the sensitive capabilities your extension needs. The host enforces it at
the API boundary — calls to a capability you didn't request are blocked and logged.

| Permission  | Unlocks                                                        |
|-------------|---------------------------------------------------------------|
| `network`   | `httpRequest`, `openUrl`                                       |
| `files`     | `readExtensionFile`, `writeExtensionFile`                     |
| `clipboard` | `copyToClipboard`                                             |
| `backups`   | `getBackups`, `createBackupNow`, `restoreBackup`             |
| `games`     | `getGames`, `getSavePath`                                     |

Capabilities that are *not* in the table (logging, settings, translations, events, menus, windows,
notifications, theming) are always available and need no declaration.

**Back-compat:** if you omit `permissions` entirely, your extension keeps the capabilities that
existed before this model (network/files/clipboard) — but the newer `backups` and `games` APIs
*always* require an explicit declaration. As soon as you add a `permissions` array, it is enforced
strictly: only what you list is granted. Official/built-in extensions are trusted with everything.

## Extension API

Extensions have access to a comprehensive sandboxed Lua environment with the following APIs:

### Logging Functions
```lua
logInfo(message)     -- Log informational message
logWarning(message)  -- Log warning message
logError(message)    -- Log error message
```

### Settings Functions
```lua
local value = getSetting(key)    -- Get extension setting
setSetting(key, value)           -- Set extension setting
```

### UI Functions (Enhanced)
```lua
addMenuItem(menuName, itemText, tooltip)  -- Add menu item
addButton(location, buttonText, callbackFunction, tooltip) -- Add a status-bar button; click calls callbackFunction(buttonText)
createWindow(title, width, height)        -- Create new custom window
addLabel(windowTitle, text)               -- Add text label to custom window
addWindowButton(windowTitle, text, callback) -- Add button to custom window
addTextBox(windowTitle, name, placeholder) -- Add text input to custom window
local value = getControlValue(windowTitle, controlName) -- Get value from control
```

### Host Data Functions (games & backups)
```lua
local gamesJson = getGames()                  -- JSON array: {name, savePath, executable, lastBackup}   [requires "games"]
local path = getSavePath(gameName)            -- Save folder for a game ("" if unknown)                 [requires "games"]
local backupsJson = getBackups(gameName)      -- JSON array: {path, description, timestamp, isAuto}      [requires "backups"]
local ok = createBackupNow(gameName)          -- Force an immediate backup of a game                     [requires "backups"]
local ok = restoreBackup(gameName, backupPath)-- Restore a game from a specific backup path              [requires "backups"]
```

### JSON Helper
A small `json` library is always available so you can work with the JSON payloads carried by host
events and the host-data functions:
```lua
local data  = json.decode(jsonString)   -- JSON string -> Lua table (nil on empty/invalid input)
local text  = json.encode(luaTable)     -- Lua table -> JSON string

local games = json.decode(getGames())
for _, g in ipairs(games) do
    logInfo(g.name .. " -> " .. g.savePath)
end
```

### System Functions (NEW!)
```lua
httpRequest(url, method, body, headers, callback) -- Make async HTTP request
showNotification(title, message, type)    -- Show toast notification (type: info, success, warning, error)
showDialog(title, message)                -- Show modal dialog
copyToClipboard(text)                     -- Copy text to clipboard
openUrl(url)                              -- Open URL in default browser
```

### Translation Functions (NEW!)
```lua
addTranslation(language, key, value)    -- Add translation
local text = getTranslation(key, fallback)  -- Get translated text
local lang = getCurrentLanguage()        -- Get current language
```

### Event Functions (NEW!)
```lua
subscribeToEvent(eventName, callbackFunction)  -- Subscribe to event
triggerEvent(eventName, data)                  -- Trigger custom event
unsubscribeFromEvent(eventName)                -- Unsubscribe from event
```

### File Functions (Sandboxed)
```lua
local content = readExtensionFile(filename)     -- Read file from extension directory
writeExtensionFile(filename, content)          -- Write file to extension directory
```

### Extension Context Variables
```lua
currentExtensionId      -- Current extension ID
currentExtensionName    -- Current extension name
currentExtensionVersion -- Current extension version
```

## System Events

Subscribe with `subscribeToEvent(eventName, "yourCallback")`. Your callback is invoked as
`callback(eventName, data)`. For events whose data is JSON, parse it with `json.decode(data)`.

**Events the host fires:**

| Event                    | `data` payload                                         |
|--------------------------|--------------------------------------------------------|
| `app.startup`            | (none) — fired once after extensions finish loading    |
| `app.shutdown`           | (none) — fired when the app is closing (best effort)   |
| `app.language.changed`   | language code string, e.g. `"en-US"`                   |
| `app.theme.changed`      | theme string: `"Light"`, `"Dark"` or `"System"`        |
| `extension.installed`    | JSON `{id, name, version}`                              |
| `extension.uninstalled`  | JSON `{id, name, version}`                              |
| `extension.enabled`      | JSON `{id, name, version}`                              |
| `extension.disabled`     | JSON `{id, name, version}`                              |
| `games.scan.completed`   | JSON `{total, withSaveLocations}`                       |
| `games.added`            | JSON `{name, savePath, executable}`                     |
| `saves.backup.created`   | JSON `{app, path, auto, time}`                          |
| `saves.restored`         | JSON `{app, fromBackup, files}`                         |

```lua
function onBackup(eventName, data)
    local b = json.decode(data)
    logInfo("Backed up " .. b.app .. " -> " .. b.path .. " (auto=" .. tostring(b.auto) .. ")")
end
subscribeToEvent("saves.backup.created", "onBackup")
```

**Reserved (not emitted by the host yet, but you may `triggerEvent` them for your own use):**
`app.settings.changed`, `app.window.opened`, `app.window.closed`, `games.removed`.

## Extension Callbacks (NEW!)

### Menu Item Callback
```lua
function onMenuItemClick(menuItemText)
    -- Called when menu items added by this extension are clicked
    if menuItemText == "My Menu Item" then
        logInfo("My menu item was clicked!")
    end
end
```

### Event Callbacks
```lua
function onLanguageChanged(eventName, data)
    -- Called when subscribed to app.language.changed
    logInfo("Language changed to: " .. (data or "unknown"))
end
```

## Language and Translation API

### Language Registration
Extensions can register themselves as providing support for additional languages:

```lua
-- Register a language (makes it available in the Language dropdown)
registerLanguage(languageCode, displayName)

-- Examples:
registerLanguage("es-ES", "Español")
registerLanguage("fr-FR", "Français") 
registerLanguage("de-DE", "Deutsch")
registerLanguage("pt-BR", "Português (Brasil)")
registerLanguage("ja-JP", "日本語")

-- Unregister a language (removes it from dropdown when extension is disabled)
unregisterLanguage(languageCode)
```

### Translation Functions
```lua
-- Add a translation for a specific key and language
addTranslation(language, key, value)

-- Get a translation (returns fallback if not found)
local text = getTranslation(key, fallbackValue)

-- Get current language code
local lang = getCurrentLanguage()

-- Get available languages (returns formatted string)
local languages = getAvailableLanguages()
```

### Language Events
```lua
function onLanguageChanged(eventName, newLanguageCode)
    -- Called when user changes language in settings
    logInfo("Language changed to: " .. newLanguageCode)
    
    -- You might want to update UI or reload content
    -- based on the new language
end

-- Subscribe to language change events
subscribeToEvent("app.language.changed", "onLanguageChanged")
```

### Best Practices for Language Extensions
1. **Always register your languages in onLoad()** - This makes them available in the settings
2. **Always unregister in onUnload()** - Clean up when extension is disabled
3. **Use standard language codes** - Like "en-US", "es-ES", "fr-FR", etc.
4. **Provide fallbacks** - Use `getTranslation(key, fallback)` to handle missing translations
5. **Subscribe to language changes** - Update your UI when user changes language
6. **Test with different languages** - Make sure your extension works in all languages you support

### Complete Language Extension Example
```lua
function onLoad()
    -- Register the languages this extension supports
    registerLanguage("es-ES", "Español")
    registerLanguage("fr-FR", "Français")
    
    -- Add translations for each language
    -- Spanish
    addTranslation("es-ES", "welcome", "Bienvenido")
    addTranslation("es-ES", "settings", "Configuración")
    addTranslation("es-ES", "exit", "Salir")
    
    -- French  
    addTranslation("fr-FR", "welcome", "Bienvenue")
    addTranslation("fr-FR", "settings", "Paramètres")
    addTranslation("fr-FR", "exit", "Quitter")
    
    -- Subscribe to language changes
    subscribeToEvent("app.language.changed", "onLanguageChanged")
    
    logInfo("Language extension loaded with Spanish and French support")
end

function onUnload()
    -- Clean up registered languages
    unregisterLanguage("es-ES")
    unregisterLanguage("fr-FR")
end

function onLanguageChanged(eventName, newLanguage)
    logInfo("Language changed to: " .. newLanguage)
    -- You could refresh UI elements here if needed
end
```

## Extension Lifecycle

### Required Functions

```lua
function onLoad()
    -- Called when extension is enabled
    -- Initialize your extension here
end

function onUnload()
    -- Called when extension is disabled
    -- Cleanup resources here
end
```

## Extension Examples

### Backup Companion (events + host data + permissions)

A complete example using the new modding capabilities. Its `manifest.json` declares:
```json
{ "id": "example.backup-companion", "name": "Backup Companion", "version": "1.0.0",
  "author": "You", "category": "Other", "main": "main.lua",
  "permissions": ["backups", "games"] }
```
```lua
function onLoad()
    logInfo("Backup Companion loaded")

    -- A status-bar button that backs up the currently selected-ish game on demand.
    addButton("statusbar", "Backup First Game", "bc_onBackupClick", "Back up the first detected game")

    -- React whenever ANY backup is created.
    subscribeToEvent("saves.backup.created", "bc_onBackup")
end

function bc_onBackup(eventName, data)
    local b = json.decode(data)
    if b then
        showNotification("Backup created", b.app .. " (" .. (b.auto and "auto" or "manual") .. ")", "success")
    end
end

function bc_onBackupClick(buttonText)
    local games = json.decode(getGames())          -- requires "games"
    if games and games[1] then
        local ok = createBackupNow(games[1].name)  -- requires "backups"
        logInfo("Manual backup of " .. games[1].name .. ": " .. tostring(ok))
    end
end

function onUnload()
    unsubscribeFromEvent("saves.backup.created")
end
```

### Comprehensive UI Extension
```lua
function onLoad()
    logInfo('Advanced Extension loaded!')
    
    -- Add menu items
    addMenuItem("Tools", "My Extension", "Open my extension window")
    addMenuItem("Tools", "Quick Action", "Perform a quick action")
    
    -- Add translations
    addTranslation("en-US", "window_title", "My Extension Window")
    addTranslation("es-ES", "window_title", "Ventana de Mi Extensión")
    addTranslation("fr-FR", "window_title", "Fenêtre de Mon Extension")
      -- Subscribe to events
    subscribeToEvent("app.language.changed", "onLanguageChanged")
    subscribeToEvent("app.startup", "onAppStartup")
    
    -- Apply settings
    setSetting('myext.loaded', 'true')
end

function onMenuItemClick(menuItemText)
    if menuItemText == "My Extension" then
        local title = getTranslation("window_title", "My Extension Window")
        createWindow(title, 500, 400)
    elseif menuItemText == "Quick Action" then
        triggerEvent("myext.quick.action", "Action performed")
    end
end

function onLanguageChanged(eventName, data)
    logInfo("Reapplying extension settings after language change")
end

function onAppStartup(eventName, data)
    logInfo("Application started, extension is ready!")
end

function onUnload()
    setSetting('myext.loaded', 'false')
    logInfo('Extension unloaded')
end
```

### Translation Extension
```lua
function onLoad()
    local lang = getCurrentLanguage()
    logInfo('Loading translations for: ' .. lang)
    
    -- Register this extension as providing Spanish language support
    registerLanguage("es-ES", "Español")
    
    -- Register this extension as providing French language support  
    registerLanguage("fr-FR", "Français")
    
    -- Register this extension as providing German language support
    registerLanguage("de-DE", "Deutsch")
    
    -- Spanish translations
    addTranslation("es-ES", "file", "Archivo")
    addTranslation("es-ES", "edit", "Editar")
    addTranslation("es-ES", "view", "Ver")
    addTranslation("es-ES", "tools", "Herramientas")
    
    -- French translations
    addTranslation("fr-FR", "file", "Fichier")
    addTranslation("fr-FR", "edit", "Modifier")
    addTranslation("fr-FR", "view", "Affichage")
    addTranslation("fr-FR", "tools", "Outils")
    
    -- German translations
    addTranslation("de-DE", "file", "Datei")
    addTranslation("de-DE", "edit", "Bearbeiten")
    addTranslation("de-DE", "view", "Ansicht")
    addTranslation("de-DE", "tools", "Werkzeuge")
    
    -- Subscribe to language changes
    subscribeToEvent("app.language.changed", "onLanguageChanged")
end

function onLanguageChanged(eventName, newLanguage)
    logInfo("Language changed to: " .. newLanguage)
    -- Could reload or refresh translations here
end

function onUnload()
    -- Unregister languages when extension is disabled
    unregisterLanguage("es-ES")
    unregisterLanguage("fr-FR") 
    unregisterLanguage("de-DE")
end
```

## Development Best Practices

### 1. Error Handling
Always wrap your code in error handling:
```lua
function onLoad()
    local success, error = pcall(function()
        -- Your extension code here
        addMenuItem("Tools", "My Extension", "My extension tooltip")
    end)
    
    if not success then
        logError('Extension failed to load: ' .. tostring(error))
    end
end
```

### 2. Internationalization
Always use translations for user-facing text:
```lua
function onLoad()
    -- Add translations first
    addTranslation("en-US", "my_extension", "My Extension")
    addTranslation("es-ES", "my_extension", "Mi Extensión")
    
    -- Use translations in UI
    local title = getTranslation("my_extension", "My Extension")
    addMenuItem("Tools", title, "My extension tooltip")
end
```

### 3. Event-Driven Design
Use events for responsive extensions:
```lua
function onLoad()
    subscribeToEvent("app.language.changed", "onLanguageChanged")
    subscribeToEvent("app.settings.changed", "onSettingsChanged")
end

function onLanguageChanged(eventName, data)
    -- Update your extension when language changes
end
```

### 4. Settings Management
Use namespaced settings to avoid conflicts:
```lua
function onLoad()
    local enabled = getSetting('myext.feature.enabled') or 'true'
    local setting = getSetting('myext.custom.setting') or 'default'
    
    if enabled == 'true' then
        addMenuItem("Tools", "My Extension", "My extension tooltip")
    end
end
```

## Security Considerations

Extensions run in a restricted environment:
- **Capability permissions** — sensitive APIs (network, files, clipboard, backups, games) are
  gated by the `permissions` you declare in the manifest (see *Permissions* above). Undeclared
  calls are blocked and logged.
- **File sandbox** — `readExtensionFile`/`writeExtensionFile` are confined to your extension's own
  folder; path traversal (`..\..`) is rejected.
- **URL restrictions** — `httpRequest` allows only `http`/`https`; `openUrl` allows only
  `http`/`https`/`mailto` (no `file:`, no launching local executables).
- **Execution watchdog** — a script or callback that runs too long (e.g. an accidental infinite
  loop) is aborted automatically after ~5 seconds, so a bad extension can't freeze the app. Note
  this interrupts Lua execution only, not time spent waiting on a host call.
- **Limited API surface** — only the documented functions are available (no `os`, `io`, `require`,
  or arbitrary .NET access).

### Important: extensions share one Lua state

All enabled extensions currently run in a single shared Lua environment. That means a *global*
function defined by one extension can be overwritten by another that uses the same name. To avoid
collisions:
- **Give your callbacks unique, namespaced names** — e.g. `myext_onBackup` instead of `onBackup`,
  `myext_onClick` instead of a generic name. Pass these names to `subscribeToEvent`, `addButton`,
  and `addWindowButton`.
- The lifecycle functions `onLoad`, `onUnload`, `onMenuItemClick`, and (for themes) `applyTheme`
  are looked up by their fixed names — keep their bodies guarded with `pcall` and avoid assuming
  no other extension touches the same globals.

## Testing Extensions

1. Enable developer mode in `extension-config.json`
2. Set `debugLogging` to `true` for detailed logs
3. Use the log viewer (Shift+` or Tools → Log Viewer) to see extension messages
4. Test extension loading/unloading from the extension manager
5. Test menu items and UI interactions
6. Test with different languages and themes

## Extension Configuration

Extensions can be configured through `extension-config.json`:

```json
{
  "internalExtensions": {
    "autoEnable": {
      "your.extension.id": true
    }
  },  "extensionApi": {
    "allowedNamespaces": [
      "settings", "logging", "file", "ui", "translation", "events"
    ],
    "security": {
      "allowUIModification": true
    }
  },
  "developerMode": {
    "enabled": true,
    "debugLogging": true
  }
}
```

## Publishing Extensions

1. Create a GitHub repository with your extension
2. Follow the manifest.json format
3. Submit to the SaveVault Extensions repository
4. Extensions will be available through the built-in extension manager

## Support

For extension development support:
- Check the logs for detailed error messages
- Enable debug logging in extension configuration
- Review the built-in Example Extension and Complete Dark Mode extension
- Submit issues to the SaveVault Extensions repository
