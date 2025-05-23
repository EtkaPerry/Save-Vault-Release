<?php
/**
 * File Upload Handler for Save Vault
 * Handles secure file uploads with validation
 */

// Include the secure file validator
require_once 'secure_file_validator.php';

/**
 * Process an uploaded profile photo
 * 
 * @param array $file The uploaded file ($_FILES array element)
 * @param int $userId The ID of the user uploading the photo
 * @return array Result information with success status and message
 */
function handleProfilePhotoUpload($file, $userId) {
    // Validate file exists and there was no error in upload
    if (!isset($file) || $file['error'] !== UPLOAD_ERR_OK) {
        $errorMessage = getUploadErrorMessage($file['error'] ?? -1);
        return ['success' => false, 'message' => $errorMessage];
    }
    
    // Define allowed parameters
    $maxSize = 2 * 1024 * 1024; // 2MB
    $allowedTypes = ['image/jpeg', 'image/png', 'image/gif'];
    
    // Perform comprehensive validation using our security class
    $validationResult = SecureFileValidator::validateUploadedFile(
        $file['tmp_name'],
        $allowedTypes,
        $maxSize
    );
    
    // If validation failed, return the error
    if (!$validationResult['success']) {
        return [
            'success' => false, 
            'message' => $validationResult['message']
        ];
    }
    
    // Get the validated MIME type
    $detectedType = $validationResult['mime_type'];
    
    // Create upload directory if it doesn't exist with secure permissions
    $uploadDir = dirname(__FILE__) . '/uploads/profile_photos';
    if (!is_dir($uploadDir)) {
        if (!mkdir($uploadDir, 0755, true)) {
            return ['success' => false, 'message' => 'Failed to create upload directory'];
        }
        
        // Create index.php to prevent directory listing
        $indexFile = $uploadDir . '/index.php';
        if (!file_exists($indexFile)) {
            file_put_contents($indexFile, '<?php header("HTTP/1.1 403 Forbidden"); exit; ?>');
        }
    }
    
    // Generate a unique filename with sanitization
    // Use only the verified extension from our detection, not user input
    $fileExtension = getSecureFileExtension($detectedType);
    
    // Create a random filename - don't rely solely on user ID and timestamp
    $randomString = bin2hex(random_bytes(8)); // 16 character random string
    $newFilename = 'profile_' . preg_replace('/[^0-9]/', '', $userId) . '_' . 
                   time() . '_' . $randomString . '.' . $fileExtension;
    
    $uploadPath = $uploadDir . '/' . $newFilename;
    
    // Move the uploaded file to the destination
    if (!move_uploaded_file($file['tmp_name'], $uploadPath)) {
        return ['success' => false, 'message' => 'Failed to save uploaded file'];
    }
    
    // Set secure permissions on the uploaded file
    chmod($uploadPath, 0644);
    
    // Get the relative path for database storage
    $relativePath = 'uploads/profile_photos/' . $newFilename;
    
    return [
        'success' => true, 
        'message' => 'File uploaded successfully', 
        'filename' => $newFilename,
        'path' => $relativePath
    ];
}

/**
 * Verify image integrity by attempting to create an image resource
 * 
 * @param string $filePath Path to the temporary uploaded file
 * @param string $mimeType Detected MIME type of the file
 * @return array Result with success status and message
 */
function verifyImageIntegrity($filePath, $mimeType) {
    // Try to create an image resource to verify it's actually a valid image
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
        }
        
        if ($image === false) {
            return [
                'success' => false, 
                'message' => 'The file appears to be corrupted or is not a valid image'
            ];
        }
        
        // Check for reasonable image dimensions (e.g., not 1x1 pixel)
        $width = imagesx($image);
        $height = imagesy($image);
        
        if ($width < 10 || $height < 10) {
            imagedestroy($image);
            return [
                'success' => false, 
                'message' => 'Image dimensions are too small. Minimum size is 10x10 pixels.'
            ];
        }
        
        // Free memory
        imagedestroy($image);
        
        return ['success' => true, 'message' => 'Image verified successfully'];
    } catch (Exception $e) {
        if ($image !== false) {
            imagedestroy($image);
        }
        
        return [
            'success' => false, 
            'message' => 'Error verifying image: ' . $e->getMessage()
        ];
    }
}

/**
 * Get a secure file extension based on mime type
 * 
 * @param string $mimeType The MIME type of the file
 * @return string A secure file extension
 */
function getSecureFileExtension($mimeType) {
    switch ($mimeType) {
        case 'image/jpeg':
            return 'jpg';
        case 'image/png':
            return 'png';
        case 'image/gif':
            return 'gif';
        default:
            return 'jpg'; // Default fallback, though this should never happen due to earlier checks
    }
}

/**
 * Get a human-readable error message for file upload errors
 * 
 * @param int $errorCode PHP file upload error code
 * @return string Human-readable error message
 */
function getUploadErrorMessage($errorCode) {
    switch ($errorCode) {
        case UPLOAD_ERR_INI_SIZE:
            return 'The uploaded file exceeds the upload_max_filesize directive in php.ini';
        case UPLOAD_ERR_FORM_SIZE:
            return 'The uploaded file exceeds the MAX_FILE_SIZE directive in the HTML form';
        case UPLOAD_ERR_PARTIAL:
            return 'The uploaded file was only partially uploaded';
        case UPLOAD_ERR_NO_FILE:
            return 'No file was uploaded';
        case UPLOAD_ERR_NO_TMP_DIR:
            return 'Missing a temporary folder';
        case UPLOAD_ERR_CANT_WRITE:
            return 'Failed to write file to disk';
        case UPLOAD_ERR_EXTENSION:
            return 'A PHP extension stopped the file upload';
        default:
            return 'Unknown upload error';
    }
}