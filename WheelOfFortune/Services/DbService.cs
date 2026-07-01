using FortuneWheel.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

namespace FortuneWheel.Services;

/// <summary>Сервис доступа к SQLite-базе.</summary>
public sealed class DbService
{
    private readonly string _connectionString;

    public DbService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>Создаёт таблицы и выполняет миграцию.</summary>
    public void Init()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Основная таблица
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Players (
                    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name           TEXT    UNIQUE NOT NULL,
                    Coefficient    INTEGER NOT NULL DEFAULT 1,
                    IsActive       INTEGER NOT NULL DEFAULT 1
                );";
            cmd.ExecuteNonQuery();
        }

        // Миграция: добавляем колонки статистики, если их нет
        EnsureColumn(conn, "Players", "Participations", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "Players", "Wins", "INTEGER NOT NULL DEFAULT 0");

        // Таблица истории розыгрышей
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS SpinHistory (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    SpinDate          TEXT    NOT NULL,
                    WinnerId          INTEGER NOT NULL,
                    WinnerCoefficient INTEGER NOT NULL,
                    ParticipantsCount INTEGER NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(1) == column) return true;
        }
        return false;
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string type)
    {
        if (!ColumnExists(conn, table, column))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
            cmd.ExecuteNonQuery();
        }
    }

    public List<Player> LoadAll() => LoadWhere("");
    public List<Player> LoadActive() => LoadWhere("WHERE p.IsActive = 1");

    private List<Player> LoadWhere(string whereClause)
    {
        var list = new List<Player>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT p.Id, p.Name, p.Coefficient, p.IsActive,
                   p.Participations, p.Wins,
                   (SELECT AVG(WinnerCoefficient) FROM SpinHistory WHERE WinnerId = p.Id) AS AvgCoef
            FROM Players p {whereClause}
            ORDER BY p.Name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Player
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Coefficient = reader.GetInt32(2),
                IsActive = reader.GetInt32(3) == 1,
                Participations = reader.GetInt32(4),
                Wins = reader.GetInt32(5),
                AvgWinCoefficient = reader.IsDBNull(6) ? null : reader.GetDouble(6)
            });
        }
        return list;
    }

    public Player? Add(string name)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Players (Name, Coefficient, IsActive)
            VALUES (@name, 1, 1);
            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        try
        {
            var id = Convert.ToInt32(cmd.ExecuteScalar());
            return new Player { Id = id, Name = name.Trim(), Coefficient = 1, IsActive = true };
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    public void SoftDelete(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Players SET IsActive = 0 WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void HardDelete(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Players WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void Restore(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Players SET IsActive = 1 WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public void UpdateCoefficients(List<Player> players)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Players SET Coefficient = @coef WHERE Id = @id;";
        var pCoef = cmd.Parameters.Add("@coef", SqliteType.Integer);
        var pId = cmd.Parameters.Add("@id", SqliteType.Integer);
        foreach (var pl in players)
        {
            pCoef.Value = pl.Coefficient;
            pId.Value = pl.Id;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Записывает розыгрыш в историю и обновляет счётчики участников.</summary>
    public void RecordSpin(int winnerId, int winnerCoefficient, int participantsCount, List<int> participantIds)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        // 1. Запись в историю
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO SpinHistory (SpinDate, WinnerId, WinnerCoefficient, ParticipantsCount)
                VALUES (@date, @winnerId, @coef, @count);";
            cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@winnerId", winnerId);
            cmd.Parameters.AddWithValue("@coef", winnerCoefficient);
            cmd.Parameters.AddWithValue("@count", participantsCount);
            cmd.ExecuteNonQuery();
        }

        // 2. Инкремент Participations у всех участников
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Players SET Participations = Participations + 1 WHERE Id = @id";
            var pId = cmd.Parameters.Add("@id", SqliteType.Integer);
            foreach (var id in participantIds)
            {
                pId.Value = id;
                cmd.ExecuteNonQuery();
            }
        }

        // 3. Инкремент Wins у победителя
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Players SET Wins = Wins + 1 WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", winnerId);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public string ExportToCsv()
    {
        var all = LoadAll();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,Name,Coefficient,IsActive,Participations,Wins");
        foreach (var p in all)
        {
            sb.AppendLine($"{p.Id},{EscapeCsv(p.Name)},{p.Coefficient},{(p.IsActive ? 1 : 0)},{p.Participations},{p.Wins}");
        }
        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    public int ImportFromCsv(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("CSV-файл не найден.", filePath);

        var lines = File.ReadAllLines(filePath, System.Text.Encoding.UTF8);
        if (lines.Length < 2) return 0;

        var processedCount = 0;
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Players (Id, Name, Coefficient, IsActive, Participations, Wins)
            VALUES (@id, @name, @coef, @active, @part, @wins);";

        var pId = cmd.Parameters.Add("@id", SqliteType.Integer);
        var pName = cmd.Parameters.Add("@name", SqliteType.Text);
        var pCoef = cmd.Parameters.Add("@coef", SqliteType.Integer);
        var pActive = cmd.Parameters.Add("@active", SqliteType.Integer);
        var pPart = cmd.Parameters.Add("@part", SqliteType.Integer);
        var pWins = cmd.Parameters.Add("@wins", SqliteType.Integer);

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var parts = ParseCsvLine(line);
            if (parts.Length < 4) continue;

            pId.Value = int.TryParse(parts[0], out int id) ? id : 0;
            pName.Value = parts[1].Trim();
            pCoef.Value = int.TryParse(parts[2], out int coef) ? coef : 1;
            pActive.Value = parts[3] == "1" ? 1 : 0;
            pPart.Value = parts.Length > 4 && int.TryParse(parts[4], out int part) ? part : 0;
            pWins.Value = parts.Length > 5 && int.TryParse(parts[5], out int wins) ? wins : 0;

            cmd.ExecuteNonQuery();
            processedCount++;
        }

        tx.Commit();
        return processedCount;
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) { result.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        result.Add(current.ToString());
        return [.. result];
    }

    /// <summary>Сбрасывает все коэффициенты сотрудников до 1.</summary>
    public int ResetCoefficients()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Players SET Coefficient = 1";
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Сбрасывает статистику (Participations, Wins) и историю розыгрышей.</summary>
    public void ResetStatistics()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE Players SET Participations = 0, Wins = 0";
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM SpinHistory";
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Полностью очищает базу данных (все таблицы).</summary>
    public void ResetAll()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM SpinHistory";
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM Players";
            cmd.ExecuteNonQuery();
        }
        // Сбрасываем счётчик AUTOINCREMENT
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM sqlite_sequence WHERE name='Players'";
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM sqlite_sequence WHERE name='SpinHistory'";
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Быстрый экспорт в папку с БД (без диалога).</summary>
    public string QuickExport()
    {
        var csv = ExportToCsv();
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = $"fortune_backup_{timestamp}.csv";

        var dbFolder = Path.GetDirectoryName(_connectionString.Replace("Data Source=", "")) ?? ".";
        var filePath = Path.Combine(dbFolder, fileName);

        File.WriteAllText(filePath, csv, System.Text.Encoding.UTF8);
        return filePath;
    }
}