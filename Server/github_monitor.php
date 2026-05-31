<?php
/**
 * GitHub Extension Monitor
 * Daily script to check SaveVaultExtensions repository for new extensions
 * Should be run via cron job daily
 */

require_once __DIR__ . '/auth_config.php';
require_once __DIR__ . '/db_handler.php';

class GitHubExtensionMonitor {
    private $db;
    private $github_repo = 'EtkaPerry/SaveVaultExtensions';
    private $github_api_base = 'https://api.github.com';
    private $rate_limit_retry_hours = 3;
      public function __construct() {
        try {
            $this->db = get_db_connection();
            if (!$this->db) {
                throw new Exception("Failed to establish database connection");
            }
        } catch (Exception $e) {
            error_log("GitHubExtensionMonitor constructor error: " . $e->getMessage());
            throw new Exception("Database connection failed in GitHubExtensionMonitor: " . $e->getMessage());
        }
    }
    
    /**
     * Main monitoring function
     */
    public function monitor() {
        try {
            $this->log("Starting GitHub extension monitoring for repository: {$this->github_repo}");
            
            // Check if we need to wait due to rate limiting
            if ($this->shouldWaitForRateLimit()) {
                $this->log("Rate limit wait period not expired, skipping this run");
                return;
            }
            
            // Get repository contents
            $contents = $this->getRepositoryContents();
            if (!$contents) {
                $this->log("Failed to get repository contents", 'ERROR');
                return;
            }
              // Process each directory (potential extension or category)
            $processed = 0;
            $added = 0;
            $updated = 0;
            
            foreach ($contents as $item) {
                if ($item['type'] === 'dir') {
                    // Check if this is a category directory or extension directory
                    $categoryDirs = ['Official', 'Fixes', 'Localization', 'Theming', 'Other', 'Improvement'];
                    
                    if (in_array($item['name'], $categoryDirs)) {
                        // This is a category directory, process extensions within it
                        $categoryResult = $this->processCategoryDirectory($item['name']);
                        $processed += $categoryResult['processed'];
                        $added += $categoryResult['added'];
                        $updated += $categoryResult['updated'];
                    } else {
                        // This is a direct extension directory (legacy support)
                        $result = $this->processExtensionDirectory($item);
                        $processed++;
                        
                        if ($result === 'added') $added++;
                        elseif ($result === 'updated') $updated++;
                    }
                }
            }
            
            $this->log("Monitoring completed. Processed: $processed, Added: $added, Updated: $updated");
            
        } catch (Exception $e) {
            $this->log("Error during monitoring: " . $e->getMessage(), 'ERROR');
            
            // If this is a rate limit error, set wait time
            if (strpos($e->getMessage(), 'rate limit') !== false || strpos($e->getMessage(), '403') !== false) {
                $this->setRateLimitWait();
            }
        }
    }
    
    /**
     * Check if we should wait due to rate limiting
     */
    private function shouldWaitForRateLimit() {
        $stmt = $this->db->prepare("SELECT value FROM system_settings WHERE key_name = 'github_rate_limit_wait' LIMIT 1");
        $stmt->execute();
        $result = $stmt->fetch(PDO::FETCH_ASSOC);
        
        if (!$result) {
            return false;
        }
        
        $wait_until = strtotime($result['value']);
        return time() < $wait_until;
    }
    
