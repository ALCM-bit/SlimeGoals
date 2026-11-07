CREATE TABLE accounts (
    id                   uuid PRIMARY KEY,
    email                text NOT NULL,
    email_confirmed_at   timestamptz,
    kdf_salt             bytea NOT NULL,
    kdf_params           text NOT NULL,
    auth_hash            bytea NOT NULL,
    recovery_hash        bytea NOT NULL,
    wrapped_key_password bytea NOT NULL,
    wrapped_key_recovery bytea NOT NULL,
    next_seq             bigint NOT NULL DEFAULT 1,
    purge_point          bigint NOT NULL DEFAULT 0,
    confirm_hash         bytea,
    confirm_expires_at   timestamptz,
    created_at           timestamptz NOT NULL
);

CREATE UNIQUE INDEX ux_accounts_email ON accounts (email);

CREATE TABLE devices (
    id           uuid PRIMARY KEY,
    account_id   uuid NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    name         text NOT NULL,
    token_hash   bytea NOT NULL,
    last_seen_at timestamptz NOT NULL,
    revoked_at   timestamptz
);

CREATE UNIQUE INDEX ux_devices_token_hash ON devices (token_hash);
CREATE INDEX ix_devices_account ON devices (account_id);

CREATE TABLE records (
    account_id uuid NOT NULL REFERENCES accounts (id) ON DELETE CASCADE,
    record_key text NOT NULL,
    seq        bigint NOT NULL,
    blob       bytea NULL,
    deleted_at timestamptz,
    PRIMARY KEY (account_id, record_key)
);

CREATE INDEX ix_records_account_seq ON records (account_id, seq);
CREATE INDEX ix_records_deleted_at ON records (deleted_at) WHERE deleted_at IS NOT NULL;
