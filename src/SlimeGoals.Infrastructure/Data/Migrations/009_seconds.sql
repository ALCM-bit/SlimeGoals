-- 009: metas de tempo passam de minutos para segundos inteiros (specs/Segundos_nas_metas_de_tempo_v1.md §4.4).
-- UPDATEs simples: updated_at não muda e nada entra na fila de sincronização (não há gatilhos).
-- Meta de quantidade e de conclusão não mudam. Todo registro de meta de tempo converte, apagado ou de compensação.
UPDATE goals SET target = target * 60 WHERE measure = 'duration';
UPDATE checkins SET amount = amount * 60
WHERE goal_id IN (SELECT id FROM goals WHERE measure = 'duration');

-- Trechos de foco guardados na sessão local de Pomodoro.
ALTER TABLE focus_chunk RENAME COLUMN minutes TO seconds;
UPDATE focus_chunk SET seconds = seconds * 60;

-- Formato do payload guardado: 1 = minutos (convertido ao aplicar), 2 = segundos.
ALTER TABLE sync_pending ADD COLUMN format_version INTEGER NOT NULL DEFAULT 1;
