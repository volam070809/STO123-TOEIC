namespace STO123.Models;

public sealed class ArenaMatch
{
    public Guid Id { get; set; }
    public int Part { get; set; }
    public int OwnerId { get; set; }
    public int? OpponentId { get; set; }
    public int? ForfeitById { get; set; }
    public string? RoomCode { get; set; }
    public string Status { get; set; } = "WAITING";
    public bool OwnerReady { get; set; }
    public bool OpponentReady { get; set; }
    public int QuestionIndex { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime? DeadlineUtc { get; set; }
    public DateTime? RevealUntilUtc { get; set; }
    public string? QuestionsJson { get; set; }
    public string AnswersJson { get; set; } = "[]";
    public byte[] RowVersion { get; set; } = [];
}
