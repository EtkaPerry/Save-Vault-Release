# SaveVault Extension Development Guide

## Overview

SaveVault supports Lua-based extensions that can modify the application's behavior and appearance without requiring direct code changes to the main application. Extensions now have comprehensive access to UI modification, translation services, event handling, and much more.

## Extension Structure

Each extension must have the following 5. Test extension loading/unloading from the extension manager
6. Test menu items and UI interactions
7. Test with different languagesucture:
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
  "createdDate": "2024-12-19T00:00:00Z",
  "updatedDate": "2024-12-19T00:00:00Z"
}
```

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
addButton(location, buttonText, tooltip)  -- Add button to main UI (future)
createWindow(title, width, height)        -- Create new custom window
addLabel(windowTitle, text)               -- Add text label to custom window
addWindowButton(windowTitle, text, callback) -- Add button to custom window
addTextBox(windowTitle, name, placeholder) -- Add text input to custom window
local value = getControlValue(windowTitle, controlName) -- Get value from control
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

## System Events (NEW!)

Extensions can subscribe to these predefined system events:

- `app.startup` - Application startup
- `app.shutdown` - Application shutdown
- `app.language.changed` - Language changed
- `app.settings.changed` - Settings changed
- `app.window.opened` - Window opened
- `app.window.closed` - Window closed
- `extension.installed` - Extension installed
- `extension.uninstalled` - Extension uninstalled
- `extension.enabled` - Extension enabled
- `extension.disabled` - Extension disabled
- `games.scan.completed` - Game scan completed
- `games.added` - Game added
- `games.removed` - Game removed
- `saves.backup.created` - Save backup created
- `saves.restored` - Save restored

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

Extensions run in a sandboxed environment with the following restrictions:
- No access to system files outside the extension directory
- No network access (except future approved APIs)
- Limited to approved API functions
- Cannot execute system commands
- UI modifications are controlled and safe

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
