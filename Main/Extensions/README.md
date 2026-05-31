# SaveVault Extensions

This folder contains built-in extensions for SaveVault.

## Structure

Each extension should have its own subfolder containing:
- `manifest.json` - Extension metadata
- `main.lua` - Main script file
- Optional: `icon.png`, `README.md`, additional scripts

## Built-in Extensions

- **Complete Dark Mode** - A truly dark theme extension that provides deep black UI elements

## Development

Extensions are written in Lua and have access to a sandboxed API for:
- Logging
- Settings management  
- Theme resource modification
- File operations within the extension directory