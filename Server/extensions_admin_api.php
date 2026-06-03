<?php
/**
 * Extensions Admin API
 * Admin interface for managing extensions
 */

require_once __DIR__ . '/security.php';
sv_init_api('GET, POST, OPTIONS'); // error handling, JSON headers, CORS allow-list, preflight

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/db_handler.php';
require_once __DIR__ . '/jwt_helper.php';

class ExtensionsAdminAPI {
    private $db;
      public function __construct() {
        try {
            $this->db = get_db_connection();
            if (!$this->db) {
                throw new Exception("Failed to establish database connection");
            }
            
            // Test the connection
            $this->db->query("SELECT 1");
            
        } catch (Exception $e) {
            error_log("ExtensionsAdminAPI constructor error: " . $e->getMessage());
            $this->sendError('Database connection failed', 500);
            exit;
        }
    }
      /**
     * Handle API requests
     */
    public function handleRequest() {
        try {
            // Add more detailed error handling
            if (!$this->db) {
                throw new Exception("Database connection not available");
            }
            
            // Verify admin authentication
            if (!$this->verifyAdminAuth()) {
                $this->sendError('Unauthorized access', 401);
                return;
            }
            
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
            error_log("Extensions Admin API Error: " . $e->getMessage());
            $this->sendError('Internal server error', 500);
        }
    }
      /**
     * Verify admin authentication
     */
    private function verifyAdminAuth() {
        try {
            // Token from the Authorization header (desktop client) or the HttpOnly
            // session cookie (web UI).
            $fromCookie = false;
            $token = sv_bearer_token($fromCookie);
            if (!$token) {
                return false;
            }

            $payload = JWT::decode($token);
            if (!$payload) {
                return false;
            }

            if (!isset($payload->admin) || !$payload->admin) {
                return false;
            }

            // CSRF guard for cookie-authenticated state-changing requests.
            sv_csrf_guard($fromCookie);

            return true;
        } catch (Exception $e) {
            error_log("Admin auth error: " . $e->getMessage());
            return false;
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
                
            default:
                $this->sendError('Invalid action', 400);
        }
    }
    
    /**
     * Handle POST requests
     */
    private function handlePost($action) {
        switch ($action) {
            case 'approve':
                $this->approveExtension();
                break;
                
            case 'reject':
                $this->rejectExtension();
                break;
                
            case 'sync':
                $this->syncExtensions();
                break;
                
            default:
                $this->sendError('Invalid action', 400);
        }
    }
      /**
     * Get list of extensions for admin
     */
    private function getExtensionsList() {
        try {
            $filter = $_GET['filter'] ?? 'all';
            
            $sql = "SELECT 
                        id as extension_id,
                        extension_id as original_extension_id,
                        name,
                        description,
                        version,
                        author,
                        category,
                        github_url,
                        download_count,
                        rating,
                        icon_url,
                        is_approved,
                        is_official,
                        created_at,
                        updated_at
                    FROM extensions";
            
            $params = [];
            
            switch ($filter) {
                case 'pending':
                    $sql .= " WHERE is_approved IS NULL";
                    break;
                case 'approved':
                    $sql .= " WHERE is_approved = 1";
                    break;
                case 'rejected':
                    $sql .= " WHERE is_approved = 0";
                    break;
                // 'all' shows everything
            }
            
            $sql .= " ORDER BY created_at DESC";
            
            $stmt = $this->db->prepare($sql);
            $stmt->execute($params);
            $extensions = $stmt->fetchAll(PDO::FETCH_ASSOC);
            
            // Get stats
            $stats = $this->getExtensionStats();
            
            $this->sendSuccess([
                'extensions' => $extensions,
                'stats' => $stats
            ]);
            
        } catch (Exception $e) {
            error_log("getExtensionsList error: " . $e->getMessage());
            $this->sendError('Failed to fetch extensions', 500);
        }
    }
      /**
     * Get extension statistics
     */
    private function getExtensionStats() {
        try {
            $stmt = $this->db->query("
                SELECT 
                    COUNT(*) as total,
                    SUM(CASE WHEN is_approved IS NULL THEN 1 ELSE 0 END) as pending,
                    SUM(CASE WHEN is_approved = 1 THEN 1 ELSE 0 END) as approved,
                    SUM(CASE WHEN is_approved = 0 THEN 1 ELSE 0 END) as rejected
                FROM extensions
            ");
            
            $result = $stmt->fetch(PDO::FETCH_ASSOC);
            return $result ?: [
                'total' => 0,
                'pending' => 0,
                'approved' => 0,
                'rejected' => 0
            ];
            
        } catch (Exception $e) {
            error_log("getExtensionStats error: " . $e->getMessage());
            return [
                'total' => 0,
                'pending' => 0,
                'approved' => 0,
                'rejected' => 0
            ];
        }
    }
      /**
     * Approve extension
     */
    private function approveExtension() {
        $input = json_decode(file_get_contents('php://input'), true);
        $extension_id = $input['extension_id'] ?? null;
        
        if (!$extension_id) {
            $this->sendError('Extension ID required', 400);
            return;
        }
        
        // Use id column instead of extension_id column since frontend sends the primary key
        $stmt = $this->db->prepare("
            UPDATE extensions 
            SET is_approved = 1, updated_at = NOW() 
            WHERE id = ?
        ");
        $stmt->execute([$extension_id]);
        
        if ($stmt->rowCount() > 0) {
            $this->sendSuccess(['message' => 'Extension approved successfully']);
        } else {
            $this->sendError('Extension not found', 404);
        }
    }
      /**
     * Reject extension
     */
    private function rejectExtension() {
        $input = json_decode(file_get_contents('php://input'), true);
        $extension_id = $input['extension_id'] ?? null;
        
        if (!$extension_id) {
            $this->sendError('Extension ID required', 400);
            return;
        }
        
        // Use id column instead of extension_id column since frontend sends the primary key
        $stmt = $this->db->prepare("
            UPDATE extensions 
            SET is_approved = 0, updated_at = NOW() 
            WHERE id = ?
        ");
        $stmt->execute([$extension_id]);
        
        if ($stmt->rowCount() > 0) {
            $this->sendSuccess(['message' => 'Extension rejected successfully']);
        } else {
            $this->sendError('Extension not found', 404);
        }
    }
      /**
     * Sync extensions from GitHub
     */
    private function syncExtensions() {
        try {
            // Include the GitHub monitor class
            require_once __DIR__ . '/github_monitor.php';
            
            // Test database connection first
            if (!$this->db) {
                throw new Exception("Database connection not available");
            }
            
            // Test that we can query the database
            $this->db->query("SELECT 1");
            
            $monitor = new GitHubExtensionMonitor();
            $monitor->initializeSystemSettings();
            
            // Capture output
            ob_start();
            $monitor->monitor();
            $output = ob_get_clean();
            
            $this->sendSuccess([
                'message' => 'GitHub sync completed successfully',
                'output' => $output
            ]);
            
        } catch (Exception $e) {
            error_log("Sync Extensions Error: " . $e->getMessage());
            $this->sendError('Sync failed', 500);
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
    $api = new ExtensionsAdminAPI();
    $api->handleRequest();
} catch (Exception $e) {
    error_log("Fatal error in ExtensionsAdminAPI: " . $e->getMessage());
    http_response_code(500);
    echo json_encode([
        'success' => false,
        'error' => 'Internal server error occurred'
    ]);
}
?>