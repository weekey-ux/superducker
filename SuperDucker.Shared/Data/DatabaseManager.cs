using Microsoft.Data.Sqlite;
using SuperDucker.Shared.Models;

namespace SuperDucker.Shared.Data;

public class DatabaseManager : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>
    /// 在指定路径打开（或创建）SQLite 数据库。
    /// </summary>
    public DatabaseManager(string dbPath)
    {
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();

        // 启用 WAL 模式与忙等待超时，以支持 sd.exe 与 superducker.exe 并发访问
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // 若数据库被其它进程锁定或存在热日志，WAL 可能失败，
            // 则回退到默认日志模式——程序在单用户模式下仍可正常工作。
        }

        InitializeSchema();
    }

    /// <summary>
    /// 解析 SuperDucker 的根目录（基于应用程序基目录）。
    /// 同时兼容开发态（dotnet sd.dll）与发布态（单文件 exe）两种运行方式。
    /// </summary>
    public static string GetRootDirectory()
    {
        // AppContext.BaseDirectory always points to the app's output directory:
        // - Dev mode: src/SuperDucker.Cli/bin/Debug/net8.0-windows/
        // - Published single-file: the directory containing the exe
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // For published single-file, ProcessPath is the exe itself (e.g. sd.exe)
        // and its directory == AppContext.BaseDirectory, so we're fine.
        // For `dotnet sd.dll`, ProcessPath is dotnet.exe (wrong), so we rely on baseDir.
        var exePath = Environment.ProcessPath;
        if (exePath != null)
        {
            var exeName = Path.GetFileName(exePath);
            // If ProcessPath is NOT a dotnet host, prefer it
            if (!exeName.StartsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(exePath) ?? baseDir;
            }
        }

        return baseDir;
    }

    /// <summary>
    /// 获取 link/ 目录路径（快捷方式存储目录）。
    /// </summary>
    public static string GetLinkDirectory()
    {
        return Path.Combine(GetRootDirectory(), "link");
    }

    /// <summary>
    /// 获取 app/ 目录路径（应用图标缓存等）。
    /// </summary>
    public static string GetAppDirectory()
    {
        return Path.Combine(GetRootDirectory(), "app");
    }

    /// <summary>
    /// 获取 data.db 数据库文件的默认路径。
    /// </summary>
    public static string GetDefaultDbPath()
    {
        return Path.Combine(GetRootDirectory(), "data.db");
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS app_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                abbreviation TEXT NOT NULL UNIQUE COLLATE NOCASE,
                friendly_name TEXT,
                target_path TEXT NOT NULL,
                working_directory TEXT,
                description TEXT,
                icon_path TEXT,
                category TEXT,
                is_built_in INTEGER NOT NULL DEFAULT 1,
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS url_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                abbreviation TEXT NOT NULL UNIQUE COLLATE NOCASE,
                friendly_name TEXT,
                url TEXT NOT NULL,
                description TEXT,
                category TEXT,
                icon_path TEXT,
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS tabs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT
            );
        ";
        cmd.ExecuteNonQuery();

        // Migration: add tab_id column if not exists
        MigrateAddColumn("app_entries", "tab_id", "INTEGER REFERENCES tabs(id) ON DELETE SET NULL");
        MigrateAddColumn("url_entries", "tab_id", "INTEGER REFERENCES tabs(id) ON DELETE SET NULL");
        MigrateAddColumn("url_entries", "icon_path", "TEXT");
        MigrateAddColumn("app_entries", "is_uninstalled", "INTEGER NOT NULL DEFAULT 0");
    }

    private void MigrateAddColumn(string table, string column, string type)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            using var alter = _connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            alter.ExecuteNonQuery();
        }
    }

    // ═══════════════════════════════════════════
    //  App Entries
    // ═══════════════════════════════════════════

    public List<AppEntry> GetAllApps(bool includeUninstalled = false)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = includeUninstalled
            ? "SELECT * FROM app_entries ORDER BY sort_order, abbreviation"
            : "SELECT * FROM app_entries WHERE is_uninstalled = 0 ORDER BY sort_order, abbreviation";
        return ReadApps(cmd);
    }

    public List<AppEntry> GetAppsByCategory(string category, bool includeUninstalled = false)
    {
        using var cmd = _connection.CreateCommand();
        var sql = "SELECT * FROM app_entries WHERE category = @cat";
        if (!includeUninstalled)
            sql += " AND is_uninstalled = 0";
        cmd.CommandText = sql + " ORDER BY sort_order, abbreviation";
        cmd.Parameters.AddWithValue("@cat", category);
        return ReadApps(cmd);
    }

    public AppEntry? GetAppByAbbreviation(string abbreviation)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM app_entries WHERE abbreviation = @abbr";
        cmd.Parameters.AddWithValue("@abbr", abbreviation.ToUpperInvariant());
        return ReadApps(cmd).FirstOrDefault();
    }

    public AppEntry? GetAppById(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM app_entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        return ReadApps(cmd).FirstOrDefault();
    }

    public bool AbbreviationExists(string abbreviation, int? excludeId = null)
    {
        using var cmd = _connection.CreateCommand();
        var sql = "SELECT COUNT(*) FROM app_entries WHERE abbreviation = @abbr";
        if (excludeId.HasValue)
            sql += " AND id != @id";
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@abbr", abbreviation.ToUpperInvariant());
        if (excludeId.HasValue)
            cmd.Parameters.AddWithValue("@id", excludeId.Value);
        // Also check url_entries for cross-table uniqueness
        var appCount = Convert.ToInt64(cmd.ExecuteScalar());

        using var cmd2 = _connection.CreateCommand();
        var sql2 = "SELECT COUNT(*) FROM url_entries WHERE abbreviation = @abbr";
        if (excludeId.HasValue)
            sql2 += " AND id != @id";
        cmd2.CommandText = sql2;
        cmd2.Parameters.AddWithValue("@abbr", abbreviation.ToUpperInvariant());
        if (excludeId.HasValue)
            cmd2.Parameters.AddWithValue("@id", excludeId.Value);
        var urlCount = Convert.ToInt64(cmd2.ExecuteScalar());

        return (appCount + urlCount) > 0;
    }

    /// <summary>
    /// Returns a human-readable description of which entry occupies the given abbreviation,
    /// or null if the abbreviation is free.
    /// </summary>
    public string? FindAbbreviationConflict(string abbreviation)
    {
        var abbr = abbreviation.ToUpperInvariant();

        // Check app_entries
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT a.friendly_name, a.abbreviation, t.name AS tab_name
                FROM app_entries a
                LEFT JOIN tabs t ON a.tab_id = t.id
                WHERE a.abbreviation = @abbr";
            cmd.Parameters.AddWithValue("@abbr", abbr);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var fn = reader.IsDBNull(0) ? null : reader.GetString(0);
                var a = reader.GetString(1);
                var tab = reader.IsDBNull(2) ? "全部" : reader.GetString(2);
                var label = fn ?? a;
                return $"程序「{label}」（Tab: {tab}）";
            }
        }

        // Check url_entries
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT u.friendly_name, u.abbreviation, t.name AS tab_name
                FROM url_entries u
                LEFT JOIN tabs t ON u.tab_id = t.id
                WHERE u.abbreviation = @abbr";
            cmd.Parameters.AddWithValue("@abbr", abbr);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                var fn = reader.IsDBNull(0) ? null : reader.GetString(0);
                var a = reader.GetString(1);
                var tab = reader.IsDBNull(2) ? "全部" : reader.GetString(2);
                var label = fn ?? a;
                return $"网址「{label}」（Tab: {tab}）";
            }
        }

        return null;
    }

    public AppEntry AddApp(AppEntry entry)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO app_entries (abbreviation, friendly_name, target_path, working_directory,
                description, icon_path, category, is_built_in, sort_order, tab_id, is_uninstalled)
            VALUES (@abbr, @fn, @tp, @wd, @desc, @icon, @cat, @bi, @so, @tab_id, @un);
            SELECT last_insert_rowid();
        ";
        BindAppParams(cmd, entry);
        entry.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return entry;
    }

    public void UpdateApp(AppEntry entry)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE app_entries SET
                abbreviation = @abbr, friendly_name = @fn, target_path = @tp,
                working_directory = @wd, description = @desc, icon_path = @icon,
                category = @cat, is_built_in = @bi, sort_order = @so, tab_id = @tab_id,
                is_uninstalled = @un
            WHERE id = @id
        ";
        BindAppParams(cmd, entry);
        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteApp(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM app_entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetAppUninstalled(int id, bool uninstalled)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE app_entries SET is_uninstalled = @un WHERE id = @id";
        cmd.Parameters.AddWithValue("@un", uninstalled ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void BindAppParams(SqliteCommand cmd, AppEntry entry)
    {
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@abbr", entry.Abbreviation.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@fn", (object?)entry.FriendlyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@tp", entry.TargetPath);
        cmd.Parameters.AddWithValue("@wd", (object?)entry.WorkingDirectory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@desc", (object?)entry.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@icon", (object?)entry.IconPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat", (object?)entry.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bi", entry.IsBuiltIn ? 1 : 0);
        cmd.Parameters.AddWithValue("@so", entry.SortOrder);
        cmd.Parameters.AddWithValue("@tab_id", (object?)entry.TabId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@un", entry.IsUninstalled ? 1 : 0);
    }

    private static List<AppEntry> ReadApps(SqliteCommand cmd)
    {
        var list = new List<AppEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AppEntry
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Abbreviation = reader.GetString(reader.GetOrdinal("abbreviation")),
                FriendlyName = reader.IsDBNull(reader.GetOrdinal("friendly_name")) ? null : reader.GetString(reader.GetOrdinal("friendly_name")),
                TargetPath = reader.GetString(reader.GetOrdinal("target_path")),
                WorkingDirectory = reader.IsDBNull(reader.GetOrdinal("working_directory")) ? null : reader.GetString(reader.GetOrdinal("working_directory")),
                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                IconPath = reader.IsDBNull(reader.GetOrdinal("icon_path")) ? null : reader.GetString(reader.GetOrdinal("icon_path")),
                Category = reader.IsDBNull(reader.GetOrdinal("category")) ? null : reader.GetString(reader.GetOrdinal("category")),
                IsBuiltIn = reader.GetInt32(reader.GetOrdinal("is_built_in")) == 1,
                SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
                TabId = reader.IsDBNull(reader.GetOrdinal("tab_id")) ? null : reader.GetInt32(reader.GetOrdinal("tab_id")),
                IsUninstalled = reader.GetInt32(reader.GetOrdinal("is_uninstalled")) == 1,
            });
        }
        return list;
    }

    // ═══════════════════════════════════════════
    //  URL Entries
    // ═══════════════════════════════════════════

    public List<UrlEntry> GetAllUrls()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM url_entries ORDER BY sort_order, abbreviation";
        return ReadUrls(cmd);
    }

    public UrlEntry? GetUrlByAbbreviation(string abbreviation)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM url_entries WHERE abbreviation = @abbr";
        cmd.Parameters.AddWithValue("@abbr", abbreviation.ToUpperInvariant());
        return ReadUrls(cmd).FirstOrDefault();
    }

    public UrlEntry AddUrl(UrlEntry entry)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO url_entries (abbreviation, friendly_name, url, description, category, icon_path, sort_order, tab_id)
            VALUES (@abbr, @fn, @url, @desc, @cat, @icon, @so, @tab_id);
            SELECT last_insert_rowid();
        ";
        BindUrlParams(cmd, entry);
        entry.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return entry;
    }

    public void UpdateUrl(UrlEntry entry)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE url_entries SET
                abbreviation = @abbr, friendly_name = @fn, url = @url,
                description = @desc, category = @cat, icon_path = @icon,
                sort_order = @so, tab_id = @tab_id
            WHERE id = @id
        ";
        BindUrlParams(cmd, entry);
        cmd.Parameters.AddWithValue("@id", entry.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteUrl(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM url_entries WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void BindUrlParams(SqliteCommand cmd, UrlEntry entry)
    {
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@abbr", entry.Abbreviation.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@fn", (object?)entry.FriendlyName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@url", entry.Url);
        cmd.Parameters.AddWithValue("@desc", (object?)entry.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cat", (object?)entry.Category ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@icon", (object?)entry.IconPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@so", entry.SortOrder);
        cmd.Parameters.AddWithValue("@tab_id", (object?)entry.TabId ?? DBNull.Value);
    }

    private static List<UrlEntry> ReadUrls(SqliteCommand cmd)
    {
        var list = new List<UrlEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new UrlEntry
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Abbreviation = reader.GetString(reader.GetOrdinal("abbreviation")),
                FriendlyName = reader.IsDBNull(reader.GetOrdinal("friendly_name")) ? null : reader.GetString(reader.GetOrdinal("friendly_name")),
                Url = reader.GetString(reader.GetOrdinal("url")),
                Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                Category = reader.IsDBNull(reader.GetOrdinal("category")) ? null : reader.GetString(reader.GetOrdinal("category")),
                IconPath = reader.IsDBNull(reader.GetOrdinal("icon_path")) ? null : reader.GetString(reader.GetOrdinal("icon_path")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
                TabId = reader.IsDBNull(reader.GetOrdinal("tab_id")) ? null : reader.GetInt32(reader.GetOrdinal("tab_id")),
            });
        }
        return list;
    }

    // ═══════════════════════════════════════════
    //  Tabs
    // ═══════════════════════════════════════════

    public List<TabEntry> GetAllTabs()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM tabs ORDER BY sort_order, name";
        var list = new List<TabEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TabEntry
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
            });
        }
        return list;
    }

    public TabEntry AddTab(TabEntry tab)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO tabs (name, sort_order) VALUES (@name, @so); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", tab.Name);
        cmd.Parameters.AddWithValue("@so", tab.SortOrder);
        tab.Id = Convert.ToInt32(cmd.ExecuteScalar());
        return tab;
    }

    public void UpdateTab(TabEntry tab)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE tabs SET name = @name, sort_order = @so WHERE id = @id";
        cmd.Parameters.AddWithValue("@name", tab.Name);
        cmd.Parameters.AddWithValue("@so", tab.SortOrder);
        cmd.Parameters.AddWithValue("@id", tab.Id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteTab(int id)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE app_entries SET tab_id = NULL WHERE tab_id = @id; UPDATE url_entries SET tab_id = NULL WHERE tab_id = @id; DELETE FROM tabs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void SetEntryTab(string table, int entryId, int? tabId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET tab_id = @tid WHERE id = @id";
        cmd.Parameters.AddWithValue("@tid", (object?)tabId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", entryId);
        cmd.ExecuteNonQuery();
    }

    public void SetEntrySortOrder(string table, int entryId, int sortOrder)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET sort_order = @so WHERE id = @id";
        cmd.Parameters.AddWithValue("@so", sortOrder);
        cmd.Parameters.AddWithValue("@id", entryId);
        cmd.ExecuteNonQuery();
    }

    public List<AppEntry> GetAppsByTab(int tabId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM app_entries WHERE tab_id = @tid ORDER BY sort_order, abbreviation";
        cmd.Parameters.AddWithValue("@tid", tabId);
        return ReadApps(cmd);
    }

    public List<UrlEntry> GetUrlsByTab(int tabId)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM url_entries WHERE tab_id = @tid ORDER BY sort_order, abbreviation";
        cmd.Parameters.AddWithValue("@tid", tabId);
        return ReadUrls(cmd);
    }

    // ═══════════════════════════════════════════
    //  Settings
    // ═══════════════════════════════════════════

    public string? GetSetting(string key)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @key";
        cmd.Parameters.AddWithValue("@key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings (key, value) VALUES (@key, @value)";
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
