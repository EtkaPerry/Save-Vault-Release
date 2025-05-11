<?php
/**
 * Save Vault API
 * Main entry point for the API handling user authentication and data syncing
 * Moved from index.php
 */

header('Content-Type: application/json');
header('Access-Control-Allow-Origin: *');
header('Access-Control-Allow-Methods: GET, POST, OPTIONS');
header('Access-Control-Allow-Headers: Content-Type, Authorization');

// Handle preflight OPTIONS request
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit();
}

require_once 'config.php';
require_once 'db.php';
require_once 'jwt_helper.php';

// Get the request path
$requestUri = $_SERVER['REQUEST_URI'];
$endpoint = trim(parse_url($requestUri, PHP_URL_PATH), '/');
$endpoint = explode('/', $endpoint);

// Remove any API prefix if exists (like 'api')
if (isset($endpoint[0]) && $endpoint[0] === 'api') {
    array_shift($endpoint);
}

// Determine the API endpoint
$action = isset($endpoint[0]) ? $endpoint[0] : '';

// Process request based on endpoint
switch ($action) {
    case 'login':
        handleLogin();
        break;
    case 'register':
        handleRegister();
        break;
    case 'validate':
        validateToken();
        break;
    case 'sync':
        handleSync();
        break;
    case 'forgot-password':
        handleForgotPassword();
        break;
    default:
        sendResponse(false, "Endpoint not found", null, 404);
        break;
}

/**
 * Handles user login
 */
function handleLogin() {
    if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
        sendResponse(false, "Method not allowed", null, 405);
        return;
    }
    // Get JSON input
    $data = json_decode(file_get_contents('php://input'), true);
    
    if (!isset($data['usernameOrEmail']) || !isset($data['password'])) {
        sendResponse(false, "Username/email and password are required", null, 400);
        return;
    }

    $usernameOrEmail = $data['usernameOrEmail'];
    $password = $data['password'];
    global $db;
    
    try {
        // Check if user exists by username or email
        // Fix: Changed 'password_hash' to 'password' to match DB schema
        $stmt = $db->prepare("SELECT id, username, password FROM users WHERE username = ? OR email = ?");
        $stmt->bind_param("ss", $usernameOrEmail, $usernameOrEmail);
        $stmt->execute();
        $result = $stmt->get_result();
        
        if ($result->num_rows === 0) {
            sendResponse(false, "Invalid username or password", null, 401);
            return;
        }
        
        $user = $result->fetch_assoc();
        
        // Verify password
        // Fix: Changed 'password_hash' to 'password' to match DB schema
        if (!password_verify($password, $user['password'])) {
            sendResponse(false, "Invalid username or password", null, 401);
            return;
        }
        
        // Get user's IP address
        $ipAddress = $_SERVER['REMOTE_ADDR'] ?? 'UNKNOWN';

        // Update last login IP
        $updateStmt = $db->prepare("UPDATE users SET last_login_ip = ? WHERE id = ?");
        $updateStmt->bind_param("si", $ipAddress, $user['id']);
        $updateStmt->execute();
        $updateStmt->close(); // Close the statement after execution

        // Generate JWT token
        // Fix: Pass individual arguments as expected by generateJWT function
        $issuedAt = time();
        $expire = $issuedAt + JWT_EXPIRE; // Use defined constant
        $token = generateJWT($user['id'], $user['username'], $issuedAt, $expire);
        
        sendResponse(true, "Login successful", [
            'token' => $token,
            'username' => $user['username']
        ], 200);
        
    } catch (Exception $e) {
        sendResponse(false, "Server error: " . $e->getMessage(), null, 500);
    }
}

/**
 * Handles user registration
 */