    /**
     * Set rate limit wait time
     */
    private function setRateLimitWait() {
        $wait_until = date('Y-m-d H:i:s', strtotime("+{$this->rate_limit_retry_hours} hours"));
        
        $stmt = $this->db->prepare("
            INSERT INTO system_settings (key_name, value) 
            VALUES ('github_rate_limit_wait', ?) 
            ON DUPLICATE KEY UPDATE value = VALUES(value)
        ");
        $stmt->execute([$wait_until]);
        
        $this->log("Rate limit encountered, will retry after: $wait_until");
    }
    
    /**
     * Get repository contents from GitHub API
     */
    private function getRepositoryContents() {
        $url = "{$this->github_api_base}/repos/{$this->github_repo}/contents";
        
        $context = stream_context_create([
            'http' => [
                'method' => 'GET',
                'header' => [
                    'User-Agent: SaveVault-Extension-Monitor/1.0',
                    'Accept: application/vnd.github.v3+json'
                ],
                'timeout' => 30
            ]
        ]);
        
        $response = @file_get_contents($url, false, $context);
        if ($response === false) {
            $error = error_get_last();
            throw new Exception("Failed to fetch repository contents: " . ($error['message'] ?? 'Unknown error'));
        }
        
        $data = json_decode($response, true);
        if (!$data) {
            throw new Exception("Invalid JSON response from GitHub API");
        }
        
        // Check for API errors
        if (isset($data['message'])) {
            throw new Exception("GitHub API error: " . $data['message']);
        }
        
        return $data;
    }
      /**
     * Process a single extension directory
     */
    private function processExtensionDirectory($directory, $categoryPath = null) {
        $extension_id = $directory['name'];
        $this->log("Processing extension directory: $extension_id" . ($categoryPath ? " (in category: $categoryPath)" : ""));
        
        try {
            // Get manifest.json for this extension
            $manifest = $this->getExtensionManifest($extension_id, $categoryPath);
            if (!$manifest) {
                $this->log("No valid manifest found for $extension_id, skipping", 'WARNING');
                return 'skipped';
            }
            
            // Check if extension exists in database
            $existing = $this->getExistingExtension($extension_id);
            
            if ($existing) {
                // Check if it needs updating
                if ($this->needsUpdate($existing, $manifest, $directory)) {
                    return $this->updateExtension($existing['id'], $manifest, $directory, $categoryPath);
                } else {
                    // Just update the last check time
                    $this->updateLastCheck($existing['id']);
                    return 'unchanged';
                }
            } else {
                // Add new extension
                return $this->addNewExtension($extension_id, $manifest, $directory, $categoryPath);
            }
            
        } catch (Exception $e) {
            $this->log("Error processing extension $extension_id: " . $e->getMessage(), 'ERROR');
            return 'error';
        }
    }
      /**
     * Get extension manifest from GitHub
     */
    private function getExtensionManifest($extension_id, $categoryPath = null) {
        $path = $categoryPath ? "{$categoryPath}/{$extension_id}" : $extension_id;
        $url = "{$this->github_api_base}/repos/{$this->github_repo}/contents/{$path}/manifest.json";
        
        $context = stream_context_create([
            'http' => [
                'method' => 'GET',
                'header' => [
                    'User-Agent: SaveVault-Extension-Monitor/1.0',
                    'Accept: application/vnd.github.v3+json'
                ],
                'timeout' => 30
            ]
        ]);
        
        $response = @file_get_contents($url, false, $context);
        if ($response === false) {
            return null;
        }
        
        $data = json_decode($response, true);
        if (!$data || !isset($data['content'])) {
            return null;
        }
        
        // Decode base64 content
        $manifest_content = base64_decode($data['content']);
        $manifest = json_decode($manifest_content, true);
        
        // Validate required fields
        if (!$manifest || !isset($manifest['id']) || !isset($manifest['name'])) {
            return null;
        }
        
        return $manifest;
    }
    
    /**
     * Get existing extension from database
     */
    private function getExistingExtension($extension_id) {
        $stmt = $this->db->prepare("SELECT * FROM extensions WHERE extension_id = ?");
        $stmt->execute([$extension_id]);
        return $stmt->fetch(PDO::FETCH_ASSOC);
    }
    
    /**
     * Check if extension needs updating
     */
    private function needsUpdate($existing, $manifest, $directory) {
        // Check if version changed
        if ($existing['version'] !== ($manifest['version'] ?? '1.0.0')) {
            return true;
        }
        
        // Check if GitHub updated_at is newer
        $github_updated = strtotime($directory['updated_at'] ?? '');
        $db_updated = strtotime($existing['github_updated_at'] ?? '');
        
        if ($github_updated > $db_updated) {
            return true;
        }
        
        return false;
    }
      /**
     * Add new extension to database
     */
    private function addNewExtension($extension_id, $manifest, $directory, $categoryPath = null) {
        $stmt = $this->db->prepare("
            INSERT INTO extensions (
                extension_id, name, description, version, author, category, 
                github_url, icon_url, is_official, created_at, updated_at, 
                last_github_check, github_updated_at
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NOW(), NOW(), NOW(), ?)
        ");
        
        $path = $categoryPath ? "{$categoryPath}/{$extension_id}" : $extension_id;
        $github_url = "https://github.com/{$this->github_repo}/tree/main/{$path}";
        $icon_url = isset($manifest['icon']) ? 
            "https://raw.githubusercontent.com/{$this->github_repo}/main/{$path}/{$manifest['icon']}" : 
            null;
        
        // Use category from path or manifest
        $category = $categoryPath ? $this->mapCategory($categoryPath) : $this->mapCategory($manifest['category'] ?? 'Other');
        
        $stmt->execute([
            $extension_id,
            $manifest['name'] ?? $extension_id,
            $manifest['description'] ?? '',
            $manifest['version'] ?? '1.0.0',
            $manifest['author'] ?? 'Unknown',
            $category,
            $github_url,
            $icon_url,
            isset($manifest['isOfficial']) && $manifest['isOfficial'] ? 1 : 0,
            $directory['updated_at'] ?? date('Y-m-d H:i:s')
        ]);
        
        $this->log("Added new extension: {$manifest['name']} (ID: $extension_id)");
        return 'added';
    }    /**
     * Update existing extension
     */
    private function updateExtension($db_id, $manifest, $directory, $categoryPath = null) {
        $stmt = $this->db->prepare("
            UPDATE extensions SET 
                name = ?, description = ?, version = ?, author = ?, category = ?,
                icon_url = ?, is_official = ?, updated_at = NOW(), 
                last_github_check = NOW(), github_updated_at = ?
            WHERE id = ?
        ");
        
        $extension_id = $manifest['id'] ?? $directory['name'];
        $path = $categoryPath ? "{$categoryPath}/{$extension_id}" : $extension_id;
        $icon_url = isset($manifest['icon']) ? 
            "https://raw.githubusercontent.com/{$this->github_repo}/main/{$path}/{$manifest['icon']}" : 
            null;
        
        // Use category from path or manifest
        $category = $categoryPath ? $this->mapCategory($categoryPath) : $this->mapCategory($manifest['category'] ?? 'Other');
        
        $stmt->execute([
            $manifest['name'] ?? '',
            $manifest['description'] ?? '',
            $manifest['version'] ?? '1.0.0',
            $manifest['author'] ?? 'Unknown',
            $category,
            $icon_url,
            isset($manifest['isOfficial']) && $manifest['isOfficial'] ? 1 : 0,
            $directory['updated_at'] ?? date('Y-m-d H:i:s'),
            $db_id
        ]);
        
        $this->log("Updated extension: {$manifest['name']}");
        return 'updated';
    }
    
    /**
     * Update last check time
     */
    private function updateLastCheck($db_id) {
        $stmt = $this->db->prepare("UPDATE extensions SET last_github_check = NOW() WHERE id = ?");
        $stmt->execute([$db_id]);
    }
    
    /**
     * Map category names to valid enum values
     */
    private function mapCategory($category) {
        $valid_categories = ['Official', 'Fixes', 'Localization', 'Theming', 'Other'];
        
        // Normalize case
        $category = ucfirst(strtolower($category));
        
        if (in_array($category, $valid_categories)) {
            return $category;
        }
        
        return 'Other';
    }
    
    /**
     * Log messages
     */
    private function log($message, $level = 'INFO') {
        $timestamp = date('Y-m-d H:i:s');
        $log_message = "[$timestamp] [$level] $message" . PHP_EOL;
        
        // Log to file
        $log_file = __DIR__ . '/logs/github_monitor.log';
        @file_put_contents($log_file, $log_message, FILE_APPEND | LOCK_EX);
        
        // Also output to console if running from CLI
        if (php_sapi_name() === 'cli') {
            echo $log_message;
        }
    }
    
    /**
     * Create system_settings table if it doesn't exist
     */
    public function initializeSystemSettings() {
        $this->db->exec("
            CREATE TABLE IF NOT EXISTS system_settings (
                id INT UNSIGNED NOT NULL AUTO_INCREMENT,
                key_name VARCHAR(100) NOT NULL,
                value TEXT,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                PRIMARY KEY (id),
                UNIQUE KEY unique_key_name (key_name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci
        ");
    }
    
    /**
     * Process a category directory containing extensions
     */
    private function processCategoryDirectory($categoryName) {
        $this->log("Processing category directory: $categoryName");
        
        $processed = 0;
        $added = 0;
        $updated = 0;
        
        try {
            // Get contents of the category directory
            $categoryContents = $this->getCategoryContents($categoryName);
            if (!$categoryContents) {
                $this->log("Failed to get contents of category: $categoryName", 'WARNING');
                return ['processed' => 0, 'added' => 0, 'updated' => 0];
            }
            
            // Process each extension in the category
            foreach ($categoryContents as $item) {
                if ($item['type'] === 'dir') {
                    // Create a modified item with the full path for processing
                    $extensionItem = $item;
                    $extensionItem['category_path'] = $categoryName;
                    
                    $result = $this->processExtensionDirectory($extensionItem, $categoryName);
                    $processed++;
                    
                    if ($result === 'added') $added++;
                    elseif ($result === 'updated') $updated++;
                }
            }
            
        } catch (Exception $e) {
            $this->log("Error processing category $categoryName: " . $e->getMessage(), 'ERROR');
        }
        
        return ['processed' => $processed, 'added' => $added, 'updated' => $updated];
    }
    
    /**
     * Get contents of a category directory from GitHub API
     */
    private function getCategoryContents($categoryName) {
        $url = "{$this->github_api_base}/repos/{$this->github_repo}/contents/{$categoryName}";
        
        $context = stream_context_create([
            'http' => [
                'method' => 'GET',
                'header' => [
                    'User-Agent: SaveVault-Extension-Monitor/1.0',
                    'Accept: application/vnd.github.v3+json'
                ],
                'timeout' => 30
            ]
        ]);
        
        $response = @file_get_contents($url, false, $context);
        if ($response === false) {
            return null;
        }
        
        $data = json_decode($response, true);
        if (!$data) {
            return null;
        }
        
        // Check for API errors
        if (isset($data['message'])) {
            $this->log("GitHub API error for category $categoryName: " . $data['message'], 'WARNING');
            return null;
        }
          return $data;
    }
}

// Create logs directory if it doesn't exist
$logs_dir = __DIR__ . '/logs';
if (!is_dir($logs_dir)) {
    @mkdir($logs_dir, 0755, true);
}

// Run if called from command line
if (php_sapi_name() === 'cli') {
    $monitor = new GitHubExtensionMonitor();
    $monitor->initializeSystemSettings();
    $monitor->monitor();
}
?>