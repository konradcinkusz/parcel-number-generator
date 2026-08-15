-- Creates the notification service's database on the shared local Postgres instance.
-- Compose mounts this into /docker-entrypoint-initdb.d/, so it runs once, on first boot
-- of an empty volume. One instance, two databases, each owned by one service (P3).
CREATE DATABASE notifications;
GRANT ALL PRIVILEGES ON DATABASE notifications TO parcelnumbers;
