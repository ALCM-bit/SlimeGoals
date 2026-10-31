-- 001: esquema inicial (spec §8, v1.3)
CREATE TABLE goals (
  id TEXT PRIMARY KEY,                -- GUID
  title TEXT NOT NULL,
  recurrence TEXT NOT NULL,           -- 'once' | 'daily' | 'weekly'
  target INTEGER NOT NULL DEFAULT 1,  -- alvo por período; sempre 1 em 'once'
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
  completed_at TEXT,                  -- só 'once': instante da conclusão
  archived_at TEXT,
  deleted_at TEXT,                    -- exclusão lógica
  CHECK (recurrence IN ('once','daily','weekly')),
  CHECK (target >= 1),
  CHECK (recurrence <> 'once' OR target = 1)
);

CREATE TABLE checkins (
  id TEXT PRIMARY KEY,                -- GUID
  goal_id TEXT NOT NULL REFERENCES goals(id),
  done_at TEXT NOT NULL,              -- ISO 8601 (UTC)
  amount INTEGER NOT NULL DEFAULT 1,
  note TEXT,
  updated_at TEXT NOT NULL,
  deleted_at TEXT
);

CREATE TABLE reminder_log (
  id TEXT PRIMARY KEY,                -- GUID
  goal_id TEXT NOT NULL REFERENCES goals(id),
  fired_at TEXT NOT NULL,
  response TEXT,                      -- 'done' | 'snooze' | 'ignore' | NULL
  responded_at TEXT
);

CREATE TABLE settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
CREATE INDEX idx_checkins_goal_date ON checkins(goal_id, done_at);
CREATE INDEX idx_reminder_log_goal ON reminder_log(goal_id, fired_at);
