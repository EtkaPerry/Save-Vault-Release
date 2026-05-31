-- Absolute Darkness Theme
-- A theme that makes everything pitch black

function onLoad()
    logInfo('Absolute Darkness theme loaded. Waiting for user to select it...')
end

function applyTheme()
    -- Main Backgrounds - Pitch Black
    setThemeResource("TitleBarBackground", "#000000")
    setThemeResource("MainBackground", "#000000")
    setThemeResource("SidebarBackground", "#000000")
    setThemeResource("PanelBackground", "#050505") -- Slightly lighter to distinguish panels
    setThemeResource("StatusBarBackground", "#000000")
    setThemeResource("HeaderBackground", "#000000")
    
    -- List Items
    setThemeResource("ListItemBackground", "#0a0a0a")
    setThemeResource("ListItemBackgroundHover", "#1a1a1a")
    
    -- Controls
    setThemeResource("TextBoxBackground", "#0a0a0a")
    
    -- Text
    setThemeResource("TextColor", "#e0e0e0") -- Slightly dimmed white for less eye strain
    setThemeResource("SecondaryTextColor", "#808080")
    
    -- Accents
    setThemeResource("DividerColor", "#1a1a1a")
    
    logInfo('Absolute Darkness colors applied')
end

function onUnload()
    logInfo('Unloading Absolute Darkness theme...')
    -- The host application reverts theme resource overrides automatically when this
    -- theme is disabled or another theme is selected, so no manual revert is needed here.
end
