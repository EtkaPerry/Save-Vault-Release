<?php
/**
 * Database Table Structure Maintenance Utility
 * 
 * This file provides functions to verify and update database table structures
 * Use it to ensure all required columns are present in the database tables
 */

require_once 'db_config.php';

/**
 * Verify and update the notifications table structure
 * 
 * @return array Status of the operation
 */
function updateNotificationsTable() {
    global $conn;
    
    try {
        // Check if notifications table exists
        $tableExists = $conn->query("SHOW TABLES LIKE 'notifications'")->num_rows > 0;
        
        if (!$tableExists) {
            // Create notifications table if it doesn't exist
            $createTableSql = "
                CREATE TABLE notifications (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    admin_id INT NOT NULL,
                    message TEXT NOT NULL,
                    type VARCHAR(50) NOT NULL DEFAULT 'info',
                    link VARCHAR(255) DEFAULT '',
                    target_type VARCHAR(50) NOT NULL DEFAULT 'all',
                    target_id INT NULL,
                    is_read TINYINT(1) NOT NULL DEFAULT 0,
                    created_at DATETIME NOT NULL,
                    expires_at DATETIME NULL DEFAULT NULL,
                    priority INT NOT NULL DEFAULT 0,
                    FOREIGN KEY (admin_id) REFERENCES users(id) ON DELETE CASCADE
                )
            ";
            
            $conn->query($createTableSql);
            echo "Created notifications table<br>";
        } else {
            // Check and add missing columns
            $requiredColumns = [
                'expires_at' => 'DATETIME NULL DEFAULT NULL',
                'priority' => 'INT NOT NULL DEFAULT 0',
                'is_read' => 'TINYINT(1) NOT NULL DEFAULT 0',
                'target_id' => 'INT NULL'
            ];
            
            foreach ($requiredColumns as $column => $definition) {
                $result = $conn->query("SHOW COLUMNS FROM notifications LIKE '$column'");
                
                if ($result->num_rows === 0) {
                    // Column doesn't exist, create it
                    $alterQuery = "ALTER TABLE notifications ADD COLUMN $column $definition";
                    $conn->query($alterQuery);
                    echo "Added missing column: $column<br>";
                }
            }
        }
        
        return ['status' => 'success', 'message' => 'Notifications table structure verified and updated'];
    } catch (Exception $e) {
        return ['status' => 'error', 'message' => 'Error updating notifications table: ' . $e->getMessage()];
    }
}

// Check if this file is being accessed directly
if (basename($_SERVER['PHP_SELF']) == basename(__FILE__)) {
    // Run the table structure update when accessed directly with proper authentication
    
    // Simple authentication for maintenance scripts
    $authorized = false;
    
    // Check for a maintenance token in the request
    if (isset($_GET['maintenance_token'])) {
        // In a real implementation, you would use a secure token verification mechanism
        $validToken = hash('sha256', 'SaveVault_Maintenance_' . date('Ymd'));
        $authorized = hash_equals($validToken, $_GET['maintenance_token']);
    }
    
    if ($authorized) {
        header('Content-Type: text/html');
        echo "<h1>Database Structure Maintenance</h1>";
        
        // Update notifications table
        $result = updateNotificationsTable();
        echo "<p><strong>Notifications Table:</strong> {$result['message']}</p>";
        
        echo "<p>Maintenance completed at: " . date('Y-m-d H:i:s') . "</p>";
    } else {
        // Not authorized
        header('HTTP/1.0 403 Forbidden');
        echo "Access denied";
    }
}
?>
