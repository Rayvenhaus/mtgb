-- MTGB release_info reset seed.
-- Use when the live release_info table has become ceremonially toasted.
--
-- This keeps historical rows for reference, but only v0.6.2 is current.
-- Historical rows intentionally have blank setup_url values because those
-- releases were not setup-EXE releases. release_page_url is left blank
-- for them as well. Clients only read is_current = 1.
-- v0.6.2 is marked is_beta = 1 while keeping version numeric for update
-- comparison.
--
-- Run against the live mtgb_community database:
--   mysql -u <user> -p mtgb_community < 2026-05-10-reset-release-info.sql

START TRANSACTION;

UPDATE `release_info`
SET `is_current` = 0;

DELETE FROM `release_info`;

ALTER TABLE `release_info` AUTO_INCREMENT = 1;

INSERT INTO `release_info`
    (`version`, `release_date`, `setup_url`, `release_notes`,
     `is_beta`, `is_current`)
VALUES
    (
        '0.1.0',
        '2026-04-14 00:00:00',
        '',
        'The one where it exists. Repository created, MTGB named, origin story established, and the Ministry began keeping records.',
        0,
        0
    ),
    (
        '0.2.0',
        '2026-04-15 00:00:00',
        '',
        'The one where the scaffold goes up. Initial solution, app structure, WPF entry point, MIT licence, README, and project foundation.',
        0,
        0
    ),
    (
        '0.2.1',
        '2026-04-15 00:00:00',
        '',
        'The one where it has a face, an icon, and a story. Added app icon, THE_TRUTH.md, changelog, and versioning policy.',
        0,
        0
    ),
    (
        '0.2.2',
        '2026-04-16 00:00:00',
        '',
        'The one where secrets are kept properly. Added Windows Credential Manager storage and webhook secret handling.',
        0,
        0
    ),
    (
        '0.2.3',
        '2026-04-16 00:00:00',
        '',
        'The one where it knows what events mean. Added AppSettings, event definitions, notification settings, and default configuration.',
        0,
        0
    ),
    (
        '0.2.4',
        '2026-04-16 00:00:00',
        '',
        'The one where it talks to SimplyPrint. Added the typed SimplyPrint API client for printers, jobs, webhooks, and actions.',
        0,
        0
    ),
    (
        '0.2.5',
        '2026-04-16 00:00:00',
        '',
        'The one where it knows who you are. Added API-key authentication and OAuth2 PKCE groundwork.',
        0,
        0
    ),
    (
        '0.2.6',
        '2026-04-16 00:00:00',
        '',
        'The one where it understands what it is looking at. Added state diffing for printer and print-job events.',
        0,
        0
    ),
    (
        '0.2.7',
        '2026-04-16 00:00:00',
        '',
        'The one where it watches. Added polling and webhook background workers.',
        0,
        0
    ),
    (
        '0.2.8',
        '2026-04-16 00:00:00',
        '',
        'The one where it goes Bing with feeling. Added notification manager, history, toast actions, grouping, and flavour text.',
        0,
        0
    ),
    (
        '0.2.9',
        '2026-04-16 00:00:00',
        '',
        'The one where it gets a face. Added tray icon behaviour, flyout UI, status cards, and tray actions.',
        0,
        0
    ),
    (
        '0.2.10',
        '2026-04-17 00:00:00',
        '',
        'The one where it boots without crashing. Fixed WPF startup, STA threading, target framework, and project wiring.',
        0,
        0
    ),
    (
        '0.2.11',
        '2026-04-17 00:00:00',
        '',
        'The one where the flyout stops falling off the screen. Fixed flyout positioning and reopening behaviour.',
        0,
        0
    ),
    (
        '0.2.12',
        '2026-04-17 00:00:00',
        '',
        'The one where the right-click menu stops being funky. Added custom tray context menu theming and app manifest fixes.',
        0,
        0
    ),
    (
        '0.2.13',
        '2026-04-17 00:00:00',
        '',
        'The one where it actually connects. Added full Settings window, account connection, printer toggles, quiet hours, and About links.',
        0,
        0
    ),
    (
        '0.2.14',
        '2026-04-17 00:00:00',
        '',
        'The one where the settings window stops bleeding everywhere. Fixed Settings clipping and flyout hover churn.',
        0,
        0
    ),
    (
        '0.2.16',
        '2026-04-17 00:00:00',
        '',
        'The one where it actually goes Bing. Confirmed end-to-end toast delivery and notification history.',
        0,
        0
    ),
    (
        '0.2.17',
        '2026-04-17 00:00:00',
        '',
        'The one where you can actually read the flyout. Improved flyout readability, mute banner, and menu styling.',
        0,
        0
    ),
    (
        '0.2.18',
        '2026-04-17 00:00:00',
        '',
        'The one where history is made. And recorded. And filtered. Added notification history UI with filters and clear history.',
        0,
        0
    ),
    (
        '0.3.0',
        '2026-04-19 00:00:00',
        '',
        'The one where the Ministry opens its doors for the first time. Added first-run Induction and supporting UI flow.',
        0,
        0
    ),
    (
        '0.4.0',
        '2026-04-19 00:00:00',
        '',
        'The one where the scribes got their quills. Added community telemetry and server infrastructure.',
        0,
        0
    ),
    (
        '0.4.1',
        '2026-04-19 00:00:00',
        '',
        'The one where the Ministry learned to count. Added local stats view and telemetry/community-map status reporting.',
        0,
        0
    ),
    (
        '0.5.0',
        '2026-04-21 00:00:00',
        '',
        'The one where MTGB learned to pack its bags. Added MSIX packaging, tile assets, and portable distribution pipeline.',
        0,
        0
    ),
    (
        '0.5.1',
        '2026-04-21 00:00:00',
        '',
        'The one where MTGB learned to update itself. Added update worker, release endpoint support, and update notifications.',
        0,
        0
    ),
    (
        '0.5.2',
        '2026-04-22 00:00:00',
        '',
        'The one where the Ministry started keeping proper records. Added Induction summary and install ID generation.',
        0,
        0
    ),
    (
        '0.5.3',
        '2026-04-25 00:00:00',
        '',
        'The one where the Ministry got a makeover. Complete Navy and Gold UI rebrand, theme dictionary, and icon refresh.',
        0,
        0
    ),
    (
        '0.5.4',
        '2026-04-25 00:00:00',
        '',
        'The one where the Ministry fixed its own face. Fixed portable ZIP contents, button text colour, and single-file publish warnings.',
        0,
        0
    ),
    (
        '0.6.0',
        '2026-05-01 00:00:00',
        '',
        'The one where the Ministry decided to convert before the Inquisition. Began the move from MSIX to WiX/Burn setup packaging.',
        0,
        0
    ),
    (
        '0.6.1',
        '2026-05-10 00:00:00',
        '',
        'The one where the Ministry stopped wandering and found the exit. Fixed WiX/Burn release packaging, first-run Induction behaviour, startup sequencing, installer identity, uninstall cleanup, Settings and History window behaviour, release metadata, and Ministry-approved logging paperwork.',
        1,
        0
    ),
    (
        '0.6.2',
        '2026-05-10 00:00:00',
        'https://github.com/Rayvenhaus/mtgb/releases/download/v0.6.2-beta/MTGB-v0.6.2-beta-x64-Setup.exe',
        'The one where the Ministry stopped stamping the same form twice. Added single-instance protection, fixed log file naming, polished Induction summary and Settings readability, refined flyout actions, and added visible printer completion percentages.',
        1,
        1
    );

UPDATE `release_info`
SET `release_page_url` =
    'https://github.com/Rayvenhaus/mtgb/releases/tag/v0.6.2-beta'
WHERE `version` = '0.6.2'
  AND `is_current` = 1;

COMMIT;
