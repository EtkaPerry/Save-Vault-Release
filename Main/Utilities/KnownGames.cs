using System.Collections.Generic;

namespace SaveVaultApp.Utilities
{
    /// <summary>
    /// Maintains a database of known games with their folder names, executables, and save paths.
    /// </summary>
    public static class KnownGames
    {        /// <summary>
        /// List of known games with their folder names, executables, and save paths.
        /// </summary>
        public static readonly List<KnownGameInfo> GamesList = new List<KnownGameInfo>
        {
            // Example format for easy adding for different type of game first have executable in path other is inside of the folder
            new KnownGameInfo
            {
                Name = "Cyberpunk 2077",
                GameFolder = "Cyberpunk 2077",
                Executable = "REDprelauncher.exe",
                SavePath = "%USERPROFILE%\\Saved Games\\CD Projekt Red\\Cyberpunk 2077"
            },            
            new KnownGameInfo
            {
                Name = "Abiotic Factor",
                GameFolder = "AbioticFactor",
                Executable = "AbioticFactor\\Binaries\\Win64\\AbioticFactor-Win64-Shipping.exe",
                SavePath = "%USERPROFILE%\\AppData\\Local\\AbioticFactor\\Saved"
            }
            // Additional games can be added here
        };
    }
}
