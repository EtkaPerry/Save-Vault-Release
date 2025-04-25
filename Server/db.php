<?php
/**
 * Database configuration
 */

// Enable error reporting for debugging
error_reporting(E_ALL);
ini_set('display_errors', 1);

// Database credentials
define('DB_HOST', 'localhost');
define('DB_NAME', 'your_database_name');
define('DB_USER', 'your_database_username');
define('DB_PASSWORD', 'your_database_password');

// Create database connection with error handling
try {
    $db = new mysqli(DB_HOST, DB_USER, DB_PASSWORD, DB_NAME);

    // Check connection
    if ($db->connect_error) {
        error_log("Database connection failed: " . $db->connect_error);
        throw new Exception("Database connection failed: " . $db->connect_error);
    }

    // Set charset to utf8mb4
    if (!$db->set_charset('utf8mb4')) {
        error_log("Error setting charset: " . $db->error);
        throw new Exception("Error setting charset: " . $db->error);
    }

    // Make the database connection available globally
    global $db;
} catch (Exception $e) {
    error_log("Database error: " . $e->getMessage());
    header('Content-Type: application/json');
    echo json_encode([
        'success' => false,
        'message' => 'Database connection error. Please try again later.',
        'data' => null
    ]);
    exit;
}
