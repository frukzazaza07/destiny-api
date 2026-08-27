namespace TarotDestiny.Api.Data;

public sealed class GeneratedAnswerVariantEntity
{
    public Guid Id { get; set; }
    public Guid GeneratedAnswerId { get; set; }
    public int VariantNumber { get; set; }
    public string ResponseJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public GeneratedAnswerEntity GeneratedAnswer { get; set; } = null!;
}
