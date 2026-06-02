-- Save Vault — database schema (database: `vault`)
--
-- This script is idempotent: it can be run repeatedly and against an already
-- populated database without failing. Every table uses CREATE TABLE IF NOT
-- EXISTS, and the "Migrations" section at the bottom uses
-- ALTER TABLE ... ADD COLUMN IF NOT EXISTS so that columns missing on an older
-- database get added, while installs that already have them are skipped.
--
-- NOTE: `ADD COLUMN IF NOT EXISTS` requires MariaDB 10.x (the server this runs
-- on). On Oracle MySQL those ALTER statements raise an error when the column
-- already exists; that error is harmless and can be ignored.

-- ===========================================================================
-- Core: users, settings, login history, cloud save data
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `users` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `username` VARCHAR(50) NOT NULL,
  `email` VARCHAR(100) NOT NULL,
  `password` VARCHAR(255) NOT NULL,
  `registration_ip` VARCHAR(45) DEFAULT NULL,
  `last_login_ip` VARCHAR(45) DEFAULT NULL,
  `profile_photo` VARCHAR(255) DEFAULT NULL,
  `is_admin` TINYINT(1) DEFAULT 0,
  `is_active` TINYINT(1) DEFAULT 1,
  `reset_token` VARCHAR(64) DEFAULT NULL,
  `reset_token_expires` DATETIME DEFAULT NULL,
  `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `last_login` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unique_email` (`email`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `user_settings` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id` INT UNSIGNED NOT NULL,
  `auto_sync` TINYINT(1) DEFAULT 1,
  `sync_interval` INT DEFAULT 60,
  `dark_mode` TINYINT(1) DEFAULT 1,
  `reminder_enabled` TINYINT(1) DEFAULT 1,
  `email_notifications` TINYINT(1) DEFAULT 1,
  `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unique_user_id` (`user_id`),
  FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `login_history` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id` INT UNSIGNED NOT NULL,
  `login_time` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `ip_address` VARCHAR(45),
  `browser` VARCHAR(255),
  `os` VARCHAR(100),
  `device_type` VARCHAR(50),
  `country` VARCHAR(100),
  `city` VARCHAR(100),
  PRIMARY KEY (`id`),
  FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Per-user cloud save payload (one JSON row per user, upserted by the API).
CREATE TABLE IF NOT EXISTS `user_data` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `user_id` INT UNSIGNED NOT NULL,
  `data` LONGTEXT,
  `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unique_user_data_user` (`user_id`),
  FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===========================================================================
-- Notifications (no foreign keys — created the same way at runtime)
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `notifications` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `message` VARCHAR(128) NOT NULL,
  `link` VARCHAR(255) DEFAULT NULL,
  `type` ENUM('info', 'warning', 'update') DEFAULT 'info',
  `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `created_by` INT UNSIGNED NOT NULL,
  `target_type` ENUM('all', 'user', 'admin') DEFAULT 'all',
  `expires_at` DATETIME DEFAULT NULL,
  `priority` TINYINT(1) DEFAULT 0,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Tracks which users have been delivered / have read each notification.
CREATE TABLE IF NOT EXISTS `user_notifications` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `notification_id` INT UNSIGNED NOT NULL,
  `user_id` INT UNSIGNED NOT NULL,
  `is_read` TINYINT(1) DEFAULT 0,
  `delivered_at` DATETIME DEFAULT NULL,
  `read_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unique_notification_user` (`notification_id`, `user_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===========================================================================
-- Extensions & system settings
-- ===========================================================================

CREATE TABLE IF NOT EXISTS `extensions` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `extension_id` VARCHAR(100) NOT NULL,
  `name` VARCHAR(255) NOT NULL,
  `description` TEXT,
  `version` VARCHAR(20) NOT NULL,
  `author` VARCHAR(100) NOT NULL,
  `category` ENUM('Official', 'Fixes', 'Localization', 'Theming', 'Other') DEFAULT 'Other',
  `github_url` VARCHAR(255) NOT NULL,
  `download_count` INT UNSIGNED DEFAULT 0,
  `rating` DECIMAL(3,2) DEFAULT 0.00,
  `icon_url` VARCHAR(255) DEFAULT NULL,
  `is_approved` TINYINT(1) DEFAULT 0,
  `is_official` TINYINT(1) DEFAULT 0,
  `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  `last_github_check` DATETIME DEFAULT NULL,
  `github_updated_at` DATETIME DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unique_extension_id` (`extension_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `system_settings` (
  `id` INT UNSIGNED NOT NULL AUTO_INCREMENT,
  `key_name` VARCHAR(100) NOT NULL,
  `value` TEXT,
  `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP,
  `updated_at` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `unique_key_name` (`key_name`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===========================================================================
-- Migrations — patch databases created before a column existed.
-- Each is a safe no-op when the column is already present (see MariaDB note
-- at the top of this file).
-- ===========================================================================

ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `profile_photo` VARCHAR(255) DEFAULT NULL;
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `is_admin` TINYINT(1) DEFAULT 0;
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `reset_token` VARCHAR(64) DEFAULT NULL;
ALTER TABLE `users` ADD COLUMN IF NOT EXISTS `reset_token_expires` DATETIME DEFAULT NULL;

ALTER TABLE `user_settings` ADD COLUMN IF NOT EXISTS `reminder_enabled` TINYINT(1) DEFAULT 1;
ALTER TABLE `user_settings` ADD COLUMN IF NOT EXISTS `email_notifications` TINYINT(1) DEFAULT 1;

ALTER TABLE `notifications` ADD COLUMN IF NOT EXISTS `expires_at` DATETIME DEFAULT NULL;
ALTER TABLE `notifications` ADD COLUMN IF NOT EXISTS `priority` TINYINT(1) DEFAULT 0;

-- Extensions table: deployments created before these columns existed return
-- "SQLSTATE[42S22]: Unknown column 'github_url'" (and similar) from extensions_api.php.
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `description` TEXT DEFAULT NULL;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `version` VARCHAR(20) NOT NULL DEFAULT '1.0.0';
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `author` VARCHAR(100) NOT NULL DEFAULT 'Unknown';
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `category` ENUM('Official','Fixes','Localization','Theming','Other') DEFAULT 'Other';
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `github_url` VARCHAR(255) NOT NULL DEFAULT '';
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `download_count` INT UNSIGNED DEFAULT 0;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `rating` DECIMAL(3,2) DEFAULT 0.00;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `icon_url` VARCHAR(255) DEFAULT NULL;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `is_approved` TINYINT(1) DEFAULT 0;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `is_official` TINYINT(1) DEFAULT 0;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `created_at` DATETIME DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `updated_at` DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `last_github_check` DATETIME DEFAULT NULL;
ALTER TABLE `extensions` ADD COLUMN IF NOT EXISTS `github_updated_at` DATETIME DEFAULT NULL;

-- Ensure the category ENUM accepts 'Official' on tables created before it was added.
ALTER TABLE `extensions` MODIFY COLUMN `category` ENUM('Official','Fixes','Localization','Theming','Other') DEFAULT 'Other';
