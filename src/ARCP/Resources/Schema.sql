-- ARCP event log schema. Embedded as an MSBuild EmbeddedResource and applied
-- at startup with CREATE TABLE IF NOT EXISTS. See RFC-0001-v2 §6.4 and §19.

CREATE TABLE IF NOT EXISTS envelopes (
    session_id      TEXT    NOT NULL,
    message_id      TEXT    NOT NULL,
    type            TEXT    NOT NULL,
    job_id          TEXT,
    stream_id       TEXT,
    subscription_id TEXT,
    trace_id        TEXT,
    correlation_id  TEXT,
    causation_id    TEXT,
    priority        TEXT,
    timestamp       TEXT    NOT NULL,
    sequence        INTEGER NOT NULL,
    body            TEXT    NOT NULL,
    PRIMARY KEY (session_id, message_id)
);

CREATE INDEX IF NOT EXISTS idx_envelopes_session_seq
    ON envelopes (session_id, sequence);

CREATE INDEX IF NOT EXISTS idx_envelopes_session_type
    ON envelopes (session_id, type);

CREATE TABLE IF NOT EXISTS idempotency (
    principal       TEXT    NOT NULL,
    idempotency_key TEXT    NOT NULL,
    session_id      TEXT    NOT NULL,
    message_id      TEXT    NOT NULL,
    created_at      TEXT    NOT NULL,
    PRIMARY KEY (principal, idempotency_key)
);

CREATE TABLE IF NOT EXISTS artifacts (
    artifact_id  TEXT NOT NULL PRIMARY KEY,
    session_id   TEXT NOT NULL,
    media_type   TEXT NOT NULL,
    size         INTEGER NOT NULL,
    sha256       TEXT,
    expires_at   TEXT,
    body         BLOB NOT NULL,
    created_at   TEXT NOT NULL
);
