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
// [RETURNS] JSON — { status, message, data: { id, version } }
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

$msixUrl = trim($body['msix_url'] ?? '');
if (empty($msixUrl) || !filter_var(
    $msixUrl, FILTER_VALIDATE_URL)) {
    send_error('Invalid or missing msix_url.', 400);
}

$zipUrl = trim($body['zip_url'] ?? '');
if (empty($zipUrl) || !filter_var(
    $zipUrl, FILTER_VALIDATE_URL)) {
    send_error('Invalid or missing zip_url.', 400);
}

$releaseNotes = trim($body['release_notes'] ?? '');
if (empty($releaseNotes)) {
    send_error('Missing release_notes.', 400);
}

$releaseDate = trim($body['release_date'] ?? '');
if (empty($releaseDate)) {
    $releaseDate = date('Y-m-d H:i:s');
}

$db = get_db();
$db->beginTransaction();

try {
    // Mark all existing releases as not current
    $db->exec('UPDATE release_info SET is_current = 0');

    // Insert new release
    $stmt = $db->prepare('
        INSERT INTO release_info
            (version, release_date, msix_url,
             zip_url, release_notes, is_current)
        VALUES
            (:version, :release_date, :msix_url,
             :zip_url, :release_notes, 1)
    ');

    $stmt->execute([
        ':version'       => sanitise_string(
                                $version, MAX_VERSION_LENGTH),
        ':release_date'  => $releaseDate,
        ':msix_url'      => $msixUrl,
        ':zip_url'       => $zipUrl,
        ':release_notes' => $releaseNotes,
    ]);

    $id = (int)$db->lastInsertId();
    $db->commit();

    send_success(
        'Release published. ' .
        'The Ministry has updated the records.',
        [
            'id'      => $id,
            'version' => $version,
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