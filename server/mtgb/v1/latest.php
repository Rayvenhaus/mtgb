<?php
// ============================================================
// MTGB Community API — Latest Release
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Returns the current release version, download URLs and
// release notes. MTGB clients check this on startup and
// every 72 hours during non-quiet hours.
// [USAGE] GET /mtgb/v1/release/latest
// [RETURNS] JSON — { status, message, data: { version, release_date,
//           msix_url, zip_url, release_notes } }
// ============================================================

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/response.php';

set_cors_headers();
require_method('GET');
require_mtgb_client();

$db = get_db();

$stmt = $db->prepare('
    SELECT
        version,
        release_date,
        msix_url,
        zip_url,
        release_notes
    FROM release_info
    WHERE is_current = 1
    ORDER BY release_date DESC
    LIMIT 1
');

$stmt->execute();
$release = $stmt->fetch();

if (!$release) {
    send_error(
        'No release information available. ' .
        'The Ministry is investigating.',
        404
    );
}

send_success(
    'Release information retrieved. ' .
    'The Ministry keeps meticulous records.',
    [
        'version'       => $release['version'],
        'release_date'  => $release['release_date'],
        'msix_url'      => $release['msix_url'],
        'zip_url'       => $release['zip_url'],
        'release_notes' => $release['release_notes'],
    ]
);