function handleRegister() {
    if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
        sendResponse(false, "Method not allowed", null, 405);
        return;
    }
    // Get JSON input
    $data = json_decode(file_get_contents('php://input'), true);
    
    if (!isset($data['username']) || !isset($data['password']) || !isset($data['email'])) {
        sendResponse(false, "Username, email, and password are required", null, 400);
        return;
    }

    $username = $data['username'];
    $email = $data['email'];
    $password = $data['password'];
    
    // Validate username (only alphanumeric and underscore, 3-20 chars)
    if (!preg_match('/^[a-zA-Z0-9_]{3,20}$/', $username)) {
        sendResponse(false, "Username must be 3-20 characters and contain only letters, numbers, and underscores", null, 400);
        return;
    }
    
    // Validate email format
    if (!filter_var($email, FILTER_VALIDATE_EMAIL)) {
        sendResponse(false, "Please provide a valid email address", null, 400);
        return;
    }
    
    // Validate password (at least 8 chars)
    if (strlen($password) < 8) {
        sendResponse(false, "Password must be at least 8 characters", null, 400);
        return;
    }
    global $db;
    
    try {
        // Check if username or email already exists
        $stmt = $db->prepare("SELECT id FROM users WHERE username = ? OR email = ?");
        $stmt->bind_param("ss", $username, $email);
        $stmt->execute();
        $result = $stmt->get_result();
        
        if ($result->num_rows > 0) {
            sendResponse(false, "Username or email already exists", null, 409);
            return;
        }
        
        // Hash the password
        $passwordHash = password_hash($password, PASSWORD_DEFAULT);
        // Insert new user with email
        // Get user's IP address
        $ipAddress = $_SERVER['REMOTE_ADDR'] ?? 'UNKNOWN';

        // Insert new user with email and IP addresses
        $stmt = $db->prepare("INSERT INTO users (username, email, password, registration_ip, last_login_ip, created_at) VALUES (?, ?, ?, ?, ?, NOW())");
        // Adjust bind_param types: sssss (string for username, email, password, reg_ip, last_ip)
        $stmt->bind_param("sssss", $username, $email, $passwordHash, $ipAddress, $ipAddress);
        $success = $stmt->execute();
        
        if (!$success) {
            // Provide more specific error if possible
            sendResponse(false, "Registration failed: " . $stmt->error, null, 500); 
            $stmt->close(); // Close statement on failure
            return;
        }
        
        $userId = $db->insert_id;
        $stmt->close(); // Close the statement after execution

        // Generate JWT token
        // Fix: Pass individual arguments as expected by generateJWT function
        $issuedAt = time();
        $expire = $issuedAt + JWT_EXPIRE; // Use defined constant
        $token = generateJWT($userId, $username, $issuedAt, $expire);
        
        sendResponse(true, "Registration successful", [
            'token' => $token,
            'username' => $username
        ], 201);
        
    } catch (Exception $e) {
        sendResponse(false, "Server error: " . $e->getMessage(), null, 500);
    }
}

/**
 * Validates a token from request headers
 */
function validateToken() {
    $headers = getallheaders();
    $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
    
    if (empty($authHeader) || !preg_match('/Bearer\s(\S+)/', $authHeader, $matches)) {
        sendResponse(false, "No token provided", null, 401);
        return;
    }
    
    $token = $matches[1];
    $payload = verifyJWT($token);
    
    if ($payload === false) {
        sendResponse(false, "Invalid token", null, 401);
        return;
    }
    
    sendResponse(true, "Token is valid", [
        'username' => $payload['username']
    ], 200);
}

/**
 * Handles data synchronization
 * Requires authentication
 */
function handleSync() {
    // Authenticate the request
    $user = authenticateRequest();
    if (!$user) {
        return; // Response already sent in authenticateRequest()
    }
    
    // Process based on request method
    if ($_SERVER['REQUEST_METHOD'] === 'GET') {
        // Get user's saved data
        getUserData($user['user_id']);
    } elseif ($_SERVER['REQUEST_METHOD'] === 'POST') {
        // Save user's data
        saveUserData($user['user_id']);
    } else {
        sendResponse(false, "Method not allowed", null, 405);
    }
}

/**
 * Gets the user's saved data
 */
function getUserData($userId) {
    global $conn;
    
    try {
        $stmt = $conn->prepare("SELECT data FROM user_data WHERE user_id = ?");
        $stmt->bind_param("i", $userId);
        $stmt->execute();
        $result = $stmt->get_result();
        
        if ($result->num_rows === 0) {
            sendResponse(true, "No data found", ['data' => null], 200);
            return;
        }
        
        $row = $result->fetch_assoc();
        $data = json_decode($row['data'], true);
        
        sendResponse(true, "Data retrieved", ['data' => $data], 200);
        
    } catch (Exception $e) {
        sendResponse(false, "Server error: " . $e->getMessage(), null, 500);
    }
}

/**
 * Saves the user's data
 */
