namespace FileFlow.Infrastructure.Database;

/// <summary>A single, versioned schema migration.</summary>
public sealed record Migration(int Version, string Description, IReadOnlyList<string> Statements);

/// <summary>
/// Ordered schema migrations applied on startup. Idempotent: the stored schema version
/// is compared against the migration list and only missing versions are applied,
/// each inside its own transaction.
/// </summary>
public static class Migrations
{
    public static readonly IReadOnlyList<Migration> All =
    [
        new Migration(
            1,
            "Initial schema: files, tags, file_tags, tasks, task_items, operation_history, settings",
        [
            """
            CREATE TABLE IF NOT EXISTS files (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                full_path    TEXT    NOT NULL UNIQUE,
                file_name    TEXT    NOT NULL,
                extension    TEXT    NOT NULL DEFAULT '',
                size_bytes   INTEGER NOT NULL DEFAULT 0,
                created_at   TEXT    NOT NULL,
                modified_at  TEXT    NOT NULL,
                scanned_at   TEXT    NOT NULL,
                content_hash TEXT,
                is_missing   INTEGER NOT NULL DEFAULT 0
            )
            """,
            "CREATE INDEX IF NOT EXISTS idx_files_name ON files(file_name)",
            "CREATE INDEX IF NOT EXISTS idx_files_extension ON files(extension)",
            "CREATE INDEX IF NOT EXISTS idx_files_modified ON files(modified_at)",
            "CREATE INDEX IF NOT EXISTS idx_files_missing ON files(is_missing)",
            """
            CREATE TABLE IF NOT EXISTS tags (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                name       TEXT    NOT NULL UNIQUE COLLATE NOCASE,
                color      TEXT,
                created_at TEXT    NOT NULL
            )
            """,
            """
            CREATE TABLE IF NOT EXISTS file_tags (
                file_id INTEGER NOT NULL REFERENCES files(id) ON DELETE CASCADE,
                tag_id  INTEGER NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
                PRIMARY KEY (file_id, tag_id)
            )
            """,
            "CREATE INDEX IF NOT EXISTS idx_file_tags_tag ON file_tags(tag_id)",
            """
            CREATE TABLE IF NOT EXISTS tasks (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                type         TEXT    NOT NULL,
                status       TEXT    NOT NULL,
                created_at   TEXT    NOT NULL,
                started_at   TEXT,
                completed_at TEXT,
                total        INTEGER NOT NULL DEFAULT 0,
                processed    INTEGER NOT NULL DEFAULT 0,
                succeeded    INTEGER NOT NULL DEFAULT 0,
                failed       INTEGER NOT NULL DEFAULT 0,
                error_count  INTEGER NOT NULL DEFAULT 0,
                description  TEXT,
                metadata     TEXT
            )
            """,
            "CREATE INDEX IF NOT EXISTS idx_tasks_status ON tasks(status)",
            """
            CREATE TABLE IF NOT EXISTS task_items (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id       INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
                source_path   TEXT    NOT NULL,
                target_path   TEXT,
                status        TEXT    NOT NULL,
                error_kind    TEXT    NOT NULL DEFAULT 'None',
                error_message TEXT,
                processed_at  TEXT
            )
            """,
            "CREATE INDEX IF NOT EXISTS idx_task_items_task ON task_items(task_id)",
            "CREATE INDEX IF NOT EXISTS idx_task_items_status ON task_items(status)",
            """
            CREATE TABLE IF NOT EXISTS operation_history (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                task_id       INTEGER,
                operation_type TEXT   NOT NULL,
                description   TEXT,
                item_count    INTEGER NOT NULL DEFAULT 0,
                success_count INTEGER NOT NULL DEFAULT 0,
                failure_count INTEGER NOT NULL DEFAULT 0,
                metadata      TEXT,
                created_at    TEXT    NOT NULL
            )
            """,
            "CREATE INDEX IF NOT EXISTS idx_history_type ON operation_history(operation_type)",
            "CREATE INDEX IF NOT EXISTS idx_history_created ON operation_history(created_at)",
            """
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )
            """,
            """
            CREATE TABLE IF NOT EXISTS schema_version (
                version    INTEGER NOT NULL,
                applied_at TEXT NOT NULL
            )
            """,
        ]),
    ];
}
