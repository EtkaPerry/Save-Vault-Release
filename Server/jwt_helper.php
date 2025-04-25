<?php
/**
 * JWT (JSON Web Token) Helper Functions
 * For Save Vault API Authentication
 */

// Secret key for JWT signing - this should be kept secure and not in source control
// In production, use an environment variable or secure configuration
require_once 'config.php';
$JWT_SECRET = JWT_SECRET;

/**
 * Generate a JWT token
 * 
 * @param int $userId User ID
 * @param string $username Username
 * @param int $issuedAt Time when token was issued (unix timestamp)
 * @param int $expire Time when token expires (unix timestamp)
 * @return string Generated JWT token
 */
function generateJWT($userId, $username, $issuedAt, $expire) {
    global $JWT_SECRET;
    
    // Create token header
    $header = [
        'typ' => 'JWT',
        'alg' => 'HS256'
    ];
    
    // Create token payload
    $payload = [
        'sub' => $userId,     // Subject (user ID)
        'name' => $username,  // Username
        'iat' => $issuedAt,   // Issued at time
        'exp' => $expire,     // Expiration time
        'jti' => uniqid()     // Unique token ID
    ];
    
    // Encode Header
    $base64UrlHeader = base64UrlEncode(json_encode($header));
    
    // Encode Payload
    $base64UrlPayload = base64UrlEncode(json_encode($payload));
    
    // Create Signature
    $signature = hash_hmac('sha256', "$base64UrlHeader.$base64UrlPayload", $JWT_SECRET, true);
    $base64UrlSignature = base64UrlEncode($signature);
    
    // Create JWT
    $token = "$base64UrlHeader.$base64UrlPayload.$base64UrlSignature";
    
    return $token;
}

/**
 * Validate a JWT token
 * 
 * @param string $token JWT token to validate
 * @param bool $allowExpired Whether to allow expired tokens (useful for refresh)
 * @return object|false Decoded payload object or false if invalid
 */
function validateJWT($token, $allowExpired = false) {
    global $JWT_SECRET;
    
    // Split token into parts
    $tokenParts = explode('.', $token);
    
    if (count($tokenParts) !== 3) {
        return false; // Invalid token format
    }
    
    list($base64UrlHeader, $base64UrlPayload, $base64UrlSignature) = $tokenParts;
    
    // Verify signature
    $signature = base64UrlDecode($base64UrlSignature);
    $expectedSignature = hash_hmac('sha256', "$base64UrlHeader.$base64UrlPayload", $JWT_SECRET, true);
    
    if (!hash_equals($signature, $expectedSignature)) {
        return false; // Invalid signature
    }
    
    // Decode payload
    $payload = json_decode(base64UrlDecode($base64UrlPayload));
    
    if (!$payload) {
        return false; // Invalid payload
    }
    
    // Check expiration
    if (!$allowExpired && isset($payload->exp) && $payload->exp < time()) {
        return false; // Token expired
    }
    
    // Add userId for convenience
    $payload->userId = $payload->sub;
    
    return $payload;
}

/**
 * Base64Url encode a string
 * 
 * @param string $data Data to encode
 * @return string Base64Url encoded string
 */
function base64UrlEncode($data) {
    $base64 = base64_encode($data);
    $base64Url = strtr($base64, '+/', '-_');
    return rtrim($base64Url, '=');
}

/**
 * Base64Url decode a string
 * 
 * @param string $data Data to decode
 * @return string Decoded data
 */
function base64UrlDecode($data) {
    $base64 = strtr($data, '-_', '+/');
    $paddedBase64 = str_pad($base64, strlen($data) % 4, '=', STR_PAD_RIGHT);
    return base64_decode($paddedBase64);
}
