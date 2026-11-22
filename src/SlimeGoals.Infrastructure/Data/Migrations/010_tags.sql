-- 010: tags nas metas (specs/Tags_v1.md §4.2 e §4.3).
-- Sem chave estrangeira em goal_tags: a ligação pode chegar antes da tag e antes da meta.
CREATE TABLE tags (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL UNIQUE,
  color TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  deleted_at TEXT
);

CREATE TABLE goal_tags (
  id TEXT PRIMARY KEY,
  goal_id TEXT NOT NULL,
  tag_name TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  removed_at TEXT,
  UNIQUE (goal_id, tag_name)
);
CREATE INDEX idx_goal_tags_goal ON goal_tags(goal_id);

-- Tags padrão: ids derivados do nome (TagIds.ForTag) e data de 1970, para qualquer edição vencer.
-- Fora da fila de sincronização de propósito.
INSERT INTO tags (id, name, color, updated_at) VALUES
  ('e6f93147-2cb6-5a0e-98be-9a29644d12ec', 'medicamentos', 'green', '1970-01-01T00:00:00.0000000+00:00'),
  ('cb7b580b-eb62-5496-863c-7a1a3b6741e2', 'estudos', 'purple', '1970-01-01T00:00:00.0000000+00:00'),
  ('6a6bce84-3d58-5cb9-8e5e-09b43cba54a0', 'tarefas', 'amber', '1970-01-01T00:00:00.0000000+00:00');

-- O SQLite não altera um CHECK: sync_outbox e sync_records são recriadas aceitando 'tag' e 'goal_tag'.
CREATE TABLE sync_outbox_new (
  position INTEGER PRIMARY KEY AUTOINCREMENT,  -- ordem da primeira entrada
  record_id TEXT NOT NULL UNIQUE,
  kind TEXT NOT NULL CHECK (kind IN ('goal', 'checkin', 'tag', 'goal_tag')),
  op TEXT NOT NULL CHECK (op IN ('upsert', 'delete')),
  base_seq INTEGER NOT NULL DEFAULT 0,
  revision INTEGER NOT NULL DEFAULT 1,         -- muda a cada substituição
  record_key TEXT                              -- guardada antes do envio: o aceite pode se perder
);
INSERT INTO sync_outbox_new (position, record_id, kind, op, base_seq, revision, record_key)
SELECT position, record_id, kind, op, base_seq, revision, record_key FROM sync_outbox;
-- Mantém o contador do AUTOINCREMENT, inclusive o de posições já usadas e removidas.
DELETE FROM sqlite_sequence WHERE name = 'sync_outbox_new';
INSERT INTO sqlite_sequence (name, seq) SELECT 'sync_outbox_new', seq FROM sqlite_sequence WHERE name = 'sync_outbox';
DROP TABLE sync_outbox;
ALTER TABLE sync_outbox_new RENAME TO sync_outbox;
CREATE INDEX idx_sync_outbox_key ON sync_outbox(record_key);

CREATE TABLE sync_records_new (
  record_id TEXT PRIMARY KEY,
  kind TEXT NOT NULL CHECK (kind IN ('goal', 'checkin', 'tag', 'goal_tag')),
  seq INTEGER NOT NULL,
  record_key TEXT NOT NULL DEFAULT '',
  confirmed INTEGER NOT NULL DEFAULT 1 CHECK (confirmed IN (0, 1))
);
INSERT INTO sync_records_new (record_id, kind, seq, record_key, confirmed)
SELECT record_id, kind, seq, record_key, confirmed FROM sync_records;
DROP TABLE sync_records;
ALTER TABLE sync_records_new RENAME TO sync_records;
CREATE INDEX idx_sync_records_key ON sync_records(record_key);
