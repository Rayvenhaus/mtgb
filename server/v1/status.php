<?php
// ============================================================
// MTGB Community API — Community Map Status
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Returns the current community map registration status
// for a given anonymous install ID.
// [USAGE] GET /mtgb/v1/map/status/{installId}
// [RETURNS] JSON — { status, message, data: { registered,
//           display_name?, country_code?, state_name?,
//           consented_at? } }
// ============================================================

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/response.php';

set_cors_headers();
require_method('GET');
require_mtgb_client();

// Install ID comes from the URL via .htaccess rewrite
$installId = trim($_GET['install_id'] ?? '');

if (empty($installId) || !validate_install_id($installId)) {
    send_error(
        'Invalid or missing install ID. ' .
        'The Ministry cannot locate your file.',
        400
    );
}

$db = get_db();

$stmt = $db->prepare('
    SELECT
        country_code,
        country_name,
        state_name,
        display_name,
        consented_at
    FROM install_locations
    WHERE install_id = :install_id
');

$stmt->execute([':install_id' => $installId]);
$location = $stmt->fetch();

if (!$location) {
    send_success(
        'Not registered on the community map.',
        [
            'registered'   => false,
            'display_name' => null,
        ]
    );
}

send_success(
    'Registration found. The dot exists.',
    [
        'registered'   => true,
        'display_name' => $location['display_name'],
        'country_code' => $location['country_code'],
        'state_name'   => $location['state_name'],
        'consented_at' => $location['consented_at'],
    ]
);