<?php
// Notifications API endpoint for Save Vault

// Include authentication API for shared functions
require_once 'config.php';
require_once 'db.php';
require_once 'jwt_helper.php';

// Set headers to allow cross-origin requests and specify content type
header('Access-Control-Allow-Origin: *');
header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With');
header('Access-Control-Max-Age: 86400'); // Cache preflight response for 24 hours
error_reporting(E_ALL);
ini_set('display_errors', 1);

// Handle preflight OPTIONS request
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit;
}

// Get the request URI and method
$requestUri = $_SERVER['REQUEST_URI'];
$requestMethod = $_SERVER['REQUEST_METHOD'];

// Add debugging
error_log("Notifications API - Request URI: " . $requestUri);
error_log("Notifications API - REQUEST_METHOD: " . $requestMethod);
error_log("Notifications API - QUERY_STRING: " . $_SERVER['QUERY_STRING']);

// Parse the request path
$path = parse_url($requestUri, PHP_URL_PATH);
error_log("Notifications API - Path from parse_url: " . $path);

// Extract endpoint from query string or path
$endpoint = '';
if (isset($_GET) && !empty($_GET)) {
    // Get the first key in the query string as the endpoint
    reset($_GET);
    $endpoint = key($_GET);
    error_log("Notifications API - Endpoint from query string key: '" . $endpoint . "'");
    
    // If query parameter is passed without a value (like ?admin), use it as the endpoint
    if ($endpoint === '0' && isset($_SERVER['QUERY_STRING'])) {
        $queryString = $_SERVER['QUERY_STRING'];
        if (!empty($queryString)) {
            // Extract the endpoint name (before any = sign)
            $parts = explode('=', $queryString, 2);
            $endpoint = $parts[0];
            error_log("Notifications API - Endpoint from query string without value: '" . $endpoint . "'");
        }
    }
}

// Special case for 'admin' in query string
if (strpos($_SERVER['QUERY_STRING'], 'admin') === 0) {
    $endpoint = 'admin';
    error_log("Notifications API - Setting endpoint to 'admin' based on query string");
}

// Get authorization header for protected routes
$authHeader = null;
if (isset($_SERVER['HTTP_AUTHORIZATION'])) {
    $authHeader = $_SERVER['HTTP_AUTHORIZATION'];
} elseif (isset($_SERVER['REDIRECT_HTTP_AUTHORIZATION'])) {
    $authHeader = $_SERVER['REDIRECT_HTTP_AUTHORIZATION'];
}

