<?php
// ============================================================
// MTGB Community API — Response Helpers
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Shared response functions used by all API endpoints.
// Ensures consistent JSON response shape, correct HTTP codes,
// and that nothing internal leaks to the client.
// [USAGE] Included by all endpoint files via require_once
// [RETURNS] JSON — { status, message, version, data? }
// ============================================================

/**
 * Send a JSON response and exit.
 * All API responses go through here — consistent shape,
 * correct headers, nothing leaks.
 */
function send_response(
    bool   $status,
    string $message,
    array  $data = [],
    int    $httpCode = 200): never
{
    http_response_code($httpCode);

    $body = [
        'status'  => $status,
        'message' => $message,
        'version' => API_VERSION,
    ];

    if (!empty($data)) {
        $body['data'] = $data;
    }

    echo json_encode($body, JSON_UNESCAPED_UNICODE);
    exit;
}

/**
 * Send a success response.
 */
function send_success(
    string $message,
    array  $data = [],
    int    $httpCode = 200): never
{
    send_response(true, $message, $data, $httpCode);
}

/**
 * Send an error response.
 * Never exposes internal details — logs them instead.
 */
function send_error(
    string $message,
    int    $httpCode = 400,
    string $internalDetail = ''): never
{
    if (!empty($internalDetail)) {
        error_log(
            '[MTGB Community] Error: ' .
            $internalDetail
        );
    }

    send_response(false, $message, [], $httpCode);
}

/**
 * Reject the request if the HTTP method doesn't match.
 */
function require_method(string $method): void
{
    if ($_SERVER['REQUEST_METHOD'] !== strtoupper($method)) {
        send_error(
            'Method not allowed. ' .
            'The Ministry only accepts ' . $method . ' here.',
            405
        );
    }
}

/**
 * Reject the request if the User-Agent doesn't look like MTGB.
 */
function require_mtgb_client(): void
{
    if (!validate_user_agent()) {
        send_error(
            'Unrecognised client. ' .
            'The Ministry is watching.',
            403
        );
    }
}

/**
 * Get and validate the install ID from the request body or header.
 * Returns the validated install ID or sends an error response.
 */
function require_install_id(array $body): string
{
    $installId = $body['install_id']
        ?? $_SERVER['HTTP_X_INSTALL_ID']
        ?? '';

    if (empty($installId)) {
        send_error(
            'Install ID is required. ' .
            'The Ministry needs to know who is filing this form.',
            400
        );
    }

    if (!validate_install_id($installId)) {
        send_error(
            'Invalid install ID format. ' .
            'The Ministry does not recognise this identifier.',
            400
        );
    }

    return $installId;
}

/**
 * Ensure an installation record exists for the given install ID.
 * Creates one if it does not — upsert pattern.
 */
function ensure_installation(
    PDO    $db,
    string $installId,
    string $mtgbVersion,
    string $windowsVersion = '',
    string $windowsBuild = ''): void
{
    $stmt = $db->prepare('
        INSERT IGNORE INTO installations
            (install_id, mtgb_version, windows_version,
             windows_build, first_seen, consent_date)
        VALUES
            (:install_id, :mtgb_version, :windows_version,
             :windows_build, NOW(), NOW())
    ');

    $stmt->execute([
        ':install_id'      => $installId,
        ':mtgb_version'    => sanitise_string(
                                $mtgbVersion,
                                MAX_VERSION_LENGTH),
        ':windows_version' => sanitise_string(
                                $windowsVersion,
                                50),
        ':windows_build'   => sanitise_string(
                                $windowsBuild,
                                20),
    ]);
}