<?php
/**
 * Application configuration
 */

// Application settings
if (!defined('APP_NAME')) define('APP_NAME', 'Save Vault');
if (!defined('APP_URL'))  define('APP_URL', '92.205.12.157');

// JWT settings.
// ROTATED 2026-06-03 after the GitHub exposure: the previous secret is in git
// history and must be treated as compromised. Rotating it invalidates every
// token signed with the old value (all users / desktop clients re-login once
// this is deployed). Prefer the SAVEVAULT_JWT_SECRET environment variable when
// set, so the live value can be kept entirely out of git if you choose. The
// defined() guard avoids a "constant already defined" warning when config.php
// and auth_config.php are both loaded in one request.
if (!defined('JWT_SECRET')) define('JWT_SECRET', getenv('SAVEVAULT_JWT_SECRET') ?: 'L50Xwk47fZFU4LNO6MAEePEeIlZkPa3578OyIeJOHr5xZw5CS2LL9DqOPyIZgaHDTJCeSf9bmoLyYBAklkAh6wbzjkoCQ7fDzIE6MfcfaWTBqnBWrPMs25J2XqA01aNqOzTymKkLArXoCPWC');
if (!defined('JWT_EXPIRE')) define('JWT_EXPIRE', 86400 * 30); // 30 days in seconds
