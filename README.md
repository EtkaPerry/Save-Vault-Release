# Save Vault

![Save Vault Logo](https://img.shields.io/badge/Save%20Vault-Game%20Save%20Manager-blue)
[![License: AGPL v3](https://img.shields.io/badge/License-AGPL%20v3-yellow.svg)](https://www.gnu.org/licenses/agpl-3.0)
![Status](https://img.shields.io/badge/Status-Alpha-orange)

Save Vault is an automatic game save backup manager that helps protect your precious game progress from corruption, accidental deletion, or hardware failures.

> **Note:** This is my first C# project. While I've tried to follow best practices, there may be areas for improvement as I continue to learn.
>
> **Alpha Status:** Save Vault is currently in alpha. Many configuration options are exposed to help identify and resolve issues. Some features are still under development.

## 🎮 About

Save Vault automatically detects installed games on your system and creates backups of your save files on a customizable schedule. Never lose hours of gameplay again!

![Save Vault Screenshot](http://vault.etka.co.uk/img/SaveVaultApp.png)

## ✨ Features

- **Automatic Game Detection**: Scans your system for installed games and applications
- **Intelligent Save Path Detection**: Automatically locates save directories for popular games
- **Customizable Backup Schedule**:
  - Auto-save backups at adjustable intervals
  - Start-save backups when launching games
  - Manual save option whenever needed
- **Game-Specific Settings**: Configure unique backup settings for each game
- **Backup Management**:
  - Color-coded backup history
  - Configurable retention policies
  - Easy restore functionality
- **System Tray Integration**: Runs in the background with minimal resource usage
- **Dark/Light Theme Support**: Adapts to your system preferences
- **Cloud Sync**: Synchronize your save backups across multiple devices (with account)
- **Detailed Logging**: Comprehensive logging system for troubleshooting

## 🚀 Getting Started

### Prerequisites

- Windows 10/11
- .NET 9.0 or later

### Installation

1. Download the latest release from [GitHub Releases](https://github.com/yourusername/Save-Vault-Release/releases)
2. Extract the ZIP file to a location of your choice
3. Run `SaveVaultApp.exe`

## 🔧 Usage

1. **First Launch**: Save Vault will scan your system for games and applications
2. **Main Interface**: View detected games, customize settings, and manage backups
3. **Game Selection**: Click on a game to see its settings and backup history
4. **Custom Settings**: Toggle custom settings to override global backup configuration
5. **Manual Backup**: Use the "Save Now" button to create an immediate backup
6. **Restore**: Select a backup from history and click "Restore" to revert to that save

## ⚙️ Configuration

### Global Settings

- **Auto-Save Interval**: Default time between automatic backups (15 minutes by default)
- **Maximum Auto-Saves**: Number of automatic backups to keep per game (5 by default)
- **Maximum Start-Saves**: Number of launch-time backups to keep per game (3 by default)
- **Backup Location**: Where your backups are stored (configurable in Options)

### Per-Game Settings

- **Custom Name**: Rename detected games for better organization
- **Custom Save Path**: Manually set the save directory if auto-detection fails
- **Hide Game**: Remove games from the main list (still accessible in Hidden Games section)
- **Game-Specific Backup Settings**: Override global settings for individual games

## 🔒 Security Notes

Save Vault includes optional cloud sync capabilities, which requires authentication. Your data is:
- Encrypted during transmission using HTTPS
- Protected by JWT token authentication
- Only accessible with your credentials

However, as this is my first C# project, I recommend:
- Using a unique password not shared with critical accounts
- Being cautious with sensitive save data

## ⚠️ Limitations

- Save Vault works best with games that store saves in standard locations
- Some games with unusual save mechanisms may not be fully compatible
- Cloud sync requires an internet connection and valid account
- As a alpha product, many features are still in development or may not work as expected
- For a complete list of planned features and known issues, please check the [project board](https://github.com/users/EtkaPerry/projects/6)

## 🔄 Sync Release Information

This repository is automatically synced from a private development repository. Changes are filtered through a GitHub Action workflow to remove sensitive information and development artifacts before being published to the public repository.

## 🛠️ Technical Details

- Built with C# and Avalonia UI framework for cross-platform compatibility
- Uses reactive programming patterns with ReactiveUI
- Implements MVVM architecture for clean separation of concerns
- Includes comprehensive logging and error handling (hopefully catching most things!)

## 🤝 Contributing

Contributions are welcome! Since this is my learning project, I'm open to suggestions and improvements. Don't hesitate to clone the repo, make your changes, and push them back – I appreciate any help!

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📝 License

This project is licensed under the GNU Affero General Public License v3.0 (AGPLv3) - see the [LICENSE](LICENSE) file for details.

The AGPLv3 license ensures that:
- Anyone who uses your software over a network must be able to receive the source code
- Modifications to the software must be released under the same license
- Patents related to the software are automatically licensed to all users