-- 002: remove a recorrência 'once' (tarefa única) e a coluna completed_at (spec §15, 01/11/2026).
-- Metas únicas são excluídas de vez, com seus check-ins e lembretes.
-- O SQLite não altera CHECK: a tabela é recriada. O MigrationRunner roda os scripts com
-- foreign_keys desligado e confere PRAGMA foreign_key_check antes do COMMIT.

DELETE FROM checkins WHERE goal_id IN (SELECT id FROM goals WHERE recurrence = 'once');
DELETE FROM reminder_log WHERE goal_id IN (SELECT id FROM goals WHERE recurrence = 'once');
DELETE FROM goals WHERE recurrence = 'once';

CREATE TABLE goals_new (
  id TEXT PRIMARY KEY,                -- GUID
  title TEXT NOT NULL,
  recurrence TEXT NOT NULL,           -- 'daily' | 'weekly'
  target INTEGER NOT NULL DEFAULT 1,  -- alvo por período
  icon TEXT NOT NULL,                 -- chave de um conjunto fixo de ícones do app
  color TEXT NOT NULL,                -- chave da paleta de metas (spec §11)
  reminder_every_min INTEGER NOT NULL,
  active_from TEXT NOT NULL,          -- 'HH:MM'
  active_until TEXT NOT NULL,
  active_days TEXT NOT NULL,          -- '1,2,3,4,5' (1 = segunda)
  start_date TEXT,                    -- 'YYYY-MM-DD' (data local), opcional
  end_date TEXT,                      -- 'YYYY-MM-DD' (data local), opcional
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  archived_at TEXT,
  deleted_at TEXT,                    -- exclusão lógica
  CHECK (recurrence IN ('daily','weekly')),
  CHECK (target >= 1)
);

INSERT INTO goals_new (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                       active_days, start_date, end_date, created_at, updated_at, archived_at, deleted_at)
SELECT id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
       active_days, start_date, end_date, created_at, updated_at, archived_at, deleted_at
FROM goals;

DROP TABLE goals;
ALTER TABLE goals_new RENAME TO goals;
