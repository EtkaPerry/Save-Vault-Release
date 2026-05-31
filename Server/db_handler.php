<?php
require_once 'auth_config.php';

/**
 * Get PDO database connection
 * @return PDO Database connection
 * @throws Exception If connection fails
 */
function get_db_connection() {
    try {
        // Check if required constants are defined
        if (!defined('DB_HOST') || !defined('DB_NAME') || !defined('DB_USER') || !defined('DB_PASS')) {
            error_log("Database constants not defined. Required: DB_HOST, DB_NAME, DB_USER, DB_PASS");
            throw new Exception("Database configuration error: Missing required constants");
        }
        
        $pdo = new PDO(
            "mysql:host=" . DB_HOST . ";dbname=" . DB_NAME . ";charset=utf8mb4",
            DB_USER,
            DB_PASS,
            [
                PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
                PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
                PDO::MYSQL_ATTR_INIT_COMMAND => "SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci"
            ]
        );
        return $pdo;
    } catch(PDOException $e) {
        error_log("PDO Connection Error: " . $e->getMessage());
        error_log("Connection details - Host: " . (defined('DB_HOST') ? DB_HOST : 'undefined') . 
                 ", DB: " . (defined('DB_NAME') ? DB_NAME : 'undefined') . 
                 ", User: " . (defined('DB_USER') ? DB_USER : 'undefined'));
        throw new Exception("Database connection failed: " . $e->getMessage());
    }
}

class DatabaseHandler {
    private $conn;
    
    public function __construct() {
        $this->conn = get_db_connection();
    }
    
    public function createUser($username, $email, $password) {
        try {
            $hashedPassword = password_hash($password, PASSWORD_DEFAULT);
            $stmt = $this->conn->prepare("INSERT INTO users (username, email, password) VALUES (?, ?, ?)");
            return $stmt->execute([$username, $email, $hashedPassword]);
        } catch(PDOException $e) {
            error_log("Create User Error: " . $e->getMessage());
            throw new Exception("Failed to create user");
        }
    }
    
    public function getUserByCredentials($usernameOrEmail) {
        try {
            $stmt = $this->conn->prepare("SELECT * FROM users WHERE username = ? OR email = ?");
            $stmt->execute([$usernameOrEmail, $usernameOrEmail]);
            return $stmt->fetch(PDO::FETCH_ASSOC);
        } catch(PDOException $e) {
            error_log("Get User Error: " . $e->getMessage());
            throw new Exception("Failed to get user");
        }
    }
    
    public function validateUserCredentials($usernameOrEmail, $password) {
        $user = $this->getUserByCredentials($usernameOrEmail);
        if ($user && password_verify($password, $user['password'])) {
            return $user;
        }
        return false;
    }
}
?>
