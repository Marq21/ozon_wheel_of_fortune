using FortuneWheel.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;

namespace FortuneWheel.Services;

/// <summary>Сервис доступа к SQLite-базе.</summary>
public sealed class DbService
{
    private readonly string _connectionString;

    public DbService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>Создаёт таблицу, если её нет.</summary>
    public void Init()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Players (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT    UNIQUE NOT NULL,
                Coefficient INTEGER NOT NULL DEFAULT 1,
                IsActive    INTEGER NOT NULL DEFAULT 1
            );";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Загружает всех сотрудников (включая мягко удалённых — на будущее).</summary>
    public List<Player> LoadAll() => LoadWhere("");

    /// <summary>Загружает только активных сотрудников.</summary>
    public List<Player> LoadActive() => LoadWhere("WHERE IsActive = 1");

    private List<Player> LoadWhere(string whereClause)
    {
        var list = new List<Player>();
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT Id, Name, Coefficient, IsActive FROM Players {whereClause} ORDER BY Name;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Player
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Coefficient = reader.GetInt32(2),
                IsActive = reader.GetInt32(3) == 1
            });
        }
        return list;
    }

    /// <summary>Добавляет нового сотрудника с коэффициентом 1. Возвращает null, если имя занято.</summary>
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
            return null; // нарушение UNIQUE
        }
    }

    /// <summary>Мягкое удаление: IsActive = 0.</summary>
    public void SoftDelete(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Players SET IsActive = 0 WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Жёсткое удаление: физически удаляет строку.</summary>
    public void HardDelete(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Players WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Пакетно обновляет коэффициенты всех переданных игроков (в одной транзакции).</summary>
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
}
