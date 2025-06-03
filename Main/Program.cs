using Avalonia;
using System;
using System.IO;
using System.Threading;
using System.Diagnostics;

namespace SaveVaultApp;

sealed class Program
{
    public static Mutex? ApplicationMutex { get; private set; }
    
    // Method to allow controlled access to the mutex
    public static void SetApplicationMutex(Mutex? mutex)
    {
        ApplicationMutex = mutex;
    }
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.    [STAThread]
    public static void Main(string[] args)
    {
        // Create a named mutex to prevent multiple instances
        const string mutexName = "SaveVaultApp_SingleInstance_Mutex";
        bool createdNew;
        
        // Check if we're explicitly marked as a second instance first
        bool explicitSecondInstance = Environment.GetEnvironmentVariable("SAVEVAULT_SECOND_INSTANCE") == "true";
        
        try
        {
            ApplicationMutex = new Mutex(true, mutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance terminated unexpectedly, but we can continue
            // The mutex is now available to us
            createdNew = true;
        }
        
        if (!createdNew && !explicitSecondInstance)
        {
            // Another instance is already running - let App handle this
            Environment.SetEnvironmentVariable("SAVEVAULT_SECOND_INSTANCE", "true");
        }
        else if (createdNew && explicitSecondInstance)
        {
            // We were marked as second instance but we got the mutex - clear the flag
            // This happens when the first instance terminated and we became the new first instance
            Environment.SetEnvironmentVariable("SAVEVAULT_SECOND_INSTANCE", null);
        }
        
        // Set default environment variable for offline mode only if this appears to be first run
        // Check if settings file exists to determine if this is a first run
        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SaveVault",
            "settings.json");
            
        if (!File.Exists(settingsPath))
        {
            Environment.SetEnvironmentVariable("SAVEVAULT_OFFLINE_MODE", "true");
        }
        
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Clean up single instance service
            SaveVaultApp.Services.SingleInstanceService.Cleanup();
            
            try
            {
                ApplicationMutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex was not owned by current thread, ignore
            }
            ApplicationMutex?.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
