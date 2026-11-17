-- 008: sessão de Pomodoro (specs/Pomodoro). Local: nada daqui sincroniza; os registros entram pela tabela checkins.
CREATE TABLE focus_session (           -- no máximo uma linha
  id INTEGER PRIMARY KEY CHECK (id = 1),
  plan TEXT NOT NULL,                  -- JSON de FocusPlan.ToJson
  goal_id TEXT,                        -- GUID; sem chave estrangeira: a meta pode sumir e o serviço trata isso
  started_at TEXT NOT NULL,            -- ISO 8601 (UTC)
  step INTEGER NOT NULL,
  block_started_at TEXT NOT NULL,
  cut_at TEXT NOT NULL,
  carry_ms INTEGER NOT NULL,           -- resto de minuto do último corte
  paused_at TEXT,                      -- preenchido = relógio congelado
  task TEXT,
  finished_at TEXT,                    -- preenchido = sessão terminada à espera de um destino para o tempo
  alerted_step INTEGER,                -- passo cujo aviso de fim já foi dado
  revision INTEGER NOT NULL
);

CREATE TABLE focus_chunk (
  id TEXT PRIMARY KEY,                 -- GUID
  minutes INTEGER NOT NULL,
  task TEXT,
  ended_at TEXT NOT NULL,              -- ISO 8601 (UTC)
  checkin_id TEXT                      -- registro que recebeu o trecho; sem chave estrangeira, como goal_id
);
