-- Papéis do banco do servidor: dono (só migrações) e app (só linhas). Roda uma vez, como
-- superusuário, conectado ao banco :"db_name", que já existe (criado pela imagem do PostgreSQL).
-- Uso: psql -v owner_role=... -v app_role=... -v db_name=... -v owner_password=... -v app_password=... -f roles.sql

CREATE ROLE :"owner_role" LOGIN PASSWORD :'owner_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;
CREATE ROLE :"app_role" LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

ALTER DATABASE :"db_name" OWNER TO :"owner_role";

-- Ninguém além dos dois papéis conecta, cria objetos ou cria tabelas temporárias.
REVOKE CONNECT, CREATE, TEMPORARY ON DATABASE :"db_name" FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO :"owner_role";

GRANT CONNECT ON DATABASE :"db_name" TO :"owner_role", :"app_role";
GRANT USAGE ON SCHEMA public TO :"app_role";

-- Tudo que o dono criar daqui em diante (as migrações) já nasce com o acesso mínimo do app.
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner_role" IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"owner_role" IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO :"app_role";
