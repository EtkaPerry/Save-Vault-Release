<?php
/**
 * Extensions API
 * Serves approved extensions to SaveVault clients
 */

require_once __DIR__ . '/security.php';
sv_init_api('GET, POST, OPTIONS'); // error handling, JSON headers, CORS allow-list, preflight

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/db_handler.php';

class ExtensionsAPI {
    private $db;
    
    public function __construct() {
        try {
            $this->db = get_db_connection();
            if (!$this->db) {
                throw new Exception("Failed to establish database connection");
            }
        } catch (Exception $e) {
            error_log("ExtensionsAPI constructor error: " . $e->getMessage());
            $this->sendError('Database connection failed', 500);
            exit;
        }
    }
    
    /**
     * Handle API requests
     */
    public function handleRequest() {
        try {
            $method = $_SERVER['REQUEST_METHOD'];
            $action = $_GET['action'] ?? 'list';
            
            switch ($method) {
                case 'GET':
                    $this->handleGet($action);
                    break;
                    
                case 'POST':
                    $this->handlePost($action);
                    break;
                    
                default:
                    $this->sendError('Method not allowed', 405);
            }
            
        } catch (Exception $e) {
            $this->sendError($e->getMessage(), 500);
        }
    }
    
    /**
     * Handle GET requests
     */
    private function handleGet($action) {
        switch ($action) {
            case 'list':
                $this->getExtensionsList();
                break;
                
            case 'catalog':
                $this->getExtensionsCatalog();
                break;
                
            case 'download':
                $this->recordDownload();
                break;
                
            default:
                $this->sendError('Invalid action', 400);
        }
    }
    
    /**
     * Handle POST requests
     */
    private function handlePost($action) {
        switch ($action) {
            case 'download':
                $this->recordDownload();
                break;
                
            default:
                $this->sendError('Invalid action', 400);
        }
    }
    
    /**
     * Get list of approved extensions
     */
    private function getExtensionsList() {
        $category = $_GET['category'] ?? null;
        $search = $_GET['search'] ?? null;
        
        $sql = "SELECT 
                    extension_id as id,
                    name,
                    description,
                    version,
                    author,
                    category,
                    github_url as downloadUrl,
                    download_count as downloads,
                    rating,
                    icon_url as iconUrl,
                    is_official as isOfficial,
                    created_at as createdDate,
                    updated_at as updatedDate
                FROM extensions 
                WHERE is_approved = 1";
        
        $params = [];
        
        if ($category && $category !== 'All') {
            $sql .= " AND category = ?";
            $params[] = $category;
        }
        
        if ($search) {
            $sql .= " AND (name LIKE ? OR description LIKE ? OR author LIKE ?)";
            $searchTerm = "%$search%";
            $params[] = $searchTerm;
            $params[] = $searchTerm;
            $params[] = $searchTerm;
        }
        
        $sql .= " ORDER BY is_official DESC, download_count DESC, name ASC";
        
        $stmt = $this->db->prepare($sql);
        $stmt->execute($params);
        $extensions = $stmt->fetchAll(PDO::FETCH_ASSOC);
        
        // Convert data types
        foreach ($extensions as &$extension) {
            $extension['downloads'] = (int)$extension['downloads'];
            $extension['rating'] = (float)$extension['rating'];
            $extension['isOfficial'] = (bool)$extension['isOfficial'];
            
            // Format dates
            $extension['createdDate'] = date('c', strtotime($extension['createdDate']));
            $extension['updatedDate'] = date('c', strtotime($extension['updatedDate']));
        }
        
        $this->sendSuccess($extensions);
    }
    
    /**
     * Get extensions catalog in SaveVault format
     */
    private function getExtensionsCatalog() {
        $this->getExtensionsList();
    }
    
    /**
     * Record extension download
     */
    private function recordDownload() {
        // This endpoint is unauthenticated; throttle it so download counts
        // cannot be trivially inflated from a single source.
        sv_rate_limit_or_die('ext_download', 60, 60);

        $input = json_decode(file_get_contents('php://input'), true);
        $extension_id = (is_array($input) ? ($input['extension_id'] ?? null) : null) ?? $_GET['extension_id'] ?? null;
        
        if (!$extension_id) {
            $this->sendError('Extension ID required', 400);
            return;
        }
        
        // Update download count
        $stmt = $this->db->prepare("
            UPDATE extensions 
            SET download_count = download_count + 1 
            WHERE extension_id = ? AND is_approved = 1
        ");
        $stmt->execute([$extension_id]);
        
        if ($stmt->rowCount() > 0) {
            $this->sendSuccess(['message' => 'Download recorded']);
        } else {
            $this->sendError('Extension not found or not approved', 404);
        }
    }
    
    /**
     * Send success response
     */
    private function sendSuccess($data) {
        http_response_code(200);
        echo json_encode([
            'success' => true,
            'data' => $data
        ]);
    }
    
    /**
     * Send error response
     */
    private function sendError($message, $code = 400) {
        http_response_code($code);
        echo json_encode([
            'success' => false,
            'error' => $message
        ]);
    }
}

// Handle the request with error catching
try {
    $api = new ExtensionsAPI();
    $api->handleRequest();
} catch (Exception $e) {
    error_log("Fatal error in ExtensionsAPI: " . $e->getMessage());
    http_response_code(500);
    echo json_encode([
        'success' => false,
        'error' => 'Internal server error occurred'
    ]);
}
?>