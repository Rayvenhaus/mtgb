<?php
// ============================================================
// MTGB Community API — Configuration
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Database configuration, constants, shared helpers,
// and bootstrap logic for the MTGB Community API.
// Requires config.local (INI format) — never committed to source.
// If config.local is missing, the API will not start.
// [USAGE] Included by all endpoint files via require_once
// [RETURNS] N/A — configuration and helper functions only
// ============================================================

// ── Load local configuration — REQUIRED ──────────────────────
// config.local is an INI file, not PHP — the web server
// cannot serve it even if misconfigured. It lives alongside
// this file but is excluded from source control via .gitignore.
// If it does not exist, we stop immediately. Loudly.

$_localConfigPath = __DIR__ . '/config.local';

if (!file_exists($_localConfigPath)) {
    http_response_code(503);
    header('Content-Type: application/json');
    echo json_encode([
        'status'  => false,
        'message' => 'Service unavailable. ' .
                     'The Ministry is not configured.'
    ]);
    error_log(
        '[MTGB Community] FATAL: config.local not found. ' .
        'Copy config.local.example to config.local ' .
        'and fill in your credentials.'
    );
    exit;
}

$_config = parse_ini_file($_localConfigPath);

if ($_config === false) {
    http_response_code(503);
    header('Content-Type: application/json');
    echo json_encode([
        'status'  => false,
        'message' => 'Service unavailable. ' .
                     'The Ministry configuration is invalid.'
    ]);
    error_log(
        '[MTGB Community] FATAL: config.local could not be parsed. ' .
        'Check the file format against config.local.example.'
    );
    exit;
}

// ── Required keys — fail loudly if any are missing ───────────
$_required = [
    'db_host', 'db_name', 'db_user',
    'db_pass', 'db_charset', 'release_publish_key'
];

foreach ($_required as $_key) {
    if (empty($_config[$_key])) {
        http_response_code(503);
        header('Content-Type: application/json');
        echo json_encode([
            'status'  => false,
            'message' => 'Service unavailable. ' .
                         'The Ministry configuration is incomplete.'
        ]);
        error_log(
            '[MTGB Community] FATAL: config.local is missing ' .
            'required key: ' . $_key
        );
        exit;
    }
}

// ── Define constants from config.local ───────────────────────
define('DB_HOST',             $_config['db_host']);
define('DB_NAME',             $_config['db_name']);
define('DB_USER',             $_config['db_user']);
define('DB_PASS',             $_config['db_pass']);
define('DB_CHARSET',          $_config['db_charset']);
define('RELEASE_PUBLISH_KEY', $_config['release_publish_key']);
define('DEBUG_MODE',   filter_var(
    $_config['debug_mode'] ?? false, FILTER_VALIDATE_BOOLEAN));

unset($_localConfigPath, $_config, $_required, $_key);

// ── API constants ─────────────────────────────────────────────
define('API_VERSION',              '1.0.0');
define('MTGB_USER_AGENT_PREFIX',   'MTGB/');
define('TELEMETRY_MIN_INTERVAL_HOURS', 23);
define('MAP_RATE_LIMIT_PER_DAY',   5);
define('ALLOWED_ORIGIN',           '*');

// ── Validation constants ──────────────────────────────────────
define('UUID_PATTERN',
    '/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-' .
    '[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i');

define('MAX_VERSION_LENGTH',     20);
define('MAX_COUNTRY_LENGTH',    100);
define('MAX_STATE_LENGTH',      100);
define('MAX_EVENT_ID_LENGTH',    50);
define('MAX_INTEGRATION_LENGTH', 50);
define('MAX_MODEL_LENGTH',      100);

// ── Database connection ───────────────────────────────────────

function get_db(): PDO
{
    static $pdo = null;

    if ($pdo !== null) {
        return $pdo;
    }

    try {
        $dsn = sprintf(
            'mysql:host=%s;dbname=%s;charset=%s',
            DB_HOST,
            DB_NAME,
            DB_CHARSET
        );

        $pdo = new PDO($dsn, DB_USER, DB_PASS, [
            PDO::ATTR_ERRMODE            => PDO::ERRMODE_EXCEPTION,
            PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
            PDO::ATTR_EMULATE_PREPARES   => false,
        ]);

        return $pdo;
    } catch (PDOException $e) {
        error_log(
            '[MTGB Community] DB connection failed: ' .
            $e->getMessage()
        );
        http_response_code(503);
        header('Content-Type: application/json');
        echo json_encode([
            'status'  => false,
            'message' => 'Service temporarily unavailable. ' .
                         'The Ministry is investigating.'
        ]);
        exit;
    }
}

// ── Request helpers ───────────────────────────────────────────

function get_json_body(): array
{
    $raw = file_get_contents('php://input');

    if (empty($raw)) {
        return [];
    }

    $decoded = json_decode($raw, true);

    if (json_last_error() !== JSON_ERROR_NONE) {
        return [];
    }

    return $decoded ?? [];
}

function validate_install_id(string $id): bool
{
    return preg_match(UUID_PATTERN, $id) === 1;
}

function validate_version(string $version): bool
{
    return strlen($version) <= MAX_VERSION_LENGTH &&
           preg_match('/^\d+\.\d+\.\d+/', $version) === 1;
}

function sanitise_string(
    string $value,
    int    $maxLength): string
{
    return substr(
        trim(strip_tags($value)),
        0,
        $maxLength
    );
}

// ── CORS headers ──────────────────────────────────────────────

function set_cors_headers(): void
{
    header('Access-Control-Allow-Origin: ' . ALLOWED_ORIGIN);
    header('Access-Control-Allow-Methods: GET, POST, DELETE, OPTIONS');
    header('Access-Control-Allow-Headers: Content-Type, X-Install-ID');
    header('Content-Type: application/json; charset=utf-8');

    if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
        http_response_code(204);
        exit;
    }
}

// ── User agent validation ─────────────────────────────────────

function validate_user_agent(): bool
{
    $ua = $_SERVER['HTTP_USER_AGENT'] ?? '';
    return str_starts_with($ua, MTGB_USER_AGENT_PREFIX);
}