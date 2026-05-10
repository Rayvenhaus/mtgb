<?php
// ============================================================
// MTGB Community API — Release Publish
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Called by the GitHub Actions pipeline when a new release
// is tagged. Inserts the new release record and marks all
// previous records as not current.
// Protected by API key — never called by MTGB clients directly.
// [USAGE] POST /mtgb/v1/release/publish
// [RETURNS] JSON — { status, message, data: { id, version, is_beta } }
// ============================================================

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/response.php';

set_cors_headers();
require_method('POST');

// ── API key authentication ────────────────────────────────────
// Key must be in X-Publish-Key header
$key = $_SERVER['HTTP_X_PUBLISH_KEY'] ?? '';

if (empty($key) ||
    !hash_equals(RELEASE_PUBLISH_KEY, $key))
{
    send_error(
        'Unauthorised. The Ministry does not recognise you.',
        401
    );
}

$body = get_json_body();

// ── Validate required fields ──────────────────────────────────
$version = trim($body['version'] ?? '');
if (empty($version) || !validate_version($version)) {
    send_error('Invalid or missing version.', 400);
}

$setupUrl = trim($body['setup_url'] ?? '');
if (empty($setupUrl) || !filter_var(
    $setupUrl, FILTER_VALIDATE_URL)) {
    send_error('Invalid or missing setup_url.', 400);
}

$releasePageUrl = trim($body['release_page_url'] ?? '');
if (!empty($releasePageUrl) && !filter_var(
    $releasePageUrl, FILTER_VALIDATE_URL)) {
    send_error('Invalid release_page_url.', 400);
}

$releaseNotes = trim($body['release_notes'] ?? '');
if (empty($releaseNotes)) {
    send_error('Missing release_notes.', 400);
}

$releaseDate = trim($body['release_date'] ?? '');
if (empty($releaseDate)) {
    $releaseDate = date('Y-m-d H:i:s');
}

$isBeta = filter_var(
    $body['is_beta'] ?? false,
    FILTER_VALIDATE_BOOLEAN);

$db = get_db();
$db->beginTransaction();

try {
    // Mark all existing releases as not current
    $db->exec('UPDATE release_info SET is_current = 0');

    // Insert new release
    $stmt = $db->prepare('
        INSERT INTO release_info
            (version, release_date, setup_url, release_page_url,
             release_notes, is_beta, is_current)
        VALUES
            (:version, :release_date, :setup_url, :release_page_url,
             :release_notes, :is_beta, 1)
    ');

    $stmt->execute([
        ':version'       => sanitise_string(
                                $version, MAX_VERSION_LENGTH),
        ':release_date'  => $releaseDate,
        ':setup_url'     => $setupUrl,
        ':release_page_url' => $releasePageUrl,
        ':release_notes' => $releaseNotes,
        ':is_beta'       => $isBeta ? 1 : 0,
    ]);

    $id = (int)$db->lastInsertId();
    $db->commit();

    send_success(
        'Release published. ' .
        'The Ministry has updated the records.',
        [
            'id'      => $id,
            'version' => $version,
            'is_beta' => $isBeta,
        ]
    );

} catch (Exception $e) {
    $db->rollBack();
    send_error(
        'Failed to publish release. ' .
        'The Ministry is investigating.',
        500,
        $e->getMessage()
    );
}