function saveUserData($userId) {
    // Get JSON input
    $input = json_decode(file_get_contents('php://input'), true);
    
    if (!isset($input['data'])) {
        sendResponse(false, "No data provided", null, 400);
        return;
    }
    
    $data = json_encode($input['data']);
    
    global $conn;
    
    try {
        // Check if user already has data
        $stmt = $conn->prepare("SELECT id FROM user_data WHERE user_id = ?");
        $stmt->bind_param("i", $userId);
        $stmt->execute();
        $result = $stmt->get_result();
        
        if ($result->num_rows > 0) {
            // Update existing data
            $stmt = $conn->prepare("UPDATE user_data SET data = ?, updated_at = NOW() WHERE user_id = ?");
            $stmt->bind_param("si", $data, $userId);
        } else {
            // Insert new data
            $stmt = $conn->prepare("INSERT INTO user_data (user_id, data, created_at, updated_at) VALUES (?, ?, NOW(), NOW())");
            $stmt->bind_param("is", $userId, $data);
        }
        
        $success = $stmt->execute();
        
        if (!$success) {
            sendResponse(false, "Failed to save data", null, 500);
            return;
        }
        
        sendResponse(true, "Data saved successfully", null, 200);
        
    } catch (Exception $e) {
        sendResponse(false, "Server error: " . $e->getMessage(), null, 500);
    }
}

/**
 * Handles forgot password requests
 */
function handleForgotPassword() {
    if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
        sendResponse(false, "Method not allowed", null, 405);
        return;
    }

    // Get JSON input
    $data = json_decode(file_get_contents('php://input'), true);
    
    if (!isset($data['username']) || !isset($data['email'])) {
        sendResponse(false, "Both username and email are required", null, 400);
        return;
    }

    $username = $data['username'];
    $email = $data['email'];

    global $conn;
    
    try {
        // Check if user exists with matching username and email
        $stmt = $conn->prepare("SELECT id FROM users WHERE username = ? AND email = ?");
        $stmt->bind_param("ss", $username, $email);
        $stmt->execute();
        $result = $stmt->get_result();
        
        if ($result->num_rows === 0) {
            // For security, return success even if user isn't found
            // This prevents email enumeration attacks
            sendResponse(true, "If your account exists, password reset instructions have been sent to your email", null, 200);
            return;
        }
        
        $userId = $result->fetch_assoc()['id'];
        
        // Generate a reset token (in a real implementation, we'd store this securely in the database)
        $resetToken = bin2hex(random_bytes(32));
        
        // Store the reset token with expiration (in a real app)
        // $stmt = $conn->prepare("INSERT INTO password_resets (user_id, token, expires_at) VALUES (?, ?, DATE_ADD(NOW(), INTERVAL 1 HOUR))");
        // $stmt->bind_param("is", $userId, $resetToken);
        // $stmt->execute();
        
        // In a real application, send an email with the reset link
        // This is just a placeholder - you would integrate with an email service
        
        sendResponse(true, "Password reset instructions sent to your email", null, 200);
        
    } catch (Exception $e) {
        sendResponse(false, "Server error: " . $e->getMessage(), null, 500);
    }
}

/**
 * Authenticates the request using JWT token
 * 
 * @return array|false User data from token or false if authentication fails
 */
function authenticateRequest() {
    $headers = getallheaders();
    $authHeader = isset($headers['Authorization']) ? $headers['Authorization'] : '';
    
    if (empty($authHeader) || !preg_match('/Bearer\s(\S+)/', $authHeader, $matches)) {
        sendResponse(false, "Authentication required", null, 401);
        return false;
    }
    
    $token = $matches[1];
    $payload = verifyJWT($token);
    
    if ($payload === false) {
        sendResponse(false, "Invalid token", null, 401);
        return false;
    }
    
    return $payload;
}

/**
 * Sends a formatted JSON response
 * @param bool $success Whether the request was successful
 * @param string $message Response message
 * @param mixed $data Additional data to include in the response (optional)
 * @param int $statusCode HTTP status code (default: 200)
 */
function sendResponse($success, $message, $data = null, $statusCode = 200) {
    http_response_code($statusCode);
    
    $response = [
        'success' => $success,
        'message' => $message,
        'data' => $data
    ];
    
    echo json_encode($response);
    exit();
}
