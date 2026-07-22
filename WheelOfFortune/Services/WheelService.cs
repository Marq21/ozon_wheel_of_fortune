using FortuneWheel.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FortuneWheel.Services;

/// <summary>
/// Логика розыгрыша. Метод Spin мутирует коэффициенты у объектов Player,
/// переданных в списке allPlayers.
/// </summary>
public sealed class WheelService
{
    private readonly Random _random = new();

    /// <summary>
    /// Выполняет розыгрыш.
    /// </summary>
    /// <returns>Кортеж: (победитель, его коэффициент ДО обнуления).</returns>
    public (Player? winner, int coefAtWin) Spin(List<Player> allPlayers, List<int> participantIds)
    {
        if (participantIds is null || participantIds.Count == 0) return (null, 0);

        var participants = allPlayers.Where(p => participantIds.Contains(p.Id)).ToList();
        if (participants.Count == 0) return (null, 0);

        // --- Шаг 3: запоминаем всех, у кого коэффициент == 0 ДО розыгрыша ---
        var oldZeros = allPlayers.Where(p => p.Coefficient == 0).ToList();

        // --- Адаптивный множитель: чем больше участников, тем сильнее влияние коэффициента ---
        double multiplier = GetMultiplier(participants.Count);

        // --- Шаг 4: взвешенная сумма ---
        double S = participants.Sum(p => Math.Pow(p.Coefficient, multiplier));

        // --- Шаг 5: если у всех участников 0 — временно поднимаем до 1 ---
        if (S == 0)
        {
            foreach (var p in participants) p.Coefficient = 1;
            S = participants.Count;
        }

        // --- Шаги 6–7: взвешенный случайный выбор ---
        double rand = _random.NextDouble() * S;
        Player? winner = null;
        foreach (var p in participants)
        {
            rand -= Math.Pow(p.Coefficient, multiplier);
            if (rand <= 0) { winner = p; break; }
        }
        winner ??= participants[^1]; // страховка от погрешностей float

        // --- Запоминаем коэффициент победителя ДО обнуления ---
        int coefAtWin = winner.Coefficient;

        // --- Шаг 8: обновление коэффициентов ---
        foreach (var p in participants)
        {
            if (p.Id == winner.Id) p.Coefficient = 0;
            else p.Coefficient += 1;
        }

        // --- Шаг 9: «подтягиваем» старые нули, которые всё ещё в нуле ---
        foreach (var p in oldZeros)
        {
            if (p.Coefficient == 0) p.Coefficient = 1;
        }

        return (winner, coefAtWin);
    }

    /// <summary>
    /// Адаптивный множитель для формулы веса.
    /// </summary>
    private static double GetMultiplier(int participantCount)
    {
        if (participantCount <= 5) return 2.5 + (5 - participantCount) * 0.25f;
        if (participantCount >= 9) return 3;
        return 2.5 + (participantCount - 5) * 0.25f;
    }
}