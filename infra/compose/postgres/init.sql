-- VibeChat application database bootstrap (runs once on first volume init).
-- Extensions used by the modular monolith (ids, hashing helpers, etc.).

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
