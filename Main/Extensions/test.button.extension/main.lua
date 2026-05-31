-- Test Button Extension
-- Adds a test button to the Tools menu that opens a new window

function onLoad()
    logInfo('Test Button Extension loaded!')
    
    -- Add menu item to Tools menu
    addMenuItem("Tools", "Test", "Click to test the extension functionality")
    
    -- Add translations for multiple languages
    addTranslation("en-US", "test_button_clicked", "You clicked test button")
    addTranslation("en-US", "test_window_title", "Test Window")
    addTranslation("en-US", "test_message", "This is a test window created by the extension!")
    addTranslation("es-ES", "test_button_clicked", "Hiciste clic en el botón de prueba")
    addTranslation("es-ES", "test_window_title", "Ventana de Prueba")
    addTranslation("es-ES", "test_message", "¡Esta es una ventana de prueba creada por la extensión!")
    addTranslation("fr-FR", "test_button_clicked", "Vous avez cliqué sur le bouton de test")
    addTranslation("fr-FR", "test_window_title", "Fenêtre de Test")
    addTranslation("fr-FR", "test_message", "Ceci est une fenêtre de test créée par l'extension!")
    
    logInfo("Test Button Extension loaded successfully! Look for 'Test' in the Tools menu.")
end

function onMenuItemClick(menuItemText)
    if menuItemText == "Test" then
        logInfo("Test button was clicked!")
        
        -- Get translated text
        local windowTitle = getTranslation("test_window_title", "Test Window")
        local message = getTranslation("test_message", "This is a test window created by the extension!")
        
        -- Create a new window
        createWindow(windowTitle, 500, 300)
        
        -- Log the action
        local buttonClickedMsg = getTranslation("test_button_clicked", "You clicked test button")
        logInfo(buttonClickedMsg)
        
        -- Trigger a custom event for demonstration
        triggerEvent("test.button.clicked", {
            message = message,
            timestamp = os.time(),
            windowTitle = windowTitle
        })
    end
end

function onUnload()
    logInfo('Test Button Extension unloading...')
    setSetting('test.extension.active', 'false')
    logInfo('Test Button Extension unloaded successfully')
end

-- Optional: Handle custom events if needed
function onTestButtonClicked(eventName, data)
    if data and data.message then
        logInfo("Custom event triggered: " .. data.message)
    end
end
