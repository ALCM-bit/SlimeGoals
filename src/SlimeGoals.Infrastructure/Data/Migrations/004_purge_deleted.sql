-- 004: exclusão definitiva (spec §15, 06/11/2026). "Excluir" passou a apagar de verdade; aqui sai o que a exclusão
-- lógica deixou para trás: metas com deleted_at e tudo ligado a elas. Registros com deleted_at também saem (o app
-- nunca os gravou nem os lê: ajuste de progresso é valor negativo). As colunas deleted_at ficam no esquema, para não
-- recriar as tabelas. Notificação pendente dessas metas não é retirada aqui: a exclusão lógica já a retirava, e o
-- toque numa que tenha sobrado só encontra o log vazio. O MigrationRunner roda VACUUM depois deste script.

DELETE FROM active_timer WHERE goal_id IN (SELECT id FROM goals WHERE deleted_at IS NOT NULL);
DELETE FROM checkins WHERE deleted_at IS NOT NULL OR goal_id IN (SELECT id FROM goals WHERE deleted_at IS NOT NULL);
DELETE FROM reminder_log WHERE goal_id IN (SELECT id FROM goals WHERE deleted_at IS NOT NULL);
DELETE FROM goals WHERE deleted_at IS NOT NULL;
