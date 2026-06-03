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
 * @param bool $isAdmin Whether the user is an admin (default: false)
 * @return string Generated JWT token
 */
function generateJWT($userId, $username, $issuedAt, $expire, $isAdmin = false) {
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
        'jti' => uniqid(),    // Unique token ID
        'admin' => $isAdmin   // Admin status
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
 * JWT Helper Class
 */
class JWT {
    /**
     * Decode a JWT token
     * 
     * @param string $token JWT token to decode
     * @param bool $allowExpired Whether to allow expired tokens (default: false)
     * @return object|false Decoded payload object or false if invalid
     */
    public static function decode($token, $allowExpired = false) {
        return validateJWT($token, $allowExpired);
    }
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

    if (!is_string($token) || $token === '') {
        return false;
    }

    // Split token into parts
    $tokenParts = explode('.', $token);

    if (count($tokenParts) !== 3) {
        return false; // Invalid token format
    }

    list($base64UrlHeader, $base64UrlPayload, $base64UrlSignature) = $tokenParts;

    // Inspect the header and only accept HS256-signed tokens. This blocks
    // algorithm-confusion attacks (e.g. forged "alg":"none" tokens or attempts
    // to switch to an asymmetric algorithm).
    $header = json_decode(base64UrlDecode($base64UrlHeader));
    if (!is_object($header) || !isset($header->alg) || $header->alg !== 'HS256') {
        return false;
    }

    // Verify the signature with a constant-time comparison. The expected value
    // is passed as the known string (first arg) per hash_equals() guidance.
    $signature = base64UrlDecode($base64UrlSignature);
    $expectedSignature = hash_hmac('sha256', "$base64UrlHeader.$base64UrlPayload", $JWT_SECRET, true);

    if (!is_string($signature) || $signature === '' || !hash_equals($expectedSignature, $signature)) {
        return false; // Invalid signature
    }

    // Decode payload
    $payload = json_decode(base64UrlDecode($base64UrlPayload));

    if (!is_object($payload)) {
        return false; // Invalid payload
    }

    // Check expiration
    if (!$allowExpired && isset($payload->exp) && $payload->exp < time()) {
        return false; // Token expired
    }

    // A subject (user id) is mandatory for every token we issue.
    if (!isset($payload->sub)) {
        return false;
    }

    // Add userId and id for convenience and backward compatibility
    $payload->userId = $payload->sub;
    $payload->id = $payload->sub; // Add id property that matches sub

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
 * Return the validated JWT payload from the `auth_token` cookie, or null when no
 * correctly-signed, unexpired token is present.
 *
 * Site pages use this to derive login state / admin status from a *verified*
 * token instead of trusting the unsigned, attacker-controllable cookie body.
 *
 * @return object|null Decoded payload, or null if missing/invalid/expired.
 */
function getAuthenticatedUserFromCookie() {
    if (empty($_COOKIE['auth_token'])) {
        return null;
    }
    $payload = validateJWT($_COOKIE['auth_token']);
    return $payload === false ? null : $payload;
}

/**
 * Base64Url decode a string
 *
 * @param string $data Data to decode
 * @return string Decoded data
 */
function base64UrlDecode($data) {
    if (!is_string($data) || $data === '') {
        return '';
    }
    $base64 = strtr($data, '-_', '+/');
    // Restore the '=' padding stripped during encoding so the length is a
    // multiple of 4 (the previous implementation padded to the wrong length).
    $remainder = strlen($base64) % 4;
    if ($remainder > 0) {
        $base64 .= str_repeat('=', 4 - $remainder);
    }
    // Strict decoding rejects tokens containing invalid base64 characters.
    $decoded = base64_decode($base64, true);
    return $decoded === false ? '' : $decoded;
}
