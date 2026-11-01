-- 003: medida por meta (spec §5.2, v1.4) e cronômetro único.
-- Metas existentes contavam check-ins: viram 'quantity' na unidade 'vezes', sem mudar de comportamento.
-- O SQLite não acrescenta CHECK entre colunas via ALTER TABLE: goals é recriada, como na 002
-- (o MigrationRunner roda com foreign_keys desligado e confere foreign_key_check antes do COMMIT).

CREATE TABLE goals_new (
  id TEXT PRIMARY KEY,                -- GUID
  title TEXT NOT NULL,
  recurrence TEXT NOT NULL,           -- 'daily' | 'weekly'
  target INTEGER NOT NULL DEFAULT 1,  -- alvo por período, na unidade base da medida
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
  measure TEXT NOT NULL DEFAULT 'quantity', -- 'completion' | 'quantity' | 'duration'
  unit TEXT,                          -- só 'quantity': unidade livre, até 20 caracteres
  CHECK (recurrence IN ('daily','weekly')),
  CHECK (target >= 1),
  CHECK (measure IN ('completion','quantity','duration')),
  CHECK (measure <> 'completion' OR target = 1),
  CHECK ((measure = 'quantity') = (unit IS NOT NULL))
);

INSERT INTO goals_new (id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
                       active_days, start_date, end_date, created_at, updated_at, archived_at, deleted_at,
                       measure, unit)
SELECT id, title, recurrence, target, icon, color, reminder_every_min, active_from, active_until,
       active_days, start_date, end_date, created_at, updated_at, archived_at, deleted_at,
       'quantity', 'vezes'
FROM goals;

DROP TABLE goals;
ALTER TABLE goals_new RENAME TO goals;

CREATE TABLE active_timer (            -- no máximo uma linha
  id INTEGER PRIMARY KEY CHECK (id = 1),
  goal_id TEXT NOT NULL REFERENCES goals(id),
  started_at TEXT NOT NULL,           -- ISO 8601 (UTC)
  stopped_at TEXT                     -- preenchido = parado, aguardando registrar ou descartar
);
