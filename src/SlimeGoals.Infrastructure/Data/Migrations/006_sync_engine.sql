-- 006: dados do motor de sincronização. sync_records está vazia em toda instalação até a Fase 9.5.
ALTER TABLE checkins ADD COLUMN compensation_period TEXT;   -- yyyy-MM-dd, início do período
ALTER TABLE checkins ADD COLUMN compensation_seen TEXT;     -- ids em JSON
ALTER TABLE checkins ADD COLUMN superseded INTEGER NOT NULL DEFAULT 0 CHECK (superseded IN (0, 1));
CREATE INDEX idx_checkins_compensation ON checkins(goal_id, compensation_period)
  WHERE compensation_period IS NOT NULL;

ALTER TABLE sync_records ADD COLUMN record_key TEXT NOT NULL DEFAULT '';
ALTER TABLE sync_records ADD COLUMN confirmed INTEGER NOT NULL DEFAULT 1 CHECK (confirmed IN (0, 1));
CREATE INDEX idx_sync_records_key ON sync_records(record_key);

ALTER TABLE sync_outbox ADD COLUMN record_key TEXT;         -- guardada antes do envio: o aceite pode se perder

ALTER TABLE sync_state ADD COLUMN full_resync INTEGER NOT NULL DEFAULT 0 CHECK (full_resync IN (0, 1));
