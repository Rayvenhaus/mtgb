-- ============================================================
-- MTGB Community API — Database Schema
-- Copyright © 2026 Myndworx Asylum & Steven Sheeley
-- All rights reserved. https://www.myndworx.com
--
-- Part of the MTGB open source project
-- https://github.com/Rayvenhaus/mtgb
-- Licensed under the MIT Licence — see LICENSE for details
--
-- The Ministry of Printer Observation & Void Containment
-- ============================================================
-- MariaDB / MySQL 8.0+ schema for the MTGB Community API.
-- Creates the mtgb_community database, all tables, indexes,
-- foreign keys, and reporting views.
--
-- Run once on a fresh server:
--   mysql -u root -p < schema.sql
--
-- The DEFINER clause has been intentionally omitted from
-- all views — they will be created under whichever user
-- runs this script.
-- ============================================================

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
SET time_zone = "+00:00";
SET NAMES utf8mb4;

-- ── Database ──────────────────────────────────────────────────

CREATE DATABASE IF NOT EXISTS `mtgb_community`
    DEFAULT CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE `mtgb_community`;

-- ── Tables ────────────────────────────────────────────────────

-- One row per anonymous MTGB installation.
-- install_id is a UUID v4 generated on the client.
-- No personal data. No identifying information.

DROP TABLE IF EXISTS `enabled_events`;
DROP TABLE IF EXISTS `printer_types`;
DROP TABLE IF EXISTS `telemetry_pings`;
DROP TABLE IF EXISTS `install_locations`;
DROP TABLE IF EXISTS `installations`;

CREATE TABLE `installations` (
    `id`              bigint        NOT NULL AUTO_INCREMENT,
    `install_id`      char(36)      COLLATE utf8mb4_unicode_ci NOT NULL,
    `first_seen`      datetime      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `mtgb_version`    varchar(20)   COLLATE utf8mb4_unicode_ci NOT NULL,
    `windows_version` varchar(50)   COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `windows_build`   varchar(20)   COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `consent_date`    datetime      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_install_id`   (`install_id`),
    KEY        `idx_first_seen`  (`first_seen`),
    KEY        `idx_mtgb_version`(`mtgb_version`)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='One row per anonymous MTGB installation.';

-- Daily telemetry pings from MTGB installations.
-- One ping per install ID per 23 hours maximum.

CREATE TABLE `telemetry_pings` (
    `id`                  bigint      NOT NULL AUTO_INCREMENT,
    `install_id`          char(36)    COLLATE utf8mb4_unicode_ci NOT NULL,
    `pinged_at`           datetime    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `mtgb_version`        varchar(20) COLLATE utf8mb4_unicode_ci NOT NULL,
    `printer_count`       int         NOT NULL DEFAULT 0,
    `grouping_enabled`    tinyint(1)  NOT NULL DEFAULT 0,
    `webhook_enabled`     tinyint(1)  NOT NULL DEFAULT 0,
    `quiet_hours_enabled` tinyint(1)  NOT NULL DEFAULT 0,
    `sound_enabled`       tinyint(1)  NOT NULL DEFAULT 0,
    `poll_success_count`  int         NOT NULL DEFAULT 0,
    `poll_failure_count`  int         NOT NULL DEFAULT 0,
    `toast_success_count` int         NOT NULL DEFAULT 0,
    `toast_failure_count` int         NOT NULL DEFAULT 0,
    PRIMARY KEY (`id`),
    KEY `idx_install_id`  (`install_id`),
    KEY `idx_pinged_at`   (`pinged_at`),
    KEY `idx_mtgb_version`(`mtgb_version`),
    CONSTRAINT `fk_pings_install`
        FOREIGN KEY (`install_id`)
        REFERENCES `installations` (`install_id`)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Daily telemetry pings from MTGB installations.';

-- Printer fleet composition per telemetry ping.
-- Integration type and model only — no printer names.

CREATE TABLE `printer_types` (
    `id`          bigint      NOT NULL AUTO_INCREMENT,
    `ping_id`     bigint      NOT NULL,
    `install_id`  char(36)    COLLATE utf8mb4_unicode_ci NOT NULL,
    `integration` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '',
    `model_brand` varchar(50) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    `model_name`  varchar(100)COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    PRIMARY KEY (`id`),
    KEY `idx_ping_id`    (`ping_id`),
    KEY `idx_integration`(`integration`),
    KEY `idx_model_brand`(`model_brand`),
    CONSTRAINT `fk_printer_types_ping`
        FOREIGN KEY (`ping_id`)
        REFERENCES `telemetry_pings` (`id`)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Printer fleet composition per telemetry ping.';

-- Enabled event types per telemetry ping.
-- Event IDs only — no content, no notification text.

CREATE TABLE `enabled_events` (
    `id`         bigint      NOT NULL AUTO_INCREMENT,
    `ping_id`    bigint      NOT NULL,
    `install_id` char(36)    COLLATE utf8mb4_unicode_ci NOT NULL,
    `event_id`   varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
    PRIMARY KEY (`id`),
    KEY `idx_ping_id` (`ping_id`),
    KEY `idx_event_id`(`event_id`),
    CONSTRAINT `fk_enabled_events_ping`
        FOREIGN KEY (`ping_id`)
        REFERENCES `telemetry_pings` (`id`)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Enabled event types per telemetry ping.';

-- Community map registrations.
-- State or territory level only. No city. No postcode.
-- One row per install ID — upserted on re-registration.

CREATE TABLE `install_locations` (
    `id`           bigint       NOT NULL AUTO_INCREMENT,
    `install_id`   char(36)     COLLATE utf8mb4_unicode_ci NOT NULL,
    `country_code` char(2)      COLLATE utf8mb4_unicode_ci NOT NULL,
    `country_name` varchar(100) COLLATE utf8mb4_unicode_ci NOT NULL,
    `state_name`   varchar(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
    `display_name` varchar(150) COLLATE utf8mb4_unicode_ci NOT NULL,
    `consented_at` datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_location_install`(`install_id`),
    KEY `idx_country_code`(`country_code`),
    KEY `idx_state_name`  (`state_name`),
    CONSTRAINT `fk_locations_install`
        FOREIGN KEY (`install_id`)
        REFERENCES `installations` (`install_id`)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Community map registrations. State/territory level only.';

-- Release info — current and historical release records.
-- Updated by the GitHub Actions pipeline on each tag.

CREATE TABLE IF NOT EXISTS `release_info` (
    `id`            bigint        NOT NULL AUTO_INCREMENT,
    `version`       varchar(20)   COLLATE utf8mb4_unicode_ci NOT NULL,
    `release_date`  datetime      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `msix_url`      varchar(500)  COLLATE utf8mb4_unicode_ci NOT NULL,
    `zip_url`       varchar(500)  COLLATE utf8mb4_unicode_ci NOT NULL,
    `release_notes` text          COLLATE utf8mb4_unicode_ci NOT NULL,
    `is_current`    tinyint(1)    NOT NULL DEFAULT 0,
    PRIMARY KEY (`id`),
    KEY `idx_version`   (`version`),
    KEY `idx_is_current`(`is_current`)
) ENGINE=InnoDB
  DEFAULT CHARSET=utf8mb4
  COLLATE=utf8mb4_unicode_ci
  COMMENT='Release records. is_current=1 is what clients receive.';

-- ── Reporting views ───────────────────────────────────────────
-- These views are used for monitoring and community stats.
-- DEFINER is intentionally omitted — inherits from running user.

DROP VIEW IF EXISTS `v_active_installations`;
CREATE VIEW `v_active_installations` AS
    SELECT
        COUNT(DISTINCT `install_id`) AS `active_installs`,
        MIN(`pinged_at`)             AS `earliest_ping`,
        MAX(`pinged_at`)             AS `latest_ping`
    FROM `telemetry_pings`
    WHERE `pinged_at` >= NOW() - INTERVAL 30 DAY;

DROP VIEW IF EXISTS `v_version_distribution`;
CREATE VIEW `v_version_distribution` AS
    SELECT
        `mtgb_version`,
        COUNT(DISTINCT `install_id`) AS `install_count`,
        MAX(`pinged_at`)             AS `last_seen`
    FROM `telemetry_pings`
    GROUP BY `mtgb_version`
    ORDER BY `last_seen` DESC;

DROP VIEW IF EXISTS `v_event_popularity`;
CREATE VIEW `v_event_popularity` AS
    SELECT
        `event_id`,
        COUNT(DISTINCT `install_id`) AS `installs_with_event`,
        COUNT(*)                     AS `total_occurrences`
    FROM `enabled_events`
    GROUP BY `event_id`
    ORDER BY `installs_with_event` DESC;

DROP VIEW IF EXISTS `v_integration_popularity`;
CREATE VIEW `v_integration_popularity` AS
    SELECT
        `integration`,
        COUNT(*)                     AS `printer_count`,
        COUNT(DISTINCT `install_id`) AS `install_count`
    FROM `printer_types`
    GROUP BY `integration`
    ORDER BY `printer_count` DESC;

DROP VIEW IF EXISTS `v_map_by_country`;
CREATE VIEW `v_map_by_country` AS
    SELECT
        `country_code`,
        `country_name`,
        `state_name`,
        `display_name`,
        COUNT(*) AS `dot_count`
    FROM `install_locations`
    GROUP BY
        `country_code`,
        `country_name`,
        `state_name`,
        `display_name`
    ORDER BY
        `country_name` ASC,
        `state_name`   ASC;

-- ============================================================
-- Schema complete.
-- The Ministry has its tables. In triplicate.
-- No llamas were normalised in the making of this schema.
-- ============================================================