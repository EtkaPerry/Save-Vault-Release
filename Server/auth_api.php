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

require_once __DIR__ . '/security.php';
sv_init_api('GET, POST, PUT, DELETE, OPTIONS'); // error handling, JSON headers, CORS allow-list, preflight

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/db.php';
require_once __DIR__ . '/jwt_helper.php';

/**
 * Get Authorization header from various sources
 * @return string|null The Authorization header or null if not found
 */
function getAuthHeaderFromRequest() {
    $auth = null;
    
    // Method 1: Standard location
    if (isset($_SERVER['HTTP_AUTHORIZATION'])) {
        $auth = $_SERVER['HTTP_AUTHORIZATION'];
    }
    // Method 2: Apache specific environment variable
    elseif (isset($_SERVER['REDIRECT_HTTP_AUTHORIZATION'])) {
        $auth = $_SERVER['REDIRECT_HTTP_AUTHORIZATION'];
    }
    // Method 3: Try alternate capitalization
    elseif (isset($_SERVER['Authorization'])) {
        $auth = $_SERVER['Authorization'];
    }
    // Method 4: Try apache_request_headers()
    elseif (function_exists('apache_request_headers')) {
        $requestHeaders = apache_request_headers();
        $requestHeaders = array_combine(
            array_map('strtolower', array_keys($requestHeaders)), 
            array_values($requestHeaders)
        );
        if (isset($requestHeaders['authorization'])) {
            $auth = $requestHeaders['authorization'];
        }
    }
    if ($auth !== null && $auth !== '') {
        $GLOBALS['sv_auth_from_cookie'] = false;
        return $auth;
    }

    // Web UI fallback: the token is kept in an HttpOnly, Secure, SameSite=Strict
    // cookie that JavaScript cannot read, so the browser sends it automatically.
    // This is safe against CSRF because (a) SameSite=Strict stops the cookie being
    // sent on cross-site requests, and (b) authenticateRequest() additionally
    // requires an X-Requested-With header for cookie-authenticated state-changing
    // methods. Query-string tokens remain unsupported (they leak via logs/history).
    if (!empty($_COOKIE['auth_token'])) {
        $GLOBALS['sv_auth_from_cookie'] = true;
        return 'Bearer ' . $_COOKIE['auth_token'];
    }

    return null;
}

/**
 * Set the website session cookie holding the JWT. HttpOnly (not script-readable,
 * mitigates XSS token theft), Secure (HTTPS only) and SameSite=Strict (CSRF).
 */
function sv_set_session_cookie($token, $maxAgeSeconds) {
    setcookie('auth_token', $token, [
        'expires'  => $maxAgeSeconds > 0 ? time() + $maxAgeSeconds : 0,
        'path'     => '/',
        'secure'   => true,
        'httponly' => true,
        'samesite' => 'Strict',
    ]);
}

/** Expire the website session cookie (server-side logout). */
function sv_clear_session_cookie() {
    setcookie('auth_token', '', [
        'expires'  => time() - 3600,
        'path'     => '/',
        'secure'   => true,
        'httponly' => true,
        'samesite' => 'Strict',
    ]);
}

// Get the request URI and method (preflight already handled by sv_init_api).
$requestUri = $_SERVER['REQUEST_URI'];
$requestMethod = $_SERVER['REQUEST_METHOD'];

// Parse the request path
$path = parse_url($requestUri, PHP_URL_PATH);

// Extract final part of path after auth_api.php/
if (strpos($path, 'auth_api.php/') !== false) {
    $path = substr($path, strpos($path, 'auth_api.php/') + strlen('auth_api.php/'));
    error_log("Path after auth_api.php/: " . $path);
} else {// Check if there's a query string endpoint
if (isset($_GET) && !empty($_GET)) {
    // Get the first key in the query string as the endpoint
    reset($_GET);
    $path = key($_GET);
    error_log("Path from query string key: '" . $path . "'");
    error_log("Full query string: '" . $_SERVER['QUERY_STRING'] . "'");
    
    // If query parameter is passed without a value (like ?admin), use it as the path
    if ($path === '0' && isset($_SERVER['QUERY_STRING'])) {
        $queryString = $_SERVER['QUERY_STRING'];
        if (!empty($queryString)) {
            // Extract the endpoint name (before any = sign)
            $parts = explode('=', $queryString, 2);
            $path = $parts[0];
            error_log("Path from query string without value: '" . $path . "'");
        }
    }
} else {
    $path = '';
    error_log("No path found in URL");
}
}

