<?php
/**
 * Enhanced Security Utilities for File Uploads
 * Save Vault - Security Layer
 */

/**
 * Advanced image security validation
 * 
 * @param string $filePath Path to the image file
 * @param array $options Options for validation
 * @return array Result with success status and message
 */
class SecureFileValidator {
    /**
     * Run comprehensive security checks on an uploaded file
     * 
     * @param string $filePath Path to the file
     * @param array $allowedMimeTypes Array of allowed MIME types
     * @param int $maxSizeBytes Maximum allowed file size in bytes
     * @return array Validation result with success status and message
     */
    public static function validateUploadedFile($filePath, $allowedMimeTypes, $maxSizeBytes) {
        // Check if file exists
        if (!file_exists($filePath) || !is_readable($filePath)) {
            return [
                'success' => false,
                'message' => 'File is not accessible or does not exist'
            ];
        }
        
        // Check file size
        $fileSize = filesize($filePath);
        if ($fileSize > $maxSizeBytes) {
            return [
                'success' => false,
                'message' => "File size ($fileSize bytes) exceeds maximum allowed ($maxSizeBytes bytes)"
            ];
        }
        
        // Check MIME type
        $finfo = finfo_open(FILEINFO_MIME_TYPE);
        $detectedMimeType = finfo_file($finfo, $filePath);
        finfo_close($finfo);
        
        if (!in_array($detectedMimeType, $allowedMimeTypes)) {
            return [
                'success' => false,
                'message' => "Invalid file type: $detectedMimeType. Allowed types: " . implode(', ', $allowedMimeTypes)
            ];
        }
        
        // Additional security checks based on file type
        if (self::isImageFile($detectedMimeType)) {
            return self::validateImage($filePath, $detectedMimeType);
        }
        
        // If we reach here, all checks passed
        return [
            'success' => true,
            'message' => 'File validation successful',
            'mime_type' => $detectedMimeType
        ];
    }
    
    /**
     * Check if the MIME type corresponds to an image
     * 
     * @param string $mimeType MIME type to check
     * @return bool True if it's an image MIME type
     */
    private static function isImageFile($mimeType) {
        return strpos($mimeType, 'image/') === 0;
    }
    
    /**
     * Validate image-specific security aspects
     * 
     * @param string $filePath Path to the image file
     * @param string $mimeType Detected MIME type
     * @return array Validation result
     */
    private static function validateImage($filePath, $mimeType) {
        // Try to create image resource based on type
        $image = false;
        
        try {
            switch ($mimeType) {
                case 'image/jpeg':
                    $image = @imagecreatefromjpeg($filePath);
                    break;
                case 'image/png':
                    $image = @imagecreatefrompng($filePath);
                    break;
                case 'image/gif':
                    $image = @imagecreatefromgif($filePath);
                    break;
                default:
                    return [
                        'success' => false,
                        'message' => "Unsupported image type: $mimeType"
                    ];
            }
            
            // Check if image creation was successful
            if ($image === false) {
                return [
                    'success' => false,
                    'message' => 'Failed to process image. File may be corrupted or contain malicious data.'
                ];
            }
            
            // Get image dimensions
            $width = imagesx($image);
            $height = imagesy($image);
            
            // Check reasonable dimensions
            if ($width <= 0 || $height <= 0 || $width > 8000 || $height > 8000) {
                imagedestroy($image);
                return [
                    'success' => false,
                    'message' => "Invalid image dimensions: {$width}x{$height}"
                ];
            }
            
            // Check for embedded PHP code in image metadata
            $imageContent = file_get_contents($filePath);
            if (stripos($imageContent, '<?php') !== false || 
                stripos($imageContent, '<?=') !== false || 
                stripos($imageContent, '<script') !== false) {
                imagedestroy($image);
                return [
                    'success' => false,
                    'message' => 'Potential malicious code detected in image'
                ];
            }
            
            // Free resources
            imagedestroy($image);
            
            // All checks passed
            return [
                'success' => true,
                'message' => 'Image validation successful',
                'dimensions' => [
                    'width' => $width,
                    'height' => $height
                ]
            ];
            
        } catch (Exception $e) {
            // Clean up if image was created
            if ($image !== false) {
                imagedestroy($image);
            }
            
            return [
                'success' => false,
                'message' => 'Error during image validation: ' . $e->getMessage()
            ];
        }
    }
}
?>
