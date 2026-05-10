<?php
// ============================================================
// MTGB Community API — Installation Removal
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Removes all server-side records for an installation.
// [USAGE] DELETE /mtgb/v1/installations
// [BODY]  JSON — { install_id }
// ============================================================

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/response.php';

set_cors_headers();
require_method('DELETE');
require_mtgb_client();

$body = get_json_body();
$installId = require_install_id($body);

$db = get_db();

$db->beginTransaction();

try {
    $childTables = [
        'enabled_events',
        'printer_types',
        'install_locations',
        'telemetry_pings',
    ];

    foreach ($childTables as $table) {
        $childStmt = $db->prepare("
            DELETE FROM {$table}
            WHERE install_id = :install_id
        ");

        $childStmt->execute([
            ':install_id' => $installId,
        ]);
    }

    $stmt = $db->prepare('
        DELETE FROM installations
        WHERE install_id = :install_id
    ');

    $stmt->execute([
        ':install_id' => $installId,
    ]);

    $removed = $stmt->rowCount() > 0;
    $db->commit();
} catch (Throwable $ex) {
    $db->rollBack();
    throw $ex;
}

send_success(
    'Installation removed. The Ministry has shredded the file.',
    [
        'install_id' => $installId,
        'removed'    => $removed,
    ]
);
