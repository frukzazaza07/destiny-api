using System.Security.Cryptography;

namespace TarotDestiny.Api.Services;

public interface IAnswerVariantSelector
{
    CachedAnswerVariant Select(CachedAnswerSet answers);
}

public sealed class RandomAnswerVariantSelector : IAnswerVariantSelector
{
    public CachedAnswerVariant Select(CachedAnswerSet answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.Variants.Count == 0)
        {
            throw new InvalidOperationException("A cached answer set must contain at least one variant.");
        }

        return answers.Variants[RandomNumberGenerator.GetInt32(answers.Variants.Count)];
    }
}
