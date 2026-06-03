-- Save Vault — least-privilege database user
-- ===========================================================================
-- The app currently connects as `Admin`, which almost certainly has full
-- privileges (DROP, ALTER, GRANT, FILE, ...). If the app is ever compromised
-- (e.g. a SQL-injection slips through), that account can destroy or exfiltrate
-- the whole server. This script creates a restricted account for the app.
--
-- ⚠️ SHARED HOSTING / cPanel (GoDaddy etc.): this SQL will NOT run — your MySQL
-- login is not a superuser, so CREATE USER/GRANT return "#1227 Access denied".
-- Instead use cPanel → "MySQL Databases":
--   1. "Add New User" (cPanel prefixes the name, e.g. cpuser_svapp) + strong pass.
--   2. "Add User To Database": pick that user + the `vault` database.
--   3. On the privileges page check ONLY: SELECT, INSERT, UPDATE, DELETE, CREATE
--      (NOT "ALL PRIVILEGES" / DROP / ALTER / GRANT). Then update db.php &
--      auth_config.php (and deploy.config.ps1) with the new user + password.
--
-- HOW TO USE on a server where you DO have admin (raw SQL / self-managed MySQL):
--   1. Edit the password below to a strong, unique value.
--   2. Run this whole script.
--   3. Point the app at it by changing the credentials in BOTH:
--        - Server/auth_config.php  (DB_USER / DB_PASS)
--        - Server/db.php           (DB_USER / DB_PASSWORD)
--      to 'savevault_app' and the password you set here.
--   4. Verify the site still works, then consider removing/disabling the old
--      'Admin' account from the application config.
-- ===========================================================================

CREATE USER IF NOT EXISTS 'savevault_app'@'localhost'
    IDENTIFIED BY 'CHANGE_ME_to_a_strong_unique_password';

-- Day-to-day data access only. Note what is intentionally NOT granted:
-- no DROP, ALTER, GRANT, CREATE USER, FILE (blocks LOAD_FILE / INTO OUTFILE,
-- i.e. reading/writing server files via SQL), SUPER, PROCESS, or SHUTDOWN.
GRANT SELECT, INSERT, UPDATE, DELETE ON `vault`.* TO 'savevault_app'@'localhost';

-- The app creates a few tables on demand at runtime (notifications,
-- user_notifications, system_settings) via "CREATE TABLE IF NOT EXISTS".
-- CREATE is required for those statements to run even when the table already
-- exists. Two options:
--   (A) Keep this grant (simplest — still far safer than full admin):
GRANT CREATE ON `vault`.* TO 'savevault_app'@'localhost';
--   (B) MORE LOCKED DOWN: run Server/sql.sql once as admin to pre-create every
--       table, then DELETE the GRANT CREATE line above so the app has zero DDL.
--       (Schema changes/migrations are then always done manually as admin.)

FLUSH PRIVILEGES;

-- To confirm what the new user can do:
--   SHOW GRANTS FOR 'savevault_app'@'localhost';
