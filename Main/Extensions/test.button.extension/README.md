# Test Button Extension

This is a simple test extension that demonstrates how users can create extensions for SaveVault.

## What it does

- Adds a "Test" button to the Tools menu
- When clicked, opens a new window with the message "You clicked test button"
- Supports multiple languages (English, Spanish, French)
- Logs actions to help with debugging

## How to use

1. The extension will automatically add a "Test" menu item to the Tools menu when loaded
2. Click on "Tools" > "Test" to see it in action
3. A new window will appear with a test message
4. Check the logs to see the extension activity

## Purpose

This extension serves as a proof of concept to demonstrate that users can create custom extensions that:
- Add UI elements to the application
- Create new windows
- Handle user interactions
- Support internationalization
- Integrate with the logging system

## Files

- `manifest.json` - Extension metadata and configuration
- `main.lua` - Main extension script with the functionality
- `README.md` - This documentation file

## Development Notes

This extension shows the basic structure and capabilities available to extension developers. It can be used as a template for creating more complex extensions.
