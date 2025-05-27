<?php
// ...existing code...

/**
 * Ensures the notifications table has the required structure
 * 
 * @return bool True if table structure is valid or was fixed
 */
function ensureNotificationsTableStructure() {
    global $conn;
    
    try {
        // Check and create required columns if they don't exist
        $requiredColumns = [
            'expires_at' => 'DATETIME NULL DEFAULT NULL',
            'priority' => 'INT NOT NULL DEFAULT 0'
        ];
        
        foreach ($requiredColumns as $column => $definition) {
            $tableCheckStmt = $conn->prepare("SHOW COLUMNS FROM notifications LIKE ?");
            $tableCheckStmt->bind_param("s", $column);
            $tableCheckStmt->execute();
            $result = $tableCheckStmt->get_result();
            
            if ($result->num_rows === 0) {
                // Column doesn't exist, create it
                $alterQuery = "ALTER TABLE notifications ADD COLUMN $column $definition";
                $conn->query($alterQuery);
                error_log("Added missing $column column to notifications table");
            }
        }
        
        return true;
    } catch (Exception $e) {
        error_log("Error ensuring notifications table structure: " . $e->getMessage());
        return false;
    }
}

/**
 * Creates a new notification
 * 
 * @param int $adminId The ID of the admin creating the notification
 * @param array $data Notification data (message, type, link, targetType, targetId, priority, expiresAt)
 * @return array Response with status and message
 */
function createNotification($adminId = null, $data = null) {
    global $conn;
    
    if (!$adminId) {
        $token = validateJwtToken();
        if (!$token) {
            return ['status' => 'error', 'message' => 'Unauthorized'];
        }
        $adminId = $token['user_id'];
    }
    
    if (!$data) {
        $data = json_decode(file_get_contents('php://input'), true);
    }
    
    // Log notification creation attempt with admin ID
    error_log("Creating notification with admin ID: " . $adminId);
    error_log("Notification data: " . http_build_query($data));
    
    // Validate required fields
    if (empty($data['message'])) {
        return ['status' => 'error', 'message' => 'Message is required'];
    }
    
    // Set default values for optional fields
    $type = $data['type'] ?? 'info';
    $link = $data['link'] ?? '';
    $targetType = $data['targetType'] ?? 'all';
    $targetId = $data['targetId'] ?? null;
    $priority = (int)($data['priority'] ?? 0);
    $expiresAt = !empty($data['expiresAt']) ? $data['expiresAt'] : null;
    
    try {
        // Ensure the notifications table has the required structure
        if (!ensureNotificationsTableStructure()) {
            throw new Exception("Failed to verify or update notifications table structure");
        }
        
        $stmt = $conn->prepare("
            INSERT INTO notifications 
            (admin_id, message, type, link, target_type, target_id, priority, created_at, expires_at) 
            VALUES (?, ?, ?, ?, ?, ?, ?, NOW(), ?)
        ");
        
        if (!$stmt) {
            throw new Exception("Prepare statement failed: " . $conn->error);
        }
        
        $stmt->bind_param("issssiis", $adminId, $data['message'], $type, $link, $targetType, $targetId, $priority, $expiresAt);
        
        if ($stmt->execute()) {
            $notificationId = $conn->insert_id;
            return ['status' => 'success', 'message' => 'Notification created successfully', 'id' => $notificationId];
        } else {
            throw new Exception("Failed to create notification: " . $stmt->error);
        }
    } catch (Exception $e) {
        error_log("Error creating notification: " . $e->getMessage());
        error_log("Stack trace: " . $e->getTraceAsString());
        return ['status' => 'error', 'message' => 'Failed to create notification: ' . $e->getMessage()];
    }
}

// ...existing code...
?>