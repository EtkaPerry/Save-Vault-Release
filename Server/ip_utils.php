<?php
/**
 * IP and Location Utilities
 * Provides helper functions for IP and location detection
 */

/**
 * Get location information from IP address
 * Using free IP Geolocation API service
 * 
 * @param string $ip The IP address to look up
 * @return array|null Location information (country, city) or null on failure
 */
function getLocationFromIP($ip) {
    // Avoid lookup for localhost, private IPs, etc.
    if ($ip == '127.0.0.1' || $ip == 'localhost' || $ip == '::1' || 
        strpos($ip, '192.168.') === 0 || strpos($ip, '10.') === 0) {
        return [
            'country' => 'Local Network',
            'city' => 'Local',
            'country_code' => 'LN'
        ];
    }
    
    try {
        // Use IP-API free service to get location info
        $url = "http://ip-api.com/json/{$ip}?fields=status,country,countryCode,city,query";
        $response = @file_get_contents($url);
        
        if ($response !== false) {
            $data = json_decode($response, true);
            
            if ($data && isset($data['status']) && $data['status'] === 'success') {
                return [
                    'country' => $data['country'],
                    'city' => $data['city'],
                    'country_code' => $data['countryCode']
                ];
            }
        }
        
        // Fallback if API call fails
        return [
            'country' => 'Unknown',
            'city' => 'Unknown',
            'country_code' => 'UN'
        ];
    } catch (Exception $e) {
        error_log("Error getting location data: " . $e->getMessage());
        return [
            'country' => 'Unknown',
            'city' => 'Unknown',
            'country_code' => 'UN'
        ];
    }
}

/**
 * Parse user agent to get browser and device information
 * 
 * @param string $userAgent The browser's User-Agent string
 * @return array Browser and device information
 */
function parseUserAgent($userAgent) {
    $browser = 'Unknown';
    $os = 'Unknown';
    $deviceType = 'Unknown';
    
    // Detect browser
    if (strpos($userAgent, 'Firefox') !== false) {
        $browser = 'Firefox';
    } elseif (strpos($userAgent, 'Edge') !== false || strpos($userAgent, 'Edg') !== false) {
        $browser = 'Edge';
    } elseif (strpos($userAgent, 'Chrome') !== false) {
        $browser = 'Chrome';
    } elseif (strpos($userAgent, 'Safari') !== false) {
        $browser = 'Safari';
    } elseif (strpos($userAgent, 'Opera') !== false || strpos($userAgent, 'OPR') !== false) {
        $browser = 'Opera';
    } elseif (strpos($userAgent, 'MSIE') !== false || strpos($userAgent, 'Trident') !== false) {
        $browser = 'Internet Explorer';
    }
    
    // Detect OS
    if (strpos($userAgent, 'Windows') !== false) {
        $os = 'Windows';
    } elseif (strpos($userAgent, 'Macintosh') !== false || strpos($userAgent, 'Mac OS') !== false) {
        $os = 'Mac OS';
    } elseif (strpos($userAgent, 'Linux') !== false) {
        $os = 'Linux';
    } elseif (strpos($userAgent, 'Android') !== false) {
        $os = 'Android';
    } elseif (strpos($userAgent, 'iOS') !== false || strpos($userAgent, 'iPhone') !== false || strpos($userAgent, 'iPad') !== false) {
        $os = 'iOS';
    }
    
    // Detect device type
    if (strpos($userAgent, 'Mobile') !== false || strpos($userAgent, 'Android') !== false && strpos($userAgent, 'Mobile') !== false) {
        $deviceType = 'Mobile';
    } elseif (strpos($userAgent, 'Tablet') !== false || strpos($userAgent, 'iPad') !== false) {
        $deviceType = 'Tablet';
    } elseif (strpos($userAgent, 'Windows') !== false || strpos($userAgent, 'Macintosh') !== false || strpos($userAgent, 'Linux') !== false) {
        $deviceType = 'Desktop';
    }
    
    return [
        'browser' => $browser,
        'os' => $os,
        'device_type' => $deviceType
    ];
}