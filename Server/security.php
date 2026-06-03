<?php
/**
 * Save Vault - shared security helpers.
 *
 * Included by the JSON API endpoints (and, in part, by site pages) to centralise
 * error handling, CORS, security headers, rate limiting and small validation
 * helpers. This file only DEFINES things and must never emit output when
 * included, so it is safe to require from anywhere.
 */

if (!defined('SAVEVAULT_SECURITY')) {
    define('SAVEVAULT_SECURITY', 1);

    // ---------------------------------------------------------------------
    // Fail safe: log everything server-side, never render errors to clients.
    // PHP notices/warnings/fatals must not leak file paths, SQL or stack
    // traces into API responses or HTML pages.
    // ---------------------------------------------------------------------
    @ini_set('display_errors', '0');
    @ini_set('display_startup_errors', '0');
    @ini_set('log_errors', '1');
    error_reporting(E_ALL);

    /**
     * Browser origins permitted to make cross-origin calls to the JSON APIs.
     * The desktop client sends no Origin header and is unaffected by CORS.
     */
    function sv_allowed_origins() {
        return [
            'https://vault.etka.co.uk',
            'https://www.vault.etka.co.uk',
        ];
    }

    /**
     * Best-effort client IP. Intentionally uses REMOTE_ADDR only: X-Forwarded-For
     * and similar headers are attacker-controlled and must not be trusted for
     * security decisions (rate limiting, logging) unless a trusted proxy is set.
     */
    function sv_client_ip() {
        return $_SERVER['REMOTE_ADDR'] ?? '0.0.0.0';
    }

    /**
     * Emit CORS headers using a strict allow-list and short-circuit the
     * preflight (OPTIONS) request. Only echoes an Origin that is allow-listed,
     * so the previous wildcard "*" behaviour is removed.
     */
    function sv_apply_cors($methods = 'GET, POST, OPTIONS') {
        $origin = $_SERVER['HTTP_ORIGIN'] ?? '';
        if ($origin !== '' && in_array($origin, sv_allowed_origins(), true)) {
            header('Access-Control-Allow-Origin: ' . $origin);
            header('Vary: Origin');
            header('Access-Control-Allow-Methods: ' . $methods);
            header('Access-Control-Allow-Headers: Content-Type, Authorization, X-Requested-With');
            header('Access-Control-Max-Age: 86400');
        }

        if (($_SERVER['REQUEST_METHOD'] ?? '') === 'OPTIONS') {
            http_response_code(204);
            exit;
        }
    }

    /**
     * Bootstrap a JSON API endpoint: hardened error handling, JSON content type,
     * no-store caching (responses can contain tokens / PII) and CORS. Generic
     * security headers (CSP, HSTS, X-Frame-Options, ...) are applied globally in
     * .htaccess so they cover both APIs and HTML pages.
     */
    function sv_init_api($methods = 'GET, POST, OPTIONS') {
        @ini_set('display_errors', '0');
        error_reporting(E_ALL);
        if (!headers_sent()) {
            header('Content-Type: application/json; charset=utf-8');
            header('Cache-Control: no-store');
            header('X-Content-Type-Options: nosniff');
        }
        sv_apply_cors($methods);
    }

    /**
     * Lightweight file-based sliding-window rate limiter.
     *
     * Fails OPEN on any storage error so a disk/permission problem can never
     * lock legitimate users out; it is a defence-in-depth control, not the
     * primary auth gate.
     *
     * @param string $bucket        Logical action name (e.g. "login").
     * @param int    $maxAttempts   Allowed attempts within the window.
     * @param int    $windowSeconds Window length in seconds.
     * @return bool  true if allowed, false if the caller has hit the limit.
     */
    function sv_rate_limit($bucket, $maxAttempts, $windowSeconds) {
        try {
            $dir = __DIR__ . '/logs/ratelimit';
            if (!is_dir($dir)) {
                @mkdir($dir, 0700, true);
            }
            if (!is_dir($dir) || !is_writable($dir)) {
                return true; // fail open
            }

            $key  = $bucket . '|' . sv_client_ip();
            $file = $dir . '/' . hash('sha256', $key) . '.json';
            $now  = time();

            $fp = @fopen($file, 'c+');
            if ($fp === false) {
                return true; // fail open
            }

            try {
                @flock($fp, LOCK_EX);
                $raw  = stream_get_contents($fp);
                $hits = $raw ? json_decode($raw, true) : [];
                if (!is_array($hits)) {
                    $hits = [];
                }

                $cutoff = $now - $windowSeconds;
                $hits = array_values(array_filter($hits, function ($t) use ($cutoff) {
                    return is_numeric($t) && $t > $cutoff;
                }));

                $allowed = count($hits) < $maxAttempts;
                if ($allowed) {
                    $hits[] = $now;
                    rewind($fp);
                    ftruncate($fp, 0);
                    fwrite($fp, json_encode($hits));
                    fflush($fp);
                }
                return $allowed;
            } finally {
                @flock($fp, LOCK_UN);
                @fclose($fp);
            }
        } catch (\Throwable $e) {
            error_log('sv_rate_limit error: ' . $e->getMessage());
            return true; // fail open
        }
    }

    /**
     * Enforce a rate limit for a JSON endpoint; emits HTTP 429 and exits when
     * the limit is exceeded. Assumes a JSON content type has been set.
     */
    function sv_rate_limit_or_die($bucket, $maxAttempts, $windowSeconds) {
        if (!sv_rate_limit($bucket, $maxAttempts, $windowSeconds)) {
            http_response_code(429);
            header('Retry-After: ' . (int) $windowSeconds);
            echo json_encode([
                'success' => false,
                'message' => 'Too many requests. Please wait a moment and try again.',
                'data'    => null,
            ]);
            exit;
        }
    }

    /**
     * Validate that a user-supplied URL is a safe absolute http(s) link.
     * Empty/null is treated as valid (callers decide whether empty is allowed).
     */
    function sv_is_safe_url($url) {
        if ($url === '' || $url === null) {
            return true;
        }
        if (!is_string($url) || strlen($url) > 2048) {
            return false;
        }
        $scheme = strtolower((string) parse_url($url, PHP_URL_SCHEME));
        return in_array($scheme, ['http', 'https'], true);
    }

    // Canonical public origin. Used to build links inside emails — intentionally
    // hardcoded (never derived from the Host header, which is attacker-controlled
    // and would otherwise allow poisoning password-reset links).
    if (!defined('SV_PUBLIC_URL')) {
        define('SV_PUBLIC_URL', 'https://vault.etka.co.uk');
    }

    // From / Reply-To addresses for outgoing mail. On GoDaddy the From should be a
    // real mailbox on your domain for best deliverability — adjust if needed.
    if (!defined('SV_MAIL_FROM'))     define('SV_MAIL_FROM', 'no-reply@vault.etka.co.uk');
    if (!defined('SV_MAIL_REPLYTO'))  define('SV_MAIL_REPLYTO', 'support@vault.etka.co.uk');

    /**
     * Send a plain-text email via PHP mail(). Returns true on success.
     *
     * This is deliberately small and self-contained so it can be swapped for an
     * SMTP implementation later without touching callers. mail() on shared hosts
     * (incl. GoDaddy) can be flaky and prone to spam-foldering; if delivery is a
     * problem, replace the body of this function with an SMTP client.
     */
    function sv_send_mail($to, $subject, $body) {
        if (!is_string($to) || !filter_var($to, FILTER_VALIDATE_EMAIL)) {
            return false;
        }

        // Strip CR/LF from header-bound values to prevent header injection.
        $from    = preg_replace('/[\r\n]+/', '', SV_MAIL_FROM);
        $replyTo = preg_replace('/[\r\n]+/', '', SV_MAIL_REPLYTO);
        $subject = preg_replace('/[\r\n]+/', ' ', (string) $subject);

        $headers = implode("\r\n", [
            'From: Save Vault <' . $from . '>',
            'Reply-To: ' . $replyTo,
            'MIME-Version: 1.0',
            'Content-Type: text/plain; charset=UTF-8',
            'X-Mailer: SaveVault',
        ]);

        // -f sets the envelope sender (helps deliverability / SPF on many hosts).
        $ok = @mail($to, $subject, $body, $headers, '-f' . $from);
        if (!$ok) {
            error_log('sv_send_mail: mail() failed for recipient');
        }
        return $ok;
    }

    /**
     * Resolve the bearer JWT from either the Authorization header (desktop client)
     * or the HttpOnly `auth_token` session cookie (web UI). Sets $fromCookie so the
     * caller can apply the CSRF guard when auth came from the cookie.
     *
     * @return string|null The raw token, or null if none present.
     */
    function sv_bearer_token(&$fromCookie = null) {
        $fromCookie = false;

        $auth = $_SERVER['HTTP_AUTHORIZATION'] ?? $_SERVER['REDIRECT_HTTP_AUTHORIZATION'] ?? '';
        if (($auth === '' || $auth === null) && function_exists('apache_request_headers')) {
            foreach (apache_request_headers() as $k => $v) {
                if (strcasecmp($k, 'Authorization') === 0) { $auth = $v; break; }
            }
        }

        if (is_string($auth) && preg_match('/Bearer\s+(\S+)/i', $auth, $m)) {
            return $m[1];
        }

        if (!empty($_COOKIE['auth_token'])) {
            $fromCookie = true;
            return $_COOKIE['auth_token'];
        }

        return null;
    }

    /**
     * CSRF guard for cookie-authenticated requests: on state-changing methods,
     * require the X-Requested-With header (which cross-site callers cannot set
     * without a CORS preflight that our origin allow-list rejects). Emits 403 and
     * exits on failure. No-op for header-token (desktop client) auth.
     */
    function sv_csrf_guard($fromCookie) {
        if (!$fromCookie) {
            return;
        }
        $method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
        if (in_array($method, ['GET', 'HEAD', 'OPTIONS'], true)) {
            return;
        }
        if (strcasecmp($_SERVER['HTTP_X_REQUESTED_WITH'] ?? '', 'XMLHttpRequest') !== 0) {
            http_response_code(403);
            echo json_encode([
                'success' => false,
                'error'   => 'Missing required request header',
                'message' => 'Missing required request header',
            ]);
            exit;
        }
    }
}
