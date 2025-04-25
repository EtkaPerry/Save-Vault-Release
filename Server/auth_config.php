<?php
// Database configuration
define('DB_HOST', 'localhost');
define('DB_NAME', 'vault');  // Changed to match db.php
define('DB_USER', 'your_database_username');  // Changed to match db.php
define('DB_PASS', 'your_database_password');  // Changed to match db.php

// JWT configuration
define('JWT_SECRET', 'your_jwt_secret_key'); // Change this to random value minimum 32 characters, preferably 128 characters
define('JWT_EXPIRATION', 86400); // 24 hours in seconds
?>
