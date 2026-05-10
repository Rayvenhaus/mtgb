-- MTGB release_info beta flag migration.
-- Adds an explicit prerelease marker while keeping version numeric
-- for client-side System.Version comparisons.

ALTER TABLE `release_info`
    ADD COLUMN `is_beta` tinyint(1) NOT NULL DEFAULT 0
    AFTER `release_notes`;

CREATE INDEX `idx_is_beta`
    ON `release_info` (`is_beta`);
