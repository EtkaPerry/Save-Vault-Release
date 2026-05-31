-- Turkish Language Pack for SaveVault
-- Türkçe dil paketi

function onLoad()
    logInfo("=== TURKISH EXTENSION: onLoad() called ===")
    
    -- Set the extension language context
    setExtensionLanguage("tr-TR")
    
    -- Register Turkish language
    logInfo("Attempting to register Turkish language...")
    local success = registerLanguage("tr-TR", "Türkçe")
    if success then
        logInfo("Türkçe dil desteği başarıyla kaydedildi")
    else
        logError("Türkçe dil desteği kaydedilemedi")
        return
    end
    
    logInfo("Starting to add translations...")
    
    -- Add translations for extension use (Turkish)
    addTranslation("tr-TR", "turkish_language_loaded", "Türkçe dil paketi başarıyla yüklendi")
    addTranslation("tr-TR", "language_changed_to_turkish", "Dil Türkçe olarak değiştirildi")
    
    -- Add extension display names and descriptions for different languages
    addTranslation("en-US", "theme_name", "Turkish")
    addTranslation("en-US", "theme_description", "Turkish language pack for SaveVault")
    addTranslation("en-US", "language_pack_name", "Turkish Language Pack")
    
    addTranslation("tr-TR", "theme_name", "Türkçe")
    addTranslation("tr-TR", "theme_description", "SaveVault için Türkçe dil paketi")
    addTranslation("tr-TR", "language_pack_name", "Türkçe Dil Paketi")
    
    addTranslation("de-DE", "theme_name", "Türkisch")
    addTranslation("de-DE", "theme_description", "Türkisches Sprachpaket für SaveVault")
    addTranslation("de-DE", "language_pack_name", "Türkisches Sprachpaket")
    
    addTranslation("fr-FR", "theme_name", "Turc")
    addTranslation("fr-FR", "theme_description", "Pack de langue turque pour SaveVault")
    addTranslation("fr-FR", "language_pack_name", "Pack de Langue Turque")
    
    addTranslation("es-ES", "theme_name", "Turco")
    addTranslation("es-ES", "theme_description", "Paquete de idioma turco para SaveVault")
    addTranslation("es-ES", "language_pack_name", "Paquete de Idioma Turco")
    
    -- Example: Add translations using extension's default language (tr-TR)
    addTranslationDefault("welcome_message", "Hoş geldiniz!")
    addTranslationDefault("extension_loaded", "Eklenti başarıyla yüklendi")
    addTranslationDefault("restart_required", "Yeniden başlatma gerekli")
    addTranslationDefault("settings_saved", "Ayarlar kaydedildi")
    
    -- Register UI text replacements for main application
    logInfo("Registering UI text replacements for Turkish...")
    
    -- Menu items from MainWindow
    replaceUIText("File", "Dosya")
    replaceUIText("Exit", "Çıkış")
    replaceUIText("Tools", "Araçlar")
    replaceUIText("Log Viewer", "Log Görüntüleyici")
    replaceUIText("Manage extensions and plugins", "Eklentileri ve pluginleri yönet")
    
    -- Menu items
    replaceUIText("A-Z", "A-Z")
    replaceUIText("Z-A", "Z-A")
    replaceUIText("Launch Game", "Oyunu Başlat")
    replaceUIText("Launch from Steam", "Steam'den Başlat")
    replaceUIText("Open in Store", "Mağazada Aç")
    replaceUIText("Uninstall", "Kaldır")    
    -- Tooltips
    replaceUIText("Backup and transfer game saves between devices", "Cihazlar arasında oyun kayıtlarını yedekleyin ve aktarın")
    replaceUIText("Manage extensions and plugins", "Eklentileri ve pluginleri yönet")
    
    -- Main menu items
    replaceUIText("File", "Dosya")
    replaceUIText("Tools", "Araçlar")
    replaceUIText("Help", "Yardım")
    replaceUIText("Extensions", "Eklentiler")
    replaceUIText("Options", "Seçenekler")
    replaceUIText("Exit", "Çıkış")
    
    -- Window titles
    replaceUIText("Extension Manager", "Eklenti Yöneticisi")
    
    -- Options window - sidebar items
    replaceUIText("General", "Genel")
    replaceUIText("Appearance", "Görünüm")
    replaceUIText("Storage", "Depolama")
    replaceUIText("Updates", "Güncellemeler")
    replaceUIText("Legal", "Yasal")
    replaceUIText("Credit", "Kredi")
    
    -- Options window - panel headers
    replaceUIText("General Settings", "Genel Ayarlar")
    replaceUIText("Storage Settings", "Depolama Ayarları")
    replaceUIText("Update Settings", "Güncelleme Ayarları")
    replaceUIText("Legal Documents", "Yasal Belgeler")
    replaceUIText("Credits", "Krediler")
    
    -- Options window - general settings
    replaceUIText("Auto-save every:", "Otomatik kayıt aralığı:")
    replaceUIText("minutes", "dakika")
    replaceUIText("Max auto-saves:", "Maksimum otomatik kayıt:")
    replaceUIText("Max start-saves:", "Maksimum başlangıç kayıtı:")
    replaceUIText("backups", "yedek")
    replaceUIText("Auto-save enabled:", "Otomatik kayıt etkin:")
    replaceUIText("Save on start:", "Başlangıçta kaydet:")
    replaceUIText("Change Detection:", "Değişiklik Algılama:")
    replaceUIText("Only backup when files change", "Sadece dosyalar değiştiğinde yedekle")
    replaceUIText("Changes are saved automatically", "Değişiklikler otomatik olarak kaydedilir")
    
    -- Options window - appearance
    replaceUIText("Language", "Dil")
    replaceUIText("Theme", "Tema")
    replaceUIText("Language:", "Dil:")
    replaceUIText("Theme:", "Tema:")
    replaceUIText("Extensions can provide additional language translations", "Eklentiler ek dil çevirileri sağlayabilir")
    
    -- Options window - storage
    replaceUIText("Backup storage location:", "Yedekleme depolama konumu:")
    replaceUIText("Choose where game saves will be backed up to", "Oyun kayıtlarının nereye yedekleneceğini seçin")
    replaceUIText("Browse...", "Gözat...")
    replaceUIText("Storage Usage:", "Depolama Kullanımı:")
    replaceUIText("View how much storage space each program is using", "Her programın ne kadar depolama alanı kullandığını görün")
    replaceUIText("Calculate Storage Usage", "Depolama Kullanımını Hesapla")
    replaceUIText("Calculating storage usage...", "Depolama kullanımı hesaplanıyor...")
    replaceUIText("Program", "Program")
    replaceUIText("Used Storage", "Kullanılan Depolama")
    replaceUIText("Application Management:", "Uygulama Yönetimi:")    replaceUIText("Reset the application cache to force a new scan for all applications.", "Tüm uygulamalar için yeni bir tarama zorlamak için uygulama önbelleğini sıfırlayın.")
    replaceUIText("Reset Program Cache", "Program Önbelleğini Sıfırla")
    replaceUIText("Reset all settings and restart the application. This will clear all preferences.", "Tüm ayarları sıfırlayın ve uygulamayı yeniden başlatın. Bu tüm tercihleri temizleyecektir.")
    replaceUIText("Reset All Settings", "Tüm Ayarları Sıfırla")
    
    -- Options window - updates
    replaceUIText("Automatically check for updates", "Güncellemeleri otomatik olarak kontrol et")
    replaceUIText("Check for updates every:", "Güncellemeleri kontrol et:")
    replaceUIText("hours", "saat")
    replaceUIText("Current Version", "Mevcut Sürüm")
    replaceUIText("Latest Version", "En Son Sürüm")
    replaceUIText("Last Checked", "Son Kontrol")
    replaceUIText("Check for Updates", "Güncellemeleri Kontrol Et")
    replaceUIText("Install Update", "Güncellemeyi Yükle")
    replaceUIText("What's New", "Yenilikler")
    replaceUIText("Release Notes", "Sürüm Notları")
    replaceUIText("Release Date:", "Yayın Tarihi:")
    
    -- Options window - legal
    replaceUIText("Terms of Service", "Hizmet Şartları")
    replaceUIText("Security Policy", "Güvenlik Politikası")
    replaceUIText("Privacy Policy", "Gizlilik Politikası")
    replaceUIText("You accepted these at", "Bunları kabul ettiğiniz tarih")
      -- Extension window
    replaceUIText("Extension Manager", "Eklenti Yöneticisi")
    replaceUIText("Discover and manage extensions for SaveVault", "SaveVault için eklentiler keşfedin ve yönetin")
    replaceUIText("Import", "İçe Aktar")
    replaceUIText("Refresh", "Yenile")
    replaceUIText("Search", "Arama")
    replaceUIText("Search extensions...", "Eklentileri ara...")
    replaceUIText("Categories", "Kategoriler")
    replaceUIText("Filters", "Filtreler")
    replaceUIText("Show Installed Only", "Sadece Yüklenenleri Göster")
    replaceUIText("INSTALLED", "YÜKLÜ")
    replaceUIText("ENABLED", "ETKİN")
    
    -- Extension actions
    replaceUIText("Install", "Yükle")
    replaceUIText("Uninstall", "Kaldır")
    replaceUIText("Enable", "Etkinleştir")
    replaceUIText("Disable", "Devre Dışı Bırak")
    replaceUIText("Author", "Yazar")
    replaceUIText("Version", "Sürüm")
    replaceUIText("Description", "Açıklama")
    replaceUIText("Category", "Kategori")
    
    -- Dialog titles and messages
    replaceUIText("Import Extension", "Eklenti İçe Aktar")    replaceUIText("Confirm Reset Program Cache", "Program Önbelleği Sıfırlamayı Onayla")
    replaceUIText("Confirm Reset All Settings", "Tüm Ayarları Sıfırlamayı Onayla")
    replaceUIText("Error Opening URL", "URL Açma Hatası")
    replaceUIText("Save Log File", "Log Dosyasını Kaydet")
    replaceUIText("Export Game Saves", "Oyun Kayıtlarını Dışa Aktar")
    replaceUIText("Import Game Saves", "Oyun Kayıtlarını İçe Aktar")
    replaceUIText("Select Backup Storage Location", "Yedekleme Depolama Konumu Seç")
    replaceUIText("Select Save Location", "Kayıt Konumu Seç")
    replaceUIText("Select Application Location Folder", "Uygulama Konum Klasörü Seç")
    replaceUIText("Select Application Executable", "Uygulama Çalıştırılabilir Dosyası Seç")
    replaceUIText("Select Save Location Folder", "Kayıt Konum Klasörü Seç")
    replaceUIText("Save Vault Already Running", "Save Vault Zaten Çalışıyor")
    replaceUIText("Extensions Modified - Restart Required", "Eklentiler Değiştirildi - Yeniden Başlatma Gerekli")
    
    -- Common buttons
    replaceUIText("OK", "Tamam")
    replaceUIText("Cancel", "İptal")
    replaceUIText("Close", "Kapat")
    replaceUIText("Save", "Kaydet")
    replaceUIText("Yes", "Evet")
    replaceUIText("No", "Hayır")
    replaceUIText("Browse...", "Gözat...")
    replaceUIText("Restart", "Yeniden Başlat")
    replaceUIText("Not Now", "Şimdi Değil")
    replaceUIText("Terminate existing and start new", "Mevcut olanı sonlandır ve yenisini başlat")
    replaceUIText("Close this instance", "Bu örneği kapat")
    replaceUIText("Restart application", "Uygulamayı yeniden başlat")
    
    -- Main window elements
    replaceUIText("Save Vault", "Save Vault")
    replaceUIText("Offline", "Çevrimdışı")
    replaceUIText("Online", "Çevrimiçi")
    replaceUIText("Running:", "Çalışan:")
    replaceUIText("Check for update", "Güncelleme kontrolü")
    replaceUIText("Download the Update", "Güncellemeyi İndir")
    replaceUIText("Notifications", "Bildirimler")
    replaceUIText(" (new)", " (yeni)")
    replaceUIText("Settings", "Ayarlar")
    replaceUIText("Home", "Ana Sayfa")
    replaceUIText("Installed Applications", "Yüklü Uygulamalar")
    replaceUIText("HIDDEN GAMES", "GİZLİ OYUNLAR")
    replaceUIText("Launch", "Başlat")
    replaceUIText("Hide", "Gizle")
    replaceUIText("Show", "Göster")
    replaceUIText("Login", "Giriş")
    replaceUIText("Select an application from the list", "Listeden bir uygulama seçin")
    replaceUIText("Save Carrier", "Kayıt Taşıyıcı")
    replaceUIText("Backup and transfer game saves between devices", "Cihazlar arasında oyun kayıtlarını yedekleyin ve aktarın")
    replaceUIText("Select games to include", "Dahil edilecek oyunları seçin")    
    -- Main window elements visible in screenshot
    replaceUIText("Save Now", "Şimdi Kaydet")
    replaceUIText("Save Backups", "Kayıt Yedekleri")
    replaceUIText("Save Type", "Kayıt Türü")
    replaceUIText("Time Passed", "Geçen Süre")
    replaceUIText("Actions", "İşlemler")
    replaceUIText("Application Details", "Uygulama Detayları")
    replaceUIText("Executable Path:", "Yürütülebilir Dosya Yolu:")
    replaceUIText("Installation Path:", "Kurulum Yolu:")
    replaceUIText("Save Path:", "Kayıt Yolu:")
    replaceUIText("Use Custom Settings", "Özel Ayarları Kullan")
    replaceUIText("Using global application settings", "Global uygulama ayarları kullanılıyor")
    replaceUIText("Off", "Kapalı")
    replaceUIText("On", "Açık")
    
    -- Application list sorting
    replaceUIText("Last Used", "Son Kullanılan")
    replaceUIText("Alphabetical", "Alfabetik")
    replaceUIText("Most Saves", "En Çok Kayıt")
    
    -- Save Carrier window
    replaceUIText("Compression Options", "Sıkıştırma Seçenekleri")
    replaceUIText("Compression Level", "Sıkıştırma Seviyesi")
    replaceUIText("None - No compression, fastest packing/unpacking but largest file size.", "Hiç - Sıkıştırma yok, en hızlı paketleme/açma ama en büyük dosya boyutu.")
    replaceUIText("Standard - Balanced compression with good file size reduction and reasonable speed.", "Standart - İyi dosya boyutu azaltma ve makul hız ile dengeli sıkıştırma.")
    replaceUIText("Maximum - Best compression with smallest file size, but slower packing/unpacking.", "Maksimum - En küçük dosya boyutu ile en iyi sıkıştırma, ancak daha yavaş paketleme/açma.")
    replaceUIText("Ready to export or import game save data", "Oyun kayıt verilerini dışa veya içe aktarmaya hazır")
    
    -- Terms window
    replaceUIText("Terms and Conditions", "Şartlar ve Koşullar")
    replaceUIText("Welcome to Save Vault! Before you continue, please read and accept our terms and conditions.", "Save Vault'a hoş geldiniz! Devam etmeden önce lütfen şartlar ve koşullarımızı okuyun ve kabul edin.")
    replaceUIText("I have read and accept the Privacy Policy", "Gizlilik Politikasını okudum ve kabul ediyorum")
    replaceUIText("I have read and accept the Terms of Service", "Hizmet Şartlarını okudum ve kabul ediyorum")
    replaceUIText("I have read and accept the Security Policy", "Güvenlik Politikasını okudum ve kabul ediyorum")
    
    -- Status messages
    replaceUIText("Ready", "Hazır")
    replaceUIText("Settings reset. Closing application...", "Ayarlar sıfırlandı. Uygulama kapatılıyor...")
    replaceUIText("Error resetting settings:", "Ayarları sıfırlama hatası:")
    replaceUIText("Selected all games", "Tüm oyunlar seçildi")
    replaceUIText("Deselected all games", "Tüm oyunlar seçimi kaldırıldı")
    replaceUIText("Inverted game selection", "Oyun seçimi tersine çevrildi")
    replaceUIText("Selected only games from KnownGames database", "Sadece KnownGames veritabanından oyunlar seçildi")
    replaceUIText("Preparing to export saves...", "Kayıtları dışa aktarmaya hazırlanıyor...")
    replaceUIText("No games selected for export", "Dışa aktarım için oyun seçilmedi")
    replaceUIText("Cannot export games without save locations:", "Kayıt konumları olmayan oyunlar dışa aktarılamaz:")
    replaceUIText("Error: Cannot access application window", "Hata: Uygulama penceresine erişilemiyor")
    replaceUIText("Export cancelled", "Dışa aktarım iptal edildi")
    replaceUIText("Error: Could not get local file path", "Hata: Yerel dosya yolu alınamadı")
    replaceUIText("Exporting", "Dışa aktarılıyor")
    replaceUIText("games with", "oyun")
    replaceUIText("compression...", "sıkıştırma ile...")
    replaceUIText("Export completed successfully! File size:", "Dışa aktarım başarıyla tamamlandı! Dosya boyutu:")
    replaceUIText("Error exporting saves", "Kayıtları dışa aktarma hatası")
    replaceUIText("Error:", "Hata:")
    replaceUIText("Preparing to import saves...", "Kayıtları içe aktarmaya hazırlanıyor...")
    replaceUIText("Import cancelled", "İçe aktarım iptal edildi")
    replaceUIText("Importing saves...", "Kayıtlar içe aktarılıyor...")
    replaceUIText("Import completed successfully!", "İçe aktarım başarıyla tamamlandı!")
    replaceUIText("games restored.", "oyun geri yüklendi.")
    replaceUIText("No games were imported", "Hiçbir oyun içe aktarılmadı")
    
    -- Policy viewer
    replaceUIText("A-", "A-")
    replaceUIText("A+", "A+")
    replaceUIText("A", "A")
    
    logInfo("UI text replacements registered")
    
    -- Subscribe to language change events
    subscribeToEvent("app.language.changed", "onLanguageChanged")
    
    logInfo("=== TURKISH EXTENSION: onLoad() completed successfully ===")
    
    -- Example of using translations
    local themeName = getTranslation("theme_name", "Turkish")
    local themeDesc = getTranslation("theme_description", "Turkish language pack for SaveVault")
    
    logInfo("Türkçe dil paketi başarıyla yüklendi. Mevcut dil: " .. getCurrentLanguage())
    logInfo("Theme name in current language: " .. themeName)
    logInfo("Theme description: " .. themeDesc)
end

function onUnload()
    -- Clean up registered language and UI text replacements for Turkish
    clearUITextReplacements("tr-TR")
    unregisterLanguage("tr-TR")
    
    logInfo("Türkçe dil paketi kaldırıldı ve UI metinleri temizlendi")
end

function onLanguageChanged(eventName, newLanguage)
    logInfo("=== TURKISH EXTENSION: onLanguageChanged() called ===")
    logInfo("Event: " .. eventName .. ", New Language: " .. newLanguage)
    
    if newLanguage == "tr-TR" then
        logInfo("Dil Türkçe olarak değiştirildi: " .. newLanguage)
        logInfo("Turkish language activated - all UI text replacements should now be applied")
    else
        logInfo("Dil değiştirildi: " .. newLanguage)
        logInfo("Language changed away from Turkish")
    end
end
