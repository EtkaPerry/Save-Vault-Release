<?php
// Database configuration.
// SECURITY: these credentials are committed to source control (and thus in the
// git history). They should be rotated and supplied via an environment variable
// or a file stored outside the web root. The defined() guards prevent
// "constant already defined" warnings when config.php is also loaded.
if (!defined('DB_HOST')) define('DB_HOST', 'localhost');
if (!defined('DB_NAME')) define('DB_NAME', 'vault');
if (!defined('DB_USER')) define('DB_USER', 'your_database_username');
if (!defined('DB_PASS')) define('DB_PASS', 'your_database_password');

// JWT configuration. ROTATED 2026-06-03 (see config.php) — kept identical to the
// value in config.php and overridable via the SAVEVAULT_JWT_SECRET env var.
if (!defined('JWT_SECRET')) define('JWT_SECRET', getenv('SAVEVAULT_JWT_SECRET') ?: 'L50Xwk47fZFU4LNO6MAEePEeIlZkPa3578OyIeJOHr5xZw5CS2LL9DqOPyIZgaHDTJCeSf9bmoLyYBAklkAh6wbzjkoCQ7fDzIE6MfcfaWTBqnBWrPMs25J2XqA01aNqOzTymKkLArXoCPWC');
if (!defined('JWT_EXPIRATION')) define('JWT_EXPIRATION', 86400); // 24 hours in seconds
