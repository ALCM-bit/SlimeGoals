-- 007: nome da tarefa do cronômetro (specs/Tarefas_no_cronometro_v1.md). Local: o cronômetro não sincroniza.
ALTER TABLE active_timer ADD COLUMN task TEXT;
