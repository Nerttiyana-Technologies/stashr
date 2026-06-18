namespace Stashr.Storage.Postgres;

/// <summary>
/// The PostgreSQL schema (ADR-0007). Idempotent (<c>IF NOT EXISTS</c>) so it can run on every
/// startup; it grows as later layers add leases/mounts/approle tables. The store persists only
/// ciphertext, wrapped keys, metadata and audit — never plaintext or the master key.
/// </summary>
internal static class PostgresSchema
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS stashr_key_ring (
            version     INT PRIMARY KEY,
            nonce       BYTEA NOT NULL,
            ciphertext  BYTEA NOT NULL,
            tag         BYTEA NOT NULL,
            algorithm   TEXT NOT NULL,
            state       TEXT NOT NULL DEFAULT 'active',
            created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS stashr_seal_config (
            id          INT PRIMARY KEY DEFAULT 1,
            sealed_root BYTEA NOT NULL,
            CONSTRAINT stashr_seal_config_single CHECK (id = 1)
        );

        CREATE TABLE IF NOT EXISTS stashr_secret_meta (
            path            TEXT PRIMARY KEY,
            current_version INT NOT NULL,
            max_versions    INT NOT NULL DEFAULT 10,
            cas_required    BOOLEAN NOT NULL DEFAULT FALSE,
            created_at      TIMESTAMPTZ NOT NULL,
            updated_at      TIMESTAMPTZ NOT NULL
        );

        CREATE TABLE IF NOT EXISTS stashr_secret (
            path        TEXT NOT NULL,
            version     INT NOT NULL,
            nonce       BYTEA NOT NULL,
            ciphertext  BYTEA NOT NULL,
            tag         BYTEA NOT NULL,
            algorithm   TEXT NOT NULL,
            key_version INT NOT NULL,
            created_at  TIMESTAMPTZ NOT NULL,
            deleted_at  TIMESTAMPTZ,
            destroyed   BOOLEAN NOT NULL DEFAULT FALSE,
            PRIMARY KEY (path, version)
        );

        CREATE TABLE IF NOT EXISTS stashr_policy (
            name       TEXT PRIMARY KEY,
            policy_json JSONB NOT NULL,
            version    INT NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS stashr_token (
            accessor   TEXT PRIMARY KEY,
            token_json JSONB NOT NULL,
            expires_at TIMESTAMPTZ NOT NULL
        );

        CREATE TABLE IF NOT EXISTS stashr_audit (
            seq        BIGINT PRIMARY KEY,
            entry_json JSONB NOT NULL,
            chain_hash TEXT
        );

        CREATE TABLE IF NOT EXISTS stashr_blob (
            key   TEXT PRIMARY KEY,
            value BYTEA NOT NULL
        );
        """;
}
