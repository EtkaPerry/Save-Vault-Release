<?php
/**
 * Enhanced user authentication API
 * Save Vault API - Etka.co.uk
 * 
 * This file handles user authentication requests including:
 * - Login
 * - Registration
 * - Token validation
 * - Password reset
 * - Account management
 */

require_once 'config.php';
require_once 'db.php';
require_once 'jwt_helper.php';

// Set headers to allow cross-origin requests and specify content type
header('Access-Control-Allow-Origin: *');
header('Content-Type: application/json; charset=utf-8');
header('Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');
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

// Parse the request path
$path = parse_url($requestUri, PHP_URL_PATH);
$path = str_replace('/api/', '', $path);

// Get authorization header for protected routes
$authHeader = null;
if (isset($_SERVER['HTTP_AUTHORIZATION'])) {
    $authHeader = $_SERVER['HTTP_AUTHORIZATION'];
} elseif (isset($_SERVER['REDIRECT_HTTP_AUTHORIZATION'])) {
    $authHeader = $_SERVER['REDIRECT_HTTP_AUTHORIZATION'];
}

// Add this function at the top level
function sendResponse($success, $message, $data = null, $statusCode = 200) {
    http_response_code($statusCode);
    
    // Clean output buffer to prevent any unwanted output
    if (ob_get_level()) ob_end_clean();
    
    $response = [
        'success' => $success,
        'message' => $message,
        'data' => $data
    ];
    
    $json = json_encode($response, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    
    if ($json === false) {
        // If JSON encoding fails, send an error response
        $error = [
            'success' => false,
            'message' => 'JSON encoding error: ' . json_last_error_msg(),
            'data' => null
        ];
        echo json_encode($error);
    } else {
        echo $json;
    }
    exit;
}

// Route the request based on path and method
switch ($path) {
    case 'login':
        if ($requestMethod === 'POST') {
            handleLogin();
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'register':
        if ($requestMethod === 'POST') {
            handleRegistration();
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'validate':
        if ($requestMethod === 'GET') {
            handleTokenValidation($authHeader);
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'refresh':
        if ($requestMethod === 'POST') {
            handleTokenRefresh($authHeader);
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'sync':
        if ($requestMethod === 'GET') {
            handleGetUserData($authHeader);
        } elseif ($requestMethod === 'POST') {
            handleSyncData($authHeader);
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'profile':
        if ($requestMethod === 'PUT') {
            handleUpdateProfile($authHeader);
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'forgot-password':
        if ($requestMethod === 'POST') {
            handleForgotPassword();
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    default:
        sendResponse(false, 'Endpoint not found', null, 404);
}

/**
 * Handle user login
 */
function handleLogin() {
    // Get request body
    $data = json_decode(file_get_contents('php://input'), true);
    
    // Validate required fields
    if (!isset($data['usernameOrEmail']) || !isset($data['password'])) {
        sendResponse(false, 'Username/email and password are required', null, 400);
        return;
    }
    
    $usernameOrEmail = $data['usernameOrEmail'];
    $password = $data['password'];
    
    global $db;
    
    // Determine if input is email or username
    $isEmail = filter_var($usernameOrEmail, FILTER_VALIDATE_EMAIL);
    $field = $isEmail ? 'email' : 'username';
    
    // Prepare and execute query
    $stmt = $db->prepare("SELECT id, username, email, password FROM users WHERE $field = ?");
    $stmt->bind_param('s', $usernameOrEmail);
    $stmt->execute();
    $result = $stmt->get_result();
    
    if ($result->num_rows === 0) {
        // User not found
        sleep(1); // Delay to prevent timing attacks
        sendResponse(false, 'Invalid username or password', null, 401);
        return;
    }
    
    $user = $result->fetch_assoc();
    
    // Verify password
    if (!password_verify($password, $user['password'])) {
        sendResponse(false, 'Invalid username or password', null, 401);
        return;
    }
    
    // Generate JWT token
    $tokenId = base64_encode(random_bytes(32));
    $issuedAt = time();
    $expire = $issuedAt + 3600; // 1 hour expiry
    
    $token = generateJWT($user['id'], $user['username'], $issuedAt, $expire);
    
    // Update last_login timestamp
    $stmt = $db->prepare("UPDATE users SET last_login = NOW() WHERE id = ?");
    $stmt->bind_param('i', $user['id']);
    $stmt->execute();
    
    // Return response with token
    $responseData = [
        'token' => $token,
        'username' => $user['username'],
        'email' => $user['email']
    ];
    
    sendResponse(true, 'Login successful', $responseData);
}

/**
 * Handle user registration
 */
function handleRegistration() {
    try {
        // Get request body
        $input = file_get_contents('php://input');
        if (empty($input)) {
            sendResponse(false, 'No data received', null, 400);
            return;
        }
        
        $data = json_decode($input, true);
        if ($data === null) {
            sendResponse(false, 'Invalid JSON received: ' . json_last_error_msg(), null, 400);
            return;
        }
        
        // Validate required fields
        if (!isset($data['username']) || !isset($data['email']) || !isset($data['password'])) {
            sendResponse(false, 'Username, email and password are required', null, 400);
            return;
        }
        
        // Validate required fields
        if (!isset($data['username']) || !isset($data['email']) || !isset($data['password'])) {
            sendResponse(false, 'Username, email and password are required', null, 400);
            return;
        }
        
        $username = trim($data['username']);
        $email = trim($data['email']);
        $password = $data['password'];
        
        // Validate inputs
        if (strlen($username) < 3) {
            sendResponse(false, 'Username must be at least 3 characters', null, 400);
            return;
        }
        
        if (!filter_var($email, FILTER_VALIDATE_EMAIL)) {
            sendResponse(false, 'Invalid email format', null, 400);
            return;
        }
        
        if (strlen($password) < 8) {
            sendResponse(false, 'Password must be at least 8 characters', null, 400);
            return;
        }
        
        global $db;
        
        // Check if username already exists
        $stmt = $db->prepare("SELECT id FROM users WHERE username = ?");
        $stmt->bind_param('s', $username);
        $stmt->execute();
        if ($stmt->get_result()->num_rows > 0) {
            sendResponse(false, 'Username already exists', null, 409);
            return;
        }
        
        // Check if email already exists
        $stmt = $db->prepare("SELECT id FROM users WHERE email = ?");
        $stmt->bind_param('s', $email);
        $stmt->execute();
        if ($stmt->get_result()->num_rows > 0) {
            sendResponse(false, 'Email already exists', null, 409);
            return;
        }
        
        // Hash password
        $hashedPassword = password_hash($password, PASSWORD_DEFAULT);
        
        // Insert new user
        $stmt = $db->prepare("INSERT INTO users (username, email, password, created_at) VALUES (?, ?, ?, NOW())");
        
        // Check if prepare failed
        if ($stmt === false) {
            sendResponse(false, 'Database prepare error: ' . $db->error, null, 500);
            return;
        }
        
        $stmt->bind_param('sss', $username, $email, $hashedPassword);
        
        if (!$stmt->execute()) {
            // Send detailed error message back
            sendResponse(false, 'Failed to register user: ' . $stmt->error, null, 500);
            return;
        }
        
        $userId = $db->insert_id;
        
        // Generate JWT token
        $issuedAt = time();
        $expire = $issuedAt + 3600; // 1 hour expiry
        
        $token = generateJWT($userId, $username, $issuedAt, $expire);
        
        // Create default user settings
        $stmt = $db->prepare("INSERT INTO user_settings (user_id, auto_sync, sync_interval, dark_mode) VALUES (?, 1, 60, 1)");
        $stmt->bind_param('i', $userId);
        $stmt->execute();
        
        // Return response with token
        $responseData = [
            'token' => $token,
            'username' => $username,
            'email' => $email
        ];
        
        sendResponse(true, 'Registration successful', $responseData, 201);
    } catch (Exception $e) {
        sendResponse(false, 'Server error: ' . $e->getMessage(), null, 500);
    }
}

/**
 * Validate token and return user information
 */
function handleTokenValidation($authHeader) {
    $token = extractToken($authHeader);
    
    if (!$token) {
        sendResponse(false, 'No token provided', null, 401);
        return;
    }
    
    $userData = validateJWT($token);
    
    if (!$userData) {
        sendResponse(false, 'Invalid or expired token', null, 401);
        return;
    }
    
    global $db;
    
    // Get user data
    $stmt = $db->prepare("SELECT username, email FROM users WHERE id = ?");
    $stmt->bind_param('i', $userData->userId);
    $stmt->execute();
    $result = $stmt->get_result();
    
    if ($result->num_rows === 0) {
        sendResponse(false, 'User not found', null, 404);
        return;
    }
    
    $user = $result->fetch_assoc();
    
    $responseData = [
        'username' => $user['username'],
        'email' => $user['email']
    ];
    
    sendResponse(true, 'Token valid', $responseData);
}

/**
 * Refresh authentication token
 */
function handleTokenRefresh($authHeader) {
    $token = extractToken($authHeader);
    
    if (!$token) {
        sendResponse(false, 'No token provided', null, 401);
        return;
    }
    
    $userData = validateJWT($token, true); // Allow expired tokens for refresh
    
    if (!$userData) {
        sendResponse(false, 'Invalid token', null, 401);
        return;
    }
    
    // If token is expired for more than 30 days, don't refresh
    if ($userData->exp < time() - 2592000) {
        sendResponse(false, 'Token expired - please login again', null, 401);
        return;
    }
    
    global $db;
    
    // Get user data
    $stmt = $db->prepare("SELECT id, username FROM users WHERE id = ?");
    $stmt->bind_param('i', $userData->userId);
    $stmt->execute();
    $result = $stmt->get_result();
    
    if ($result->num_rows === 0) {
        sendResponse(false, 'User not found', null, 404);
        return;
    }
    
    $user = $result->fetch_assoc();
    
    // Generate new token
    $issuedAt = time();
    $expire = $issuedAt + 3600; // 1 hour expiry
    
    $newToken = generateJWT($user['id'], $user['username'], $issuedAt, $expire);
    
    sendResponse(true, 'Token refreshed', ['token' => $newToken]);
}

/**
 * Get user data and sync settings
 */
function handleGetUserData($authHeader) {
    $userData = authenticateRequest($authHeader);
    if (!$userData) return;
    
    global $db;
    
    // Get user settings
    $stmt = $db->prepare("SELECT 
                          auto_sync, 
                          sync_interval, 
                          dark_mode,
                          reminder_enabled,
                          email_notifications
                          FROM user_settings 
                          WHERE user_id = ?");
    $stmt->bind_param('i', $userData->userId);
    $stmt->execute();
    $result = $stmt->get_result();
    
    if ($result->num_rows === 0) {
        // Create default settings if not exist
        $stmt = $db->prepare("INSERT INTO user_settings (user_id, auto_sync, sync_interval, dark_mode) VALUES (?, 1, 60, 1)");
        $stmt->bind_param('i', $userData->userId);
        $stmt->execute();
        
        $settings = [
            'autoSync' => true,
            'syncIntervalMinutes' => 60,
            'darkMode' => true,
            'reminderEnabled' => true,
            'emailNotifications' => true
        ];
    } else {
        $settingsRow = $result->fetch_assoc();
        
        $settings = [
            'autoSync' => (bool)$settingsRow['auto_sync'],
            'syncIntervalMinutes' => (int)$settingsRow['sync_interval'],
            'darkMode' => (bool)$settingsRow['dark_mode'],
            'reminderEnabled' => (bool)$settingsRow['reminder_enabled'],
            'emailNotifications' => (bool)$settingsRow['email_notifications']
        ];
    }
    
    // Get user save data
    $stmt = $db->prepare("SELECT save_data FROM user_saves WHERE user_id = ?");
    $stmt->bind_param('i', $userData->userId);
    $stmt->execute();
    $result = $stmt->get_result();
    
    $saveData = $result->num_rows > 0 ? json_decode($result->fetch_assoc()['save_data'], true) : [];
    
    // Prepare response
    $responseData = [
        'settings' => $settings,
        'data' => $saveData
    ];
    
    sendResponse(true, 'User data retrieved successfully', ['data' => $responseData]);
}

/**
 * Sync user data with server
 */
function handleSyncData($authHeader) {
    $userData = authenticateRequest($authHeader);
    if (!$userData) return;
    
    // Get request body
    $requestData = json_decode(file_get_contents('php://input'), true);
    
    if (!isset($requestData['data'])) {
        sendResponse(false, 'No data provided', null, 400);
        return;
    }
    
    $saveData = json_encode($requestData['data']);
    
    global $db;
    
    // Check if user already has save data
    $stmt = $db->prepare("SELECT id FROM user_saves WHERE user_id = ?");
    $stmt->bind_param('i', $userData->userId);
    $stmt->execute();
    $result = $stmt->get_result();
    
    if ($result->num_rows === 0) {
        // Insert new save data
        $stmt = $db->prepare("INSERT INTO user_saves (user_id, save_data, last_updated) VALUES (?, ?, NOW())");
        $stmt->bind_param('is', $userData->userId, $saveData);
    } else {
        // Update existing save data
        $stmt = $db->prepare("UPDATE user_saves SET save_data = ?, last_updated = NOW() WHERE user_id = ?");
        $stmt->bind_param('si', $saveData, $userData->userId);
    }
    
    if ($stmt->execute()) {
        sendResponse(true, 'Data synced successfully');
    } else {
        sendResponse(false, 'Failed to sync data', null, 500);
    }
}

/**
 * Update user profile information
 */
function handleUpdateProfile($authHeader) {
    $userData = authenticateRequest($authHeader);
    if (!$userData) return;
    
    // Get request body
    $data = json_decode(file_get_contents('php://input'), true);
    $updates = [];
    $params = [];
    $types = '';
    
    global $db;
    
    // Check if updating email
    if (isset($data['email'])) {
        $email = trim($data['email']);
        
        if (!filter_var($email, FILTER_VALIDATE_EMAIL)) {
            sendResponse(false, 'Invalid email format', null, 400);
            return;
        }
        
        // Check if email already exists for another user
        $stmt = $db->prepare("SELECT id FROM users WHERE email = ? AND id != ?");
        $stmt->bind_param('si', $email, $userData->userId);
        $stmt->execute();
        
        if ($stmt->get_result()->num_rows > 0) {
            sendResponse(false, 'Email already in use', null, 409);
            return;
        }
        
        $updates[] = "email = ?";
        $params[] = $email;
        $types .= 's';
    }
    
    // Check if updating password
    if (isset($data['password'])) {
        $password = $data['password'];
        
        if (strlen($password) < 8) {
            sendResponse(false, 'Password must be at least 8 characters', null, 400);
            return;
        }
        
        $hashedPassword = password_hash($password, PASSWORD_DEFAULT);
        $updates[] = "password = ?";
        $params[] = $hashedPassword;
        $types .= 's';
    }
    
    // If nothing to update
    if (empty($updates)) {
        sendResponse(false, 'No valid fields to update', null, 400);
        return;
    }
    
    // Build update query
    $updateQuery = "UPDATE users SET " . implode(", ", $updates) . " WHERE id = ?";
    $params[] = $userData->userId;
    $types .= 'i';
    
    $stmt = $db->prepare($updateQuery);
    $stmt->bind_param($types, ...$params);
    
    if ($stmt->execute()) {
        sendResponse(true, 'Profile updated successfully');
    } else {
        sendResponse(false, 'Failed to update profile', null, 500);
    }
}

/**
 * Handle forgot password request
 */
function handleForgotPassword() {
    // Get request body
    $data = json_decode(file_get_contents('php://input'), true);
    
    // Validate required fields
    if (!isset($data['username']) || !isset($data['email'])) {
        sendResponse(false, 'Username and email are required', null, 400);
        return;
    }
    
    $username = $data['username'];
    $email = $data['email'];
    
    global $db;
    
    // Check if user exists with matching username and email
    $stmt = $db->prepare("SELECT id FROM users WHERE username = ? AND email = ?");
    $stmt->bind_param('ss', $username, $email);
    $stmt->execute();
    
    if ($stmt->get_result()->num_rows === 0) {
        // For security, don't reveal that user doesn't exist
        sendResponse(true, 'If the information is correct, password reset instructions will be sent to your email');
        return;
    }
    
    // Generate reset token
    $resetToken = bin2hex(random_bytes(32));
    $tokenExpiry = date('Y-m-d H:i:s', strtotime('+1 hour'));
    
    // Store reset token
    $stmt = $db->prepare("UPDATE users SET reset_token = ?, reset_token_expires = ? WHERE username = ? AND email = ?");
    $stmt->bind_param('ssss', $resetToken, $tokenExpiry, $username, $email);
    $stmt->execute();
    
    // In a real app, you would send an email here
    // For this example, we'll just return success message
    sendResponse(true, 'If the information is correct, password reset instructions will be sent to your email');
}

/**
 * Extract JWT token from Authorization header
 */
function extractToken($authHeader) {
    if (!$authHeader || !preg_match('/^Bearer\s+(.*?)$/', $authHeader, $matches)) {
        return null;
    }
    
    return $matches[1];
}

/**
 * Authenticate request and return user data or send error response
 */
function authenticateRequest($authHeader) {
    $token = extractToken($authHeader);
    
    if (!$token) {
        sendResponse(false, 'No token provided', null, 401);
        return null;
    }
    
    $userData = validateJWT($token);
    
    if (!$userData) {
        sendResponse(false, 'Invalid or expired token', null, 401);
        return null;
    }
    
    return $userData;
}
