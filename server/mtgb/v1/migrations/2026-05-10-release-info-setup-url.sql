-- MTGB release_info migration for WiX/Burn setup releases.
-- Run once against the live community database before publishing
-- setup-based releases through /mtgb/v1/release/publish.

ALTER TABLE `release_info`
    ADD COLUMN `setup_url` varchar(500)
    COLLATE utf8mb4_unicode_ci NULL
    AFTER `release_date`;

UPDATE `release_info`
SET `setup_url` = `msix_url`
WHERE `setup_url` IS NULL
  AND `msix_url` IS NOT NULL;

ALTER TABLE `release_info`
    MODIFY COLUMN `setup_url` varchar(500)
    COLLATE utf8mb4_unicode_ci NOT NULL;

ALTER TABLE `release_info`
    DROP COLUMN `msix_url`,
    DROP COLUMN `zip_url`;
