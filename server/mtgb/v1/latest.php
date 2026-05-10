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
//           setup_url, release_page_url, release_notes, is_beta } }
// ============================================================

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/response.php';

set_cors_headers();
require_method('GET');
require_mtgb_client();

$db = get_db();
$includeBeta = filter_var(
    $_GET['include_beta'] ?? true,
    FILTER_VALIDATE_BOOLEAN,
    FILTER_NULL_ON_FAILURE);

if ($includeBeta === null) {
    $includeBeta = true;
}

$sql = $includeBeta
    ? '
        SELECT
            version,
            release_date,
            setup_url,
            release_page_url,
            release_notes,
            is_beta
        FROM release_info
        WHERE is_current = 1
        ORDER BY release_date DESC, id DESC
        LIMIT 1
    '
    : '
        SELECT
            version,
            release_date,
            setup_url,
            release_page_url,
            release_notes,
            is_beta
        FROM release_info
        WHERE is_beta = 0
        ORDER BY release_date DESC, id DESC
        LIMIT 1
    ';

$stmt = $db->prepare($sql);

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
        'setup_url'     => $release['setup_url'],
        'release_page_url' => $release['release_page_url'],
        'release_notes' => $release['release_notes'],
        'is_beta'       => (bool)$release['is_beta'],
    ]
);