// Clean up path by removing any additional slashes
$path = trim($path, '/');
error_log("Final path: " . $path);

// Handle additional URI format possibilities - Support both /api/login and just /login
if (strpos($path, 'api/') === 0) {
    $path = str_replace('api/', '', $path);
}

// Get authorization header for protected routes
$authHeader = getAuthHeaderFromRequest();

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
error_log("Path for routing: '" . $path . "'");
error_log("Request method: '" . $requestMethod . "'");

// Special case for 'admin' in query string
if (strpos($_SERVER['QUERY_STRING'], 'admin') === 0) {
    $path = 'admin';
    error_log("Setting path to 'admin' based on query string");
}

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

    case 'reset-password':
        if ($requestMethod === 'POST') {
            handleResetPassword();
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;

    case 'logout':
        if ($requestMethod === 'POST') {
            handleLogout();
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'upload-photo':
        if ($requestMethod === 'POST') {
            handleProfilePhotoUploadRequest($authHeader);
        } else {
            sendResponse(false, 'Method not allowed', null, 405);
        }
        break;
        
    case 'admin':
        if ($requestMethod === 'GET') {
            handleAdminRequest($authHeader);
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
    // Include IP utility functions
    require_once __DIR__ . '/ip_utils.php';

    // Throttle brute-force / credential-stuffing attempts per client IP.
    sv_rate_limit_or_die('login', 10, 300);

    // Get request body
    $data = json_decode(file_get_contents('php://input'), true);

    // Validate required fields
    if (!is_array($data) || !isset($data['usernameOrEmail']) || !isset($data['password'])) {
        sendResponse(false, 'Username/email and password are required', null, 400);
        return;
    }

    $usernameOrEmail = $data['usernameOrEmail'];
    $password = $data['password'];

    global $db;

    // Look the user up by email or username. The field name comes from a fixed
    // whitelist (not user input), so it is safe to interpolate.
    $isEmail = filter_var($usernameOrEmail, FILTER_VALIDATE_EMAIL);
    $field = $isEmail ? 'email' : 'username';

    $stmt = $db->prepare("SELECT id, username, email, password, is_admin FROM users WHERE $field = ?");
    $stmt->bind_param('s', $usernameOrEmail);
    $stmt->execute();
    $result = $stmt->get_result();
    $user = $result->fetch_assoc();

    // Always perform a password hash comparison, even when the account does not
    // exist, so the response time does not reveal whether a username/email is
    // registered (user-enumeration defence). The dummy hash below is a valid
    // bcrypt hash that no real password will match.
    $hash = $user['password'] ?? '$2y$10$CZCJTHVUrmMnoCsc8MEaRuUt6IpUvP.571mzyrz2Bj.cz1uE1AfYW';
    $passwordOk = password_verify($password, $hash);

    if (!$user || !$passwordOk) {
        sendResponse(false, 'Invalid username or password', null, 401);
        return;
    }
    
    // Generate JWT token. "Remember me" extends both the token lifetime and the
    // cookie; otherwise a short 1-hour session is used.
    $remember = !empty($data['remember']);
    $issuedAt = time();
    $tokenLifetime = $remember ? (30 * 24 * 3600) : 3600; // 30 days vs 1 hour
    $expire = $issuedAt + $tokenLifetime;

    // Include is_admin status in the JWT token
    $token = generateJWT($user['id'], $user['username'], $issuedAt, $expire, (bool)$user['is_admin']);
    
    // Get IP address
    $ipAddress = $_SERVER['REMOTE_ADDR'] ?? 'Unknown';
    
    // Get location data
    $locationData = getLocationFromIP($ipAddress);
    
    // Get browser and device info
    $userAgent = $_SERVER['HTTP_USER_AGENT'] ?? 'Unknown';
    $deviceInfo = parseUserAgent($userAgent);
    
    // Update last_login timestamp
    $stmt = $db->prepare("UPDATE users SET last_login = NOW(), last_login_ip = ? WHERE id = ?");
    $stmt->bind_param('si', $ipAddress, $user['id']);
    $stmt->execute();
    
    // Log login details in login_history table
    $stmt = $db->prepare("INSERT INTO login_history (user_id, ip_address, browser, os, device_type, country, city) VALUES (?, ?, ?, ?, ?, ?, ?)");
    $stmt->bind_param('issssss', 
        $user['id'], 
        $ipAddress, 
        $deviceInfo['browser'],
        $deviceInfo['os'],
        $deviceInfo['device_type'],
        $locationData['country'],
        $locationData['city']
    );
    $stmt->execute();
    
    // Return response with token and additional user info
    $responseData = [
        'token' => $token,
        'username' => $user['username'],
        'email' => $user['email'],
        'is_admin' => (bool)$user['is_admin'],
        'login_info' => [
            'ip' => $ipAddress,
            'location' => $locationData['city'] . ', ' . $locationData['country'],
            'browser' => $deviceInfo['browser'],
            'device' => $deviceInfo['device_type'] . ' (' . $deviceInfo['os'] . ')'
        ]
    ];

    // Set the HttpOnly session cookie used by the website (the token is also in the
    // body for the desktop client, which authenticates via the Authorization header).
    sv_set_session_cookie($token, $tokenLifetime);

    sendResponse(true, 'Login successful', $responseData);
}

/**
 * Handle user registration
 */
function handleRegistration() {
    // Limit automated mass account creation per client IP.
    sv_rate_limit_or_die('register', 5, 600);

    try {
        // Get request body
        $input = file_get_contents('php://input');
        if (empty($input)) {
            sendResponse(false, 'No data received', null, 400);
            return;
        }
        
        $data = json_decode($input, true);
        if (!is_array($data)) {
            sendResponse(false, 'Invalid request body', null, 400);
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
        
        // Check for at least one uppercase letter
        if (!preg_match('/[A-Z]/', $password)) {
            sendResponse(false, 'Password must contain at least one uppercase letter', null, 400);
            return;
        }
        
        // Check for at least one number
        if (!preg_match('/[0-9]/', $password)) {
            sendResponse(false, 'Password must contain at least one number', null, 400);
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
        
        // Get client IP
        $ip = $_SERVER['REMOTE_ADDR'] ?? null;
        
        // Insert new user
        $stmt = $db->prepare("INSERT INTO users (username, email, password, registration_ip, last_login, created_at) VALUES (?, ?, ?, ?, NOW(), NOW())");
        
        // Check if prepare failed
        if ($stmt === false) {
            error_log('auth_api register prepare error: ' . $db->error);
            sendResponse(false, 'Registration failed. Please try again later.', null, 500);
            return;
        }

        $stmt->bind_param('ssss', $username, $email, $hashedPassword, $ip);

        if (!$stmt->execute()) {
            error_log('auth_api register execute error: ' . $stmt->error);
            sendResponse(false, 'Registration failed. Please try again later.', null, 500);
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
        error_log('auth_api registration error: ' . $e->getMessage());
        sendResponse(false, 'An unexpected server error occurred. Please try again later.', null, 500);
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
    
    // Get user information
    $stmt = $db->prepare("SELECT 
                         username,
                         email,
                         profile_photo,
                         is_admin, 
                         created_at,
                         last_login,
                         last_login_ip
                         FROM users 
                         WHERE id = ?");
    $stmt->bind_param('i', $userData->userId);
    $stmt->execute();
    $userResult = $stmt->get_result();
    $userInfo = $userResult->fetch_assoc();
    
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
    
    // Get login history
    $stmt = $db->prepare("SELECT 
                        login_time,
                        ip_address, 
                        browser,
                        os,
                        device_type,
                        country,
                        city
                        FROM login_history 
                        WHERE user_id = ? 
                        ORDER BY login_time DESC
                        LIMIT 10");
    $stmt->bind_param('i', $userData->userId);
    $stmt->execute();
    $loginResult = $stmt->get_result();
    
    $loginHistory = [];
    while ($row = $loginResult->fetch_assoc()) {
        $loginHistory[] = $row;
    }
    
    // Prepare user object
    $userObject = [
        'username' => $userInfo['username'],
        'email' => $userInfo['email'],
        'is_admin' => (bool)$userInfo['is_admin'],
        'profile_photo' => $userInfo['profile_photo'],
        'created_at' => $userInfo['created_at'],
        'last_login' => $userInfo['last_login'],
        'last_login_ip' => $userInfo['last_login_ip']
    ];
    
    // Prepare response
    $responseData = [
        'user' => $userObject,
        'settings' => $settings,
        'login_history' => $loginHistory,
        'data' => [] // Empty array since we're not implementing cloud saves yet
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
    
    // We're not implementing cloud saves yet, but we'll update user settings if provided
    if (isset($requestData['data']['settings'])) {
        $settings = $requestData['data']['settings'];
        
        global $db;
        
        // Get the existing settings
        $stmt = $db->prepare("SELECT id FROM user_settings WHERE user_id = ?");
        $stmt->bind_param('i', $userData->userId);
        $stmt->execute();
        $result = $stmt->get_result();
        
        // Build the settings update
        $updateFields = [];
        $updateParams = [];
        $updateTypes = '';
        
        if (isset($settings['autoSync'])) {
            $updateFields[] = "auto_sync = ?";
            $updateParams[] = $settings['autoSync'] ? 1 : 0;
            $updateTypes .= 'i';
        }
        
        if (isset($settings['syncIntervalMinutes'])) {
            $updateFields[] = "sync_interval = ?";
            $updateParams[] = $settings['syncIntervalMinutes'];
            $updateTypes .= 'i';
        }
        
        if (isset($settings['darkMode'])) {
            $updateFields[] = "dark_mode = ?";
            $updateParams[] = $settings['darkMode'] ? 1 : 0;
            $updateTypes .= 'i';
        }
        
        if (isset($settings['reminderEnabled'])) {
            $updateFields[] = "reminder_enabled = ?";
            $updateParams[] = $settings['reminderEnabled'] ? 1 : 0;
            $updateTypes .= 'i';
        }
        
        if (isset($settings['emailNotifications'])) {
            $updateFields[] = "email_notifications = ?";
            $updateParams[] = $settings['emailNotifications'] ? 1 : 0;
            $updateTypes .= 'i';
        }
        
        // Only update if we have fields to update
        if (!empty($updateFields)) {
            if ($result->num_rows === 0) {
                // Create default settings if they don't exist
                $stmt = $db->prepare("INSERT INTO user_settings (user_id, auto_sync, sync_interval, dark_mode) VALUES (?, 1, 60, 1)");
                $stmt->bind_param('i', $userData->userId);
                $stmt->execute();
            }
            
            // Add the user_id to the params and types
            $updateParams[] = $userData->userId;
            $updateTypes .= 'i';
            
            // Update the settings
            $query = "UPDATE user_settings SET " . implode(", ", $updateFields) . " WHERE user_id = ?";
            $stmt = $db->prepare($query);
            
            // Bind parameters dynamically
            if (!empty($updateParams)) {
                $stmt->bind_param($updateTypes, ...$updateParams);
                $stmt->execute();
            }
        }
    }
    
    // Send success response - cloud saves are not implemented yet
    sendResponse(true, 'Settings synced successfully');
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
        
        // Check for at least one uppercase letter
        if (!preg_match('/[A-Z]/', $password)) {
            sendResponse(false, 'Password must contain at least one uppercase letter', null, 400);
            return;
        }
        
        // Check for at least one number
        if (!preg_match('/[0-9]/', $password)) {
            sendResponse(false, 'Password must contain at least one number', null, 400);
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
    // Limit abuse of the reset flow per client IP.
    sv_rate_limit_or_die('forgot', 5, 600);

    // Get request body
    $data = json_decode(file_get_contents('php://input'), true);

    // Email is required; username is accepted but optional (the web form sends email only).
    if (!is_array($data) || !isset($data['email']) || !filter_var($data['email'], FILTER_VALIDATE_EMAIL)) {
        sendResponse(false, 'A valid email address is required', null, 400);
        return;
    }

    $email = trim($data['email']);

    // The same generic response is returned in every branch so the endpoint never
    // reveals whether an address is registered (account-enumeration defence).
    $genericMessage = 'If that email is registered, password reset instructions will be sent to it shortly.';

    global $db;

    $stmt = $db->prepare("SELECT id FROM users WHERE email = ?");
    $stmt->bind_param('s', $email);
    $stmt->execute();
    $result = $stmt->get_result();

    if ($result->num_rows === 0) {
        sendResponse(true, $genericMessage);
        return;
    }

    $user = $result->fetch_assoc();

    // Single-use token: we email the raw token but store only its SHA-256 hash,
    // so a database read alone cannot be used to take over accounts.
    $rawToken    = bin2hex(random_bytes(32));
    $tokenHash   = hash('sha256', $rawToken);
    $tokenExpiry = date('Y-m-d H:i:s', strtotime('+1 hour'));

    $stmt = $db->prepare("UPDATE users SET reset_token = ?, reset_token_expires = ? WHERE id = ?");
    $stmt->bind_param('ssi', $tokenHash, $tokenExpiry, $user['id']);
    $stmt->execute();

    // Build the reset link from the hardcoded public origin (never the Host header).
    $resetLink = SV_PUBLIC_URL . '/reset-password?token=' . urlencode($rawToken);

    $body = "Hello,\r\n\r\n"
          . "We received a request to reset the password for your Save Vault account.\r\n\r\n"
          . "Use the link below to choose a new password (valid for 1 hour):\r\n"
          . $resetLink . "\r\n\r\n"
          . "If you did not request this, you can safely ignore this email and your password will stay the same.\r\n\r\n"
          . "— Save Vault";

    sv_send_mail($email, 'Reset your Save Vault password', $body);

    // Always return the same message regardless of whether mail() succeeded.
    sendResponse(true, $genericMessage);
}

/**
 * Log out: clear the HttpOnly session cookie server-side (JS cannot clear it).
 */
function handleLogout() {
    sv_clear_session_cookie();
    sendResponse(true, 'Logged out');
}

/**
 * Complete a password reset using a token that was emailed to the user.
 */
function handleResetPassword() {
    sv_rate_limit_or_die('reset', 10, 600);

    $data = json_decode(file_get_contents('php://input'), true);

    if (!is_array($data) || !isset($data['token']) || !isset($data['password'])) {
        sendResponse(false, 'Token and new password are required', null, 400);
        return;
    }

    $token    = (string) $data['token'];
    $password = (string) $data['password'];

    // Enforce the same password policy as registration.
    if (strlen($password) < 8) {
        sendResponse(false, 'Password must be at least 8 characters', null, 400);
        return;
    }
    if (!preg_match('/[A-Z]/', $password)) {
        sendResponse(false, 'Password must contain at least one uppercase letter', null, 400);
        return;
    }
    if (!preg_match('/[0-9]/', $password)) {
        sendResponse(false, 'Password must contain at least one number', null, 400);
        return;
    }

    // Tokens are stored hashed; hash the supplied token to look it up.
    $tokenHash = hash('sha256', $token);

    global $db;

    $stmt = $db->prepare("SELECT id FROM users WHERE reset_token = ? AND reset_token_expires IS NOT NULL AND reset_token_expires > NOW()");
    $stmt->bind_param('s', $tokenHash);
    $stmt->execute();
    $result = $stmt->get_result();

    if ($result->num_rows === 0) {
        sendResponse(false, 'This password reset link is invalid or has expired. Please request a new one.', null, 400);
        return;
    }

    $user = $result->fetch_assoc();

    // Set the new password and invalidate the token (single use).
    $hashedPassword = password_hash($password, PASSWORD_DEFAULT);
    $stmt = $db->prepare("UPDATE users SET password = ?, reset_token = NULL, reset_token_expires = NULL WHERE id = ?");
    $stmt->bind_param('si', $hashedPassword, $user['id']);

    if ($stmt->execute()) {
        sendResponse(true, 'Your password has been reset. You can now log in with your new password.');
    } else {
        error_log('auth_api reset-password update failed');
        sendResponse(false, 'Could not reset password. Please try again later.', null, 500);
    }
}

/**
 * Extract JWT token from Authorization header
 */
function extractToken($authHeader) {
    if (!$authHeader) {
        return null;
    }

    if (!preg_match('/^Bearer\s+(.*?)$/i', $authHeader, $matches)) {
        // Accept a raw JWT (three dot-separated segments) without a Bearer prefix.
        if (substr_count($authHeader, '.') === 2) {
            return $authHeader;
        }
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
        return null;
    }

    $userData = validateJWT($token);

    if (!$userData) {
        return null;
    }

    // CSRF defence for cookie-authenticated requests: state-changing methods must
    // carry an X-Requested-With header. A cross-site page cannot set that header
    // without a CORS preflight, which our origin allow-list rejects; combined with
    // the cookie's SameSite=Strict attribute this blocks CSRF. The desktop client
    // authenticates via the Authorization header and is unaffected.
    if (!empty($GLOBALS['sv_auth_from_cookie'])) {
        $method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
        if (!in_array($method, ['GET', 'HEAD', 'OPTIONS'], true)) {
            $xrw = $_SERVER['HTTP_X_REQUESTED_WITH'] ?? '';
            if (strcasecmp($xrw, 'XMLHttpRequest') !== 0) {
                sendResponse(false, 'Missing required request header', null, 403);
            }
        }
    }

    return $userData;
}

/**
 * Handle profile photo upload
 */
function handleProfilePhotoUploadRequest($authHeader) {
    // Include file upload utility (defines handleProfilePhotoUpload()).
    require_once __DIR__ . '/file_upload.php';

    $userData = authenticateRequest($authHeader);
    if (!$userData) {
        sendResponse(false, 'Authentication required', null, 401);
        return;
    }

    // Check if files were uploaded
    if (!isset($_FILES['photo']) || !is_array($_FILES['photo'])) {
        sendResponse(false, 'No file uploaded', null, 400);
        return;
    }

    // Process the uploaded file
    $result = handleProfilePhotoUpload($_FILES['photo'], $userData->userId);
    
    if (!$result['success']) {
        sendResponse(false, $result['message'], null, 400);
        return;
    }
    
    // Update user profile with new photo path
    global $db;
    $stmt = $db->prepare("UPDATE users SET profile_photo = ? WHERE id = ?");
    $stmt->bind_param('si', $result['path'], $userData->userId);
    
    if (!$stmt->execute()) {
        sendResponse(false, 'Failed to update profile photo', null, 500);
        return;
    }
    
    sendResponse(true, 'Profile photo uploaded successfully', [
        'photo_url' => $result['path']
    ]);
}

/**
 * Handle admin request with admin verification
 */
function handleAdminRequest($authHeader) {
    // Use our custom auth header retrieval function if no header was provided
    if (!$authHeader) {
        $authHeader = getAuthHeaderFromRequest();
    }

    $userData = authenticateRequest($authHeader);
    if (!$userData) {
        sendResponse(false, 'Authentication required', null, 401);
        return;
    }

    // Check if user is admin
    if (!isset($userData->admin) || !$userData->admin) {
        sendResponse(false, 'Unauthorized: Admin privileges required', null, 403);
        return;
    }

    global $db;
    
    // Get user statistics for admin dashboard
    $userStats = [];
    
    // Total users
    $stmt = $db->query("SELECT COUNT(*) as total FROM users");
    $userStats['total_users'] = $stmt->fetch_assoc()['total'];
    
    // Active users (last login within 30 days)
    $stmt = $db->query("SELECT COUNT(*) as active FROM users WHERE last_login >= DATE_SUB(NOW(), INTERVAL 30 DAY)");
    $userStats['active_users'] = $stmt->fetch_assoc()['active'];
    
    // New users in last 30 days
    $stmt = $db->query("SELECT COUNT(*) as new_users FROM users WHERE created_at >= DATE_SUB(NOW(), INTERVAL 30 DAY)");
    $userStats['new_users'] = $stmt->fetch_assoc()['new_users'];
    
    // Recent logins
    $stmt = $db->query("SELECT u.username, l.login_time, l.browser, l.os, l.device_type, l.country, l.city, l.ip_address 
                         FROM login_history l
                         JOIN users u ON l.user_id = u.id
                         ORDER BY l.login_time DESC
                         LIMIT 10");
    
    $recentLogins = [];
    while ($row = $stmt->fetch_assoc()) {
        $recentLogins[] = $row;
    }
    
    // Send admin dashboard data
    sendResponse(true, 'Admin data retrieved successfully', [
        'user_stats' => $userStats,
        'recent_logins' => $recentLogins
    ]);
}
