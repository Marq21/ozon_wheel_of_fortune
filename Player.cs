namespace FortuneWheel.Models;

/// <summary>Сущность сотрудника (строка в таблице Players).</summary>
public class Player
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Coefficient { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}
