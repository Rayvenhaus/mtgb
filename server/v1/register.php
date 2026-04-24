<?php
// ============================================================
// MTGB Community API — Community Map Registration
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Registers or removes an anonymous installation dot
// on the MTGB community map. State or territory level only.
// The dots are anonymous. There is nothing to see here.
// Except tiny little unassuming dots.
// [USAGE] POST   /mtgb/v1/map/register — add to the map
//         DELETE /mtgb/v1/map/register — remove from the map
// [RETURNS] JSON — { status, message, data: { display_name?,
//           country_code?, state_name?, removed? } }
// ============================================================

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/response.php';

set_cors_headers();
require_mtgb_client();

$method = $_SERVER['REQUEST_METHOD'];

if ($method !== 'POST' && $method !== 'DELETE') {
    send_error(
        'Method not allowed. ' .
        'The Ministry accepts POST and DELETE here.',
        405
    );
}

$body      = get_json_body();
$installId = require_install_id($body);
$db        = get_db();

// ── DELETE — opt out ──────────────────────────────────────────

if ($method === 'DELETE') {
    $stmt = $db->prepare('
        DELETE FROM install_locations
        WHERE install_id = :install_id
    ');

    $stmt->execute([':install_id' => $installId]);

    $removed = $stmt->rowCount() > 0;

    send_success(
        $removed
            ? 'You have been removed from the map. ' .
              'The Ministry has filed the departure form.'
            : 'No registration found for this install ID. ' .
              'The Ministry checked. Twice.',
        ['removed' => $removed]
    );
}

// ── POST — register ───────────────────────────────────────────

// Validate country code
$countryCode = strtoupper(trim($body['country_code'] ?? ''));
if (empty($countryCode) ||
    !preg_match('/^[A-Z]{2}$/', $countryCode)) {
    send_error(
        'Invalid or missing country_code. ' .
        'The Ministry requires a valid ISO 3166-1 alpha-2 code.',
        400
    );
}

// Validate country name
$countryName = sanitise_string(
    $body['country_name'] ?? '',
    MAX_COUNTRY_LENGTH);

if (empty($countryName)) {
    send_error(
        'Invalid or missing country_name.',
        400
    );
}

// State name is optional — null for countries without states
$stateName = isset($body['state_name']) &&
             !empty(trim($body['state_name']))
    ? sanitise_string($body['state_name'], MAX_STATE_LENGTH)
    : null;

// Build display name
$displayName = $stateName
    ? sanitise_string(
        $stateName . ', ' . $countryName,
        150)
    : sanitise_string($countryName, 150);

// Rate limiting — max MAP_RATE_LIMIT_PER_DAY attempts per day
$rateCheck = $db->prepare('
    SELECT COUNT(*) FROM install_locations
    WHERE install_id  = :install_id
    AND   consented_at >= DATE_SUB(NOW(), INTERVAL 1 DAY)
');
$rateCheck->execute([':install_id' => $installId]);
$attempts = (int)$rateCheck->fetchColumn();

if ($attempts >= MAP_RATE_LIMIT_PER_DAY) {
    send_error(
        'Too many registration attempts. ' .
        'The Ministry asks that you try again tomorrow.',
        429
    );
}

// Ensure installation record exists
$mtgbVersion = sanitise_string(
    $body['mtgb_version'] ?? '0.0.0',
    MAX_VERSION_LENGTH);

ensure_installation($db, $installId, $mtgbVersion);

// Upsert — update if already registered, insert if not
$stmt = $db->prepare('
    INSERT INTO install_locations
        (install_id, country_code, country_name,
         state_name, display_name, consented_at)
    VALUES
        (:install_id, :country_code, :country_name,
         :state_name, :display_name, NOW())
    ON DUPLICATE KEY UPDATE
        country_code  = VALUES(country_code),
        country_name  = VALUES(country_name),
        state_name    = VALUES(state_name),
        display_name  = VALUES(display_name),
        consented_at  = NOW()
');

$stmt->execute([
    ':install_id'   => $installId,
    ':country_code' => $countryCode,
    ':country_name' => $countryName,
    ':state_name'   => $stateName,
    ':display_name' => $displayName,
]);

send_success(
    'Registration complete. ' .
    'You are now on the map. ' .
    'The dot is yours.',
    [
        'display_name' => $displayName,
        'country_code' => $countryCode,
        'state_name'   => $stateName,
    ]
);