-- 005: tabelas da sincronização entre dispositivos (spec da sincronização, Fase 9). Nasce desligada e com a fila vazia.
CREATE TABLE sync_state (
  id INTEGER PRIMARY KEY CHECK (id = 1),
  enabled INTEGER NOT NULL DEFAULT 0 CHECK (enabled IN (0, 1)),
  device_id TEXT,
  last_seq INTEGER NOT NULL DEFAULT 0,
  last_sync_at TEXT,
  last_error TEXT
);
INSERT INTO sync_state (id) VALUES (1);

CREATE TABLE sync_outbox (
  position INTEGER PRIMARY KEY AUTOINCREMENT,  -- ordem da primeira entrada
  record_id TEXT NOT NULL UNIQUE,
  kind TEXT NOT NULL CHECK (kind IN ('goal', 'checkin')),
  op TEXT NOT NULL CHECK (op IN ('upsert', 'delete')),
  base_seq INTEGER NOT NULL DEFAULT 0,
  revision INTEGER NOT NULL DEFAULT 1          -- muda a cada substituição
);

CREATE TABLE sync_records (
  record_id TEXT PRIMARY KEY,
  kind TEXT NOT NULL CHECK (kind IN ('goal', 'checkin')),
  seq INTEGER NOT NULL
);

CREATE TABLE sync_tombstones (goal_id TEXT PRIMARY KEY);

CREATE TABLE sync_pending (
  record_id TEXT PRIMARY KEY,
  goal_id TEXT NOT NULL,                       -- sem chave estrangeira: a meta ainda não chegou
  seq INTEGER NOT NULL,
  payload TEXT NOT NULL
);
CREATE INDEX idx_sync_pending_goal ON sync_pending(goal_id);
