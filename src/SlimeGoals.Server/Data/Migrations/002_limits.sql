-- Segunda barreira: os tamanhos que as rotas já conferem, também no banco. Um defeito numa rota não grava lixo.
ALTER TABLE accounts
    ADD CONSTRAINT ck_accounts_email CHECK (char_length(email) BETWEEN 3 AND 254),
    ADD CONSTRAINT ck_accounts_kdf_salt CHECK (octet_length(kdf_salt) BETWEEN 16 AND 64),
    ADD CONSTRAINT ck_accounts_kdf_params CHECK (char_length(kdf_params) BETWEEN 1 AND 256),
    ADD CONSTRAINT ck_accounts_auth_hash CHECK (octet_length(auth_hash) = 32),
    ADD CONSTRAINT ck_accounts_recovery_hash CHECK (octet_length(recovery_hash) = 32),
    ADD CONSTRAINT ck_accounts_wrapped_key_password CHECK (octet_length(wrapped_key_password) BETWEEN 1 AND 256),
    ADD CONSTRAINT ck_accounts_wrapped_key_recovery CHECK (octet_length(wrapped_key_recovery) BETWEEN 1 AND 256),
    ADD CONSTRAINT ck_accounts_confirm_hash CHECK (confirm_hash IS NULL OR octet_length(confirm_hash) = 32);

ALTER TABLE devices
    ADD CONSTRAINT ck_devices_name CHECK (char_length(name) BETWEEN 1 AND 100),
    ADD CONSTRAINT ck_devices_token_hash CHECK (octet_length(token_hash) = 32);

ALTER TABLE records
    ADD CONSTRAINT ck_records_record_key CHECK (char_length(record_key) BETWEEN 1 AND 128);