// Import sendResponse function if not already defined
if (!function_exists('sendResponse')) {
    function sendResponse($success, $message, $data = null, $statusCode = 200) {
        http_response_code($statusCode);
        
        // Clean output buffer to prevent any unwanted output
        if (ob_get_level()) ob_end_clean();
        
        $response = [
            'success' => $success,
            'message' => $message,
            'data' => $data
        ];
        
        echo json_encode($response, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        exit;
    }
}

// Import authenticateRequest function if not already defined
if (!function_exists('authenticateRequest')) {
    function authenticateRequest($authHeader) {
        if (!$authHeader) {
            return null;
        }
        
        // Extract token from Authorization header
        $token = null;
        if (strpos($authHeader, 'Bearer ') === 0) {
            $token = substr($authHeader, 7);
        }
        
        if (!$token) {
            return null;
        }
        
        // Validate token
        $userData = validateJWT($token);
        if (!$userData) {
            return null;
        }
        
        return $userData;
    }
}

// Route based on request method and endpoint
error_log("Notifications API - Endpoint for routing: '" . $endpoint . "'");
error_log("Notifications API - Request method: '" . $requestMethod . "'");

// Handle GET requests
if ($requestMethod === 'GET') {
    // Admin endpoint for getting admin-created notifications
    if ($endpoint === 'admin') {
        // Extract the auth token from headers
        $headers = getallheaders();
        $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
        
        // Authenticate the request
        $userData = authenticateRequest($authHeader);
        if (!$userData) {
            sendResponse(false, 'Authentication required', null, 401);
            exit;
        }
        
        // Check if user is admin
        if (!isset($userData->admin) || !$userData->admin) {
            sendResponse(false, 'Unauthorized: Admin privileges required', null, 403);
            exit;
        }
        
        // Get admin sent notifications (placeholder implementation)
        $notifications = getAdminSentNotifications();
        
        // Send the response
        sendResponse(true, 'Admin notifications retrieved successfully', $notifications);
    } 
    // Regular user notifications endpoint
    else {
        // Extract the auth token from headers
        $headers = getallheaders();
        $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
        
        // Authenticate the request
        $userData = authenticateRequest($authHeader);
        if (!$userData) {
            sendResponse(false, 'Authentication required', null, 401);
            exit;
        }
        
        // Get user ID from the authentication data
        $userId = $userData->id;
        
        // Get notifications for the user
        $notifications = getUserNotifications($userId);
        
        // Send the response
        sendResponse(true, 'Notifications retrieved successfully', $notifications);
    }
}
// Handle POST requests
elseif ($requestMethod === 'POST') {
    // Admin endpoint for creating notifications
    if ($endpoint === 'admin') {
        // Extract the auth token from headers
        $headers = getallheaders();
        $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
        
        // Authenticate the request and check admin privileges
        $userData = authenticateRequest($authHeader);
        if (!$userData) {
            sendResponse(false, 'Authentication required', null, 401);
            exit;
        }
        
        // Check if user is admin
        if (!isset($userData->admin) || !$userData->admin) {
            sendResponse(false, 'Unauthorized: Admin privileges required', null, 403);
            exit;
        }
        
        // Get request body
        $data = json_decode(file_get_contents('php://input'), true);
        
        // Validate required fields
        if (!isset($data['message'])) {
            sendResponse(false, 'Message is required', null, 400);
            exit;
        }
        
        // Validate message length (max 128 characters)
        if (strlen($data['message']) > 128) {
            sendResponse(false, 'Message must be no more than 128 characters', null, 400);
            exit;
        }
        
        // Validate message content for security (prevent malicious code)
        $message = $data['message'];
        // Check for potential XSS or injection patterns
        $maliciousPatterns = [
            '/<script/i',
            '/<\/script>/i',
            '/javascript:/i',
            '/on\w+=/i',  // onload, onclick, etc.
            '/\beval\s*\(/i',
            '/document\s*\./i',
            '/\balert\s*\(/i',
            '/\bexec\s*\(/i',
            '/\bsystem\s*\(/i',
            '/\bpassthru\s*\(/i',
            '/\bshell_exec\s*\(/i',
            '/\binclude\s*\(/i',
            '/\brequire\s*\(/i'
        ];
        
        foreach ($maliciousPatterns as $pattern) {
            if (preg_match($pattern, $message)) {
                sendResponse(false, 'Message contains potentially malicious code', null, 400);
                exit;
            }
        }
        
        // Sanitize the message for extra security
        $message = htmlspecialchars($message, ENT_QUOTES, 'UTF-8');
        $data['message'] = $message;
          // Create notification
        $notificationId = createNotification($data);
        
        // Check if notification was created successfully
        if ($notificationId > 0) {
            // Send the response
            sendResponse(true, 'Notification created successfully', ['id' => $notificationId]);
        } else {
            // There was an error creating the notification
            sendResponse(false, 'Failed to create notification. Please check server logs.', null, 500);
        }
    } else {
        sendResponse(false, 'Endpoint not found', null, 404);
    }
}
// Handle PUT requests
elseif ($requestMethod === 'PUT') {
    // Extract the auth token from headers
    $headers = getallheaders();
    $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
    
    // Authenticate the request
    $userData = authenticateRequest($authHeader);
    if (!$userData) {
        sendResponse(false, 'Authentication required', null, 401);
        exit;
    }
    
    // Get user ID from the authentication data
    $userId = $userData->id;
    
    // Mark all notifications as read
    if (strpos($path, '/notifications_api.php/read-all') !== false) {
        $success = markAllNotificationsAsRead($userId);
        if ($success) {
            sendResponse(true, 'All notifications marked as read');
        } else {
            sendResponse(false, 'Failed to mark all notifications as read', null, 500);
        }
    }
    // Mark specific notification as read
    elseif (preg_match('#/notifications_api.php/(\d+)/read#', $path, $matches)) {
        $notificationId = (int)$matches[1];
        $success = markNotificationAsRead($userId, $notificationId);
        if ($success) {
            sendResponse(true, 'Notification marked as read');
        } else {
            sendResponse(false, 'Failed to mark notification as read', null, 500);
        }
    } else {
        sendResponse(false, 'Endpoint not found', null, 404);
    }
} else {
    sendResponse(false, 'Method not allowed', null, 405);
}

/**
 * Get notifications for a user
 */
function getUserNotifications($userId) {
    global $db;
    
    // Check if notifications table exists
    try {
        $result = $db->query("SHOW TABLES LIKE 'notifications'");
        $tableExists = $result->num_rows > 0;
        
        if (!$tableExists) {
            error_log("Notifications table does not exist - returning mock data");
            // If table doesn't exist, return mock data
            return [
                [
                    'id' => 1,
                    'message' => 'Welcome to Save Vault notifications!',
                    'date' => date('Y-m-d H:i:s'),
                    'is_read' => false,
                    'type' => 'info',
                    'link' => ''
                ]
            ];
        }
        
        // Get all global notifications and user-specific notifications
        $stmt = $db->prepare("
            SELECT n.id, n.message, n.link, n.type, n.created_at as date, n.expires_at, n.priority,
                   COALESCE(un.is_read, 0) as is_read
            FROM notifications n
            LEFT JOIN user_notifications un ON n.id = un.notification_id AND un.user_id = ?
            WHERE (n.target_type = 'all' OR (n.target_type = 'user' AND un.user_id = ?))
            AND (n.expires_at IS NULL OR n.expires_at > NOW())
            ORDER BY n.priority DESC, n.created_at DESC
            LIMIT 100
        ");
        
        $stmt->bind_param('ii', $userId, $userId);
        $stmt->execute();
        $result = $stmt->get_result();
        
        $notifications = [];
        while ($row = $result->fetch_assoc()) {
            // Convert is_read to boolean
            $row['is_read'] = (bool)$row['is_read'];
            $notifications[] = $row;
        }
        
        // Mark notifications as delivered if not already
        markNotificationsAsDelivered($userId, $notifications);
        
        return $notifications;
    } catch (Exception $e) {
        error_log("Error fetching notifications: " . $e->getMessage());
        // Return empty array in case of error
        return [];
    }
}

/**
 * Mark notifications as delivered for a user
 */
function markNotificationsAsDelivered($userId, $notifications) {
    global $db;
    
    // Check if user_notifications table exists
    try {
        $result = $db->query("SHOW TABLES LIKE 'user_notifications'");
        $tableExists = $result->num_rows > 0;
        
        if (!$tableExists) {
            error_log("User notifications table does not exist - cannot mark as delivered");
            return;
        }
        
        // Get current time
        $now = date('Y-m-d H:i:s');
        
        // Create or update user_notification records for delivered notifications
        foreach ($notifications as $notification) {
            // Check if a record already exists
            $stmt = $db->prepare("
                SELECT id FROM user_notifications 
                WHERE notification_id = ? AND user_id = ?
            ");
            $stmt->bind_param('ii', $notification['id'], $userId);
            $stmt->execute();
            $result = $stmt->get_result();
            
            if ($result->num_rows > 0) {
                // Already exists, only update if delivered_at is NULL
                $stmt = $db->prepare("
                    UPDATE user_notifications 
                    SET delivered_at = ? 
                    WHERE notification_id = ? AND user_id = ? AND delivered_at IS NULL
                ");
                $stmt->bind_param('sii', $now, $notification['id'], $userId);
                $stmt->execute();
            } else {
                // Create new record
                $stmt = $db->prepare("
                    INSERT INTO user_notifications 
                    (notification_id, user_id, delivered_at) 
                    VALUES (?, ?, ?)
                ");
                $stmt->bind_param('iis', $notification['id'], $userId, $now);
                $stmt->execute();
            }
        }
    } catch (Exception $e) {
        error_log("Error marking notifications as delivered: " . $e->getMessage());
    }
}

/**
 * Create a notification
 */
function createNotification($data) {
    global $db;
    
    // Check if notifications table exists
    try {
        // First, ensure the database connection is valid
        if ($db->ping() === false) {
            error_log("Database connection lost, attempting to reconnect");
            $db = new mysqli(DB_HOST, DB_USER, DB_PASSWORD, DB_NAME);
            if ($db->connect_error) {
                throw new Exception("Database reconnection failed: " . $db->connect_error);
            }
        }
        
        // Log the current query state
        error_log("Checking if notifications table exists");
        
        $result = $db->query("SHOW TABLES LIKE 'notifications'");
        if ($result === false) {
            throw new Exception("Error checking if notifications table exists: " . $db->error);
        }
        
        $tableExists = $result->num_rows > 0;
        error_log("Notifications table exists: " . ($tableExists ? "Yes" : "No"));
        
        if (!$tableExists) {
            error_log("Notifications table does not exist - attempting to create it");
            
            // Try to create the notifications table - removing the foreign key constraint for now
            $createNotificationsTable = "
                CREATE TABLE IF NOT EXISTS `notifications` (
                  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
                  `message` VARCHAR(128) NOT NULL,
                  `link` VARCHAR(255) DEFAULT NULL,
                  `type` ENUM('info', 'warning', 'update') DEFAULT 'info',
                  `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP,
                  `created_by` INT UNSIGNED NOT NULL,
                  `target_type` ENUM('all', 'user', 'admin') DEFAULT 'all',
                  `expires_at` DATETIME DEFAULT NULL,
                  `priority` TINYINT(1) DEFAULT 0,
                  PRIMARY KEY (`id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";
            
            $result = $db->query($createNotificationsTable);
            if ($result === false) {
                throw new Exception("Error creating notifications table: " . $db->error);
            }
            
            error_log("Successfully created notifications table");
            
            // Create the user_notifications table as well - removing the foreign keys for now
            $createUserNotificationsTable = "
                CREATE TABLE IF NOT EXISTS `user_notifications` (
                  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
                  `notification_id` INT UNSIGNED NOT NULL,
                  `user_id` INT UNSIGNED NOT NULL,
                  `is_read` TINYINT(1) DEFAULT 0,
                  `delivered_at` DATETIME DEFAULT NULL,
                  `read_at` DATETIME DEFAULT NULL,
                  PRIMARY KEY (`id`),
                  UNIQUE KEY `unique_notification_user` (`notification_id`, `user_id`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            ";
            
            $result = $db->query($createUserNotificationsTable);
            if ($result === false) {
                throw new Exception("Error creating user_notifications table: " . $db->error);
            }
            
            error_log("Successfully created user_notifications table");
        }
        
        // Get the admin user ID from the JWT token (which we already validated)
        $headers = getallheaders();
        $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
        $userData = authenticateRequest($authHeader);
        
        // Check if userData is valid
        if (!$userData) {
            throw new Exception("Invalid user authentication data");
        }
        
        // Extract admin ID from userData - using 'id' instead of 'sub'
        $adminId = 0;
        if (isset($userData->id)) {
            $adminId = $userData->id;
        } elseif (isset($userData->sub)) {
            $adminId = $userData->sub;
        } else {
            // Fallback to 1 if we can't determine the admin ID
            error_log("Warning: Could not determine admin ID, using default value 1");
            $adminId = 1;
        }
        
        error_log("Creating notification with admin ID: " . $adminId);
        
        // Set default values if not provided
        $message = $data['message'];
        $type = isset($data['type']) ? $data['type'] : 'info';
        $link = isset($data['link']) ? $data['link'] : '';
        $targetType = isset($data['target_type']) ? $data['target_type'] : 'all';
        $expiresAt = null;
        $priority = isset($data['priority']) && $data['priority'] ? 1 : 0;
        
        // Calculate expiration date if time-limited notification
        if (isset($data['time_limited']) && $data['time_limited']) {
            $expirationDays = isset($data['expiration_days']) && is_numeric($data['expiration_days']) 
                ? intval($data['expiration_days']) : 7; // Default 7 days
            $expiresAt = date('Y-m-d H:i:s', strtotime("+{$expirationDays} days"));
        }
        
        error_log("Notification data: message=$message, type=$type, link=$link, targetType=$targetType, priority=$priority, expiresAt=" . ($expiresAt ?? 'null'));
        
        // Insert the notification
        $stmt = $db->prepare("
            INSERT INTO notifications 
            (message, link, type, created_by, target_type, expires_at, priority) 
            VALUES (?, ?, ?, ?, ?, ?, ?)
        ");
        
        if ($stmt === false) {
            throw new Exception("Error preparing statement: " . $db->error);
        }
        
        $bindResult = $stmt->bind_param('sssissi', $message, $link, $type, $adminId, $targetType, $expiresAt, $priority);
        if ($bindResult === false) {
            throw new Exception("Error binding parameters: " . $stmt->error);
        }
        
        $executeResult = $stmt->execute();
        if ($executeResult === false) {
            throw new Exception("Error executing statement: " . $stmt->error);
        }
        
        // Get the inserted notification ID
        $notificationId = $db->insert_id;
        
        // Log the notification creation
        error_log("Successfully created notification ID $notificationId by admin ID $adminId: $message");
          return $notificationId;
    } catch (Exception $e) {
        error_log("Error creating notification: " . $e->getMessage());
        error_log("Stack trace: " . $e->getTraceAsString());
        
        // Return 0 to indicate error
        return 0;
    }
}

/**
 * Get notifications that were created by admins
 */
function getAdminSentNotifications() {
    global $db;
    
    // Check if notifications table exists
    try {
        $result = $db->query("SHOW TABLES LIKE 'notifications'");
        $tableExists = $result->num_rows > 0;
        
        if (!$tableExists) {
            error_log("Notifications table does not exist - returning mock data for admin");
            // If table doesn't exist, return mock data
            return [
                [
                    'id' => 100,
                    'message' => 'System-wide notification sent by admin',
                    'date' => date('Y-m-d H:i:s', strtotime('-1 day')),
                    'recipients' => 'all',
                    'type' => 'info'
                ]
            ];
        }
        
        // Get notifications created by current admin
        $headers = getallheaders();
        $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
        $userData = authenticateRequest($authHeader);
        $adminId = $userData->id ?? $userData->sub ?? 0;
        
        // Query to get all notifications created by this admin
        $stmt = $db->prepare("
            SELECT n.id, n.message, n.link, n.type, n.created_at as date, n.target_type as recipients,
                   n.expires_at, n.priority,
                   (SELECT COUNT(*) FROM user_notifications WHERE notification_id = n.id) as delivery_count,
                   (SELECT COUNT(*) FROM user_notifications WHERE notification_id = n.id AND is_read = 1) as read_count
            FROM notifications n
            WHERE n.created_by = ?
            ORDER BY n.created_at DESC
            LIMIT 100
        ");
        
        $stmt->bind_param('i', $adminId);
        $stmt->execute();
        $result = $stmt->get_result();
        
        $notifications = [];
        while ($row = $result->fetch_assoc()) {
            $notifications[] = $row;
        }
        
        return $notifications;
    } catch (Exception $e) {
        error_log("Error fetching admin notifications: " . $e->getMessage());
        // Return empty array in case of error
        return [];
    }
}

/**
 * Mark all notifications as read for a user
 */
function markAllNotificationsAsRead($userId) {
    global $db;
    
    try {
        // Check if tables exist
        $result = $db->query("SHOW TABLES LIKE 'notifications'");
        $notificationsTableExists = $result->num_rows > 0;
        
        $result = $db->query("SHOW TABLES LIKE 'user_notifications'");
        $userNotificationsTableExists = $result->num_rows > 0;
        
        if (!$notificationsTableExists || !$userNotificationsTableExists) {
            error_log("Required tables do not exist - cannot mark all as read");
            return false;
        }
        
        // Get current time
        $now = date('Y-m-d H:i:s');
        
        // Get all notifications
        $stmt = $db->prepare("
            SELECT id FROM notifications
            WHERE target_type = 'all' OR id IN (
                SELECT notification_id FROM user_notifications WHERE user_id = ?
            )
        ");
        $stmt->bind_param('i', $userId);
        $stmt->execute();
        $result = $stmt->get_result();
        
        // Mark each notification as read
        $success = true;
        while ($row = $result->fetch_assoc()) {
            $notificationId = $row['id'];
            
            // Check if a user_notification record exists
            $checkStmt = $db->prepare("
                SELECT id FROM user_notifications 
                WHERE notification_id = ? AND user_id = ?
            ");
            $checkStmt->bind_param('ii', $notificationId, $userId);
            $checkStmt->execute();
            $checkResult = $checkStmt->get_result();
            
            if ($checkResult->num_rows > 0) {
                // Update existing record
                $updateStmt = $db->prepare("
                    UPDATE user_notifications 
                    SET is_read = 1, read_at = ? 
                    WHERE notification_id = ? AND user_id = ?
                ");
                $updateStmt->bind_param('sii', $now, $notificationId, $userId);
                $updateSuccess = $updateStmt->execute();
            } else {
                // Create new record
                $insertStmt = $db->prepare("
                    INSERT INTO user_notifications 
                    (notification_id, user_id, is_read, read_at, delivered_at) 
                    VALUES (?, ?, 1, ?, ?)
                ");
                $insertStmt->bind_param('iiss', $notificationId, $userId, $now, $now);
                $updateSuccess = $insertStmt->execute();
            }
            
            if (!$updateSuccess) {
                $success = false;
                error_log("Failed to mark notification {$notificationId} as read for user {$userId}");
            }
        }
        
        return $success;
    } catch (Exception $e) {
        error_log("Error marking all notifications as read: " . $e->getMessage());
        return false;
    }
}

/**
 * Mark a specific notification as read for a user
 */
function markNotificationAsRead($userId, $notificationId) {
    global $db;
    
    try {
        // Check if user_notifications table exists
        $result = $db->query("SHOW TABLES LIKE 'user_notifications'");
        $tableExists = $result->num_rows > 0;
        
        if (!$tableExists) {
            error_log("User notifications table does not exist - cannot mark as read");
            return false;
        }
        
        // Get current time
        $now = date('Y-m-d H:i:s');
        
        // Check if a record already exists
        $stmt = $db->prepare("
            SELECT id FROM user_notifications 
            WHERE notification_id = ? AND user_id = ?
        ");
        $stmt->bind_param('ii', $notificationId, $userId);
        $stmt->execute();
        $result = $stmt->get_result();
        
        if ($result->num_rows > 0) {
            // Update existing record
            $stmt = $db->prepare("
                UPDATE user_notifications 
                SET is_read = 1, read_at = ? 
                WHERE notification_id = ? AND user_id = ?
            ");
            $stmt->bind_param('sii', $now, $notificationId, $userId);
            $success = $stmt->execute();
        } else {
            // Create new record
            $stmt = $db->prepare("
                INSERT INTO user_notifications 
                (notification_id, user_id, is_read, read_at, delivered_at) 
                VALUES (?, ?, 1, ?, ?)
            ");
            $stmt->bind_param('iiss', $notificationId, $userId, $now, $now);
            $success = $stmt->execute();
        }
        
        return $success;
    } catch (Exception $e) {
        error_log("Error marking notification as read: " . $e->getMessage());
        return false;
    }
}
?>