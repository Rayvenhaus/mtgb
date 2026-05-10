-- MTGB release_info release page URL migration.
-- Adds a separate human-facing release page URL while preserving
-- setup_url as the machine-download installer location.

ALTER TABLE `release_info`
    ADD COLUMN `release_page_url` varchar(500)
    COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT ''
    AFTER `setup_url`;
