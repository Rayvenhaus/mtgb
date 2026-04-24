<?php
// ============================================================
// MTGB Community API — Telemetry Ingest
// Copyright © 2026 Myndworx Asylum & Steven Sheeley — All rights reserved.
// https://www.myndworx.com
//
// Part of the MTGB open source project
// https://github.com/Rayvenhaus/mtgb
// Licensed under the MIT Licence — see LICENSE for details
//
// The Ministry of Printer Observation & Void Containment
// ============================================================
// Receives daily anonymous telemetry pings from MTGB clients.
// One ping per installation per 23 hours maximum.
// No personal data. No identifying information.
// Boring, beautiful, anonymous numbers.
// [USAGE] POST /mtgb/v1/telemetry
// [RETURNS] JSON — { status, message, data: { accepted, ping_id? } }
// ============================================================

require_once __DIR__ . '/config.php';
require_once __DIR__ . '/response.php';

set_cors_headers();
require_method('POST');
require_mtgb_client();

$body      = get_json_body();
$installId = require_install_id($body);

// ── Validate required fields ──────────────────────────────────

$mtgbVersion = trim($body['mtgb_version'] ?? '');
if (empty($mtgbVersion) || !validate_version($mtgbVersion)) {
    send_error(
        'Invalid or missing mtgb_version. ' .
        'The Ministry requires a version number.',
        400
    );
}

// ── Rate limiting ─────────────────────────────────────────────
// One ping per install ID per 23 hours maximum.
// Silently accept but do not insert if too soon.

$db = get_db();

$rateCheck = $db->prepare('
    SELECT MAX(pinged_at) AS last_ping
    FROM telemetry_pings
    WHERE install_id = :install_id
');
$rateCheck->execute([':install_id' => $installId]);
$lastPing = $rateCheck->fetchColumn();

if ($lastPing) {
    $hoursSince = (time() - strtotime($lastPing)) / 3600;
    if ($hoursSince < TELEMETRY_MIN_INTERVAL_HOURS) {
        // Too soon — acknowledge but don't insert
        send_success(
            'Ping acknowledged. ' .
            'The Ministry notes you are eager.',
            ['accepted' => false,
             'reason'   => 'rate_limited']
        );
    }
}

// ── Ensure installation record exists ─────────────────────────

ensure_installation(
    $db,
    $installId,
    $mtgbVersion,
    trim($body['windows_version'] ?? ''),
    trim($body['windows_build']   ?? '')
);

// ── Insert telemetry ping ─────────────────────────────────────

$db->beginTransaction();

try {
    $pingStmt = $db->prepare('
        INSERT INTO telemetry_pings
            (install_id, mtgb_version, printer_count,
             grouping_enabled, webhook_enabled,
             quiet_hours_enabled, sound_enabled,
             poll_success_count, poll_failure_count,
             toast_success_count, toast_failure_count,
             pinged_at)
        VALUES
            (:install_id, :mtgb_version, :printer_count,
             :grouping_enabled, :webhook_enabled,
             :quiet_hours_enabled, :sound_enabled,
             :poll_success_count, :poll_failure_count,
             :toast_success_count, :toast_failure_count,
             NOW())
    ');

    $pingStmt->execute([
        ':install_id'           => $installId,
        ':mtgb_version'         => sanitise_string(
                                       $mtgbVersion,
                                       MAX_VERSION_LENGTH),
        ':printer_count'        => (int)($body['printer_count']        ?? 0),
        ':grouping_enabled'     => (int)(bool)($body['grouping_enabled']     ?? false),
        ':webhook_enabled'      => (int)(bool)($body['webhook_enabled']      ?? false),
        ':quiet_hours_enabled'  => (int)(bool)($body['quiet_hours_enabled']  ?? false),
        ':sound_enabled'        => (int)(bool)($body['sound_enabled']        ?? false),
        ':poll_success_count'   => (int)($body['poll_success_count']   ?? 0),
        ':poll_failure_count'   => (int)($body['poll_failure_count']   ?? 0),
        ':toast_success_count'  => (int)($body['toast_success_count']  ?? 0),
        ':toast_failure_count'  => (int)($body['toast_failure_count']  ?? 0),
    ]);

    $pingId = (int)$db->lastInsertId();

    // ── Insert printer types ──────────────────────────────────

    $printers = $body['printers'] ?? [];
    if (is_array($printers)) {
        $printerStmt = $db->prepare('
            INSERT INTO printer_types
                (ping_id, install_id, integration,
                 model_brand, model_name)
            VALUES
                (:ping_id, :install_id, :integration,
                 :model_brand, :model_name)
        ');

        foreach ($printers as $printer) {
            if (!is_array($printer)) continue;

            $printerStmt->execute([
                ':ping_id'     => $pingId,
                ':install_id'  => $installId,
                ':integration' => sanitise_string(
                                      $printer['integration'] ?? '',
                                      MAX_INTEGRATION_LENGTH),
                ':model_brand' => sanitise_string(
                                      $printer['model_brand'] ?? '',
                                      MAX_MODEL_LENGTH),
                ':model_name'  => sanitise_string(
                                      $printer['model_name'] ?? '',
                                      MAX_MODEL_LENGTH),
            ]);
        }
    }

    // ── Insert enabled events ─────────────────────────────────

    $events = $body['enabled_events'] ?? [];
    if (is_array($events)) {
        $eventStmt = $db->prepare('
            INSERT INTO enabled_events
                (ping_id, install_id, event_id)
            VALUES
                (:ping_id, :install_id, :event_id)
        ');

        foreach ($events as $eventId) {
            if (!is_string($eventId)) continue;

            $clean = sanitise_string(
                $eventId,
                MAX_EVENT_ID_LENGTH);

            if (empty($clean)) continue;

            $eventStmt->execute([
                ':ping_id'    => $pingId,
                ':install_id' => $installId,
                ':event_id'   => $clean,
            ]);
        }
    }

    $db->commit();

    send_success(
        'Ping received. The scribes are grateful.',
        ['accepted' => true,
         'ping_id'  => $pingId]
    );

} catch (Exception $e) {
    $db->rollBack();
    send_error(
        'Failed to record telemetry. ' .
        'The Ministry apologises for the inconvenience.',
        500,
        $e->getMessage()
    );
}