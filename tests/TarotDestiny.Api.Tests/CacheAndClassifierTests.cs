using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class CacheKeyBuilderTests
{
    [TestMethod]
    public void PositionAndOrderChangeCacheKey()
    {
        var builder = NewBuilder();
        var original = TestSupport.DestinyRequest();
        var changed = original with
        {
            Cards =
            [
                new("PAST", "THE_STAR", Orientation.UPRIGHT),
                new("PRESENT", "THE_MAGICIAN", Orientation.UPRIGHT),
                new("DIRECTION", "THE_TOWER", Orientation.UPRIGHT)
            ]
        };

        Assert.AreNotEqual(
            builder.BuildRedisKey(original, TestSupport.CareerChangeClassification()),
            builder.BuildRedisKey(changed, TestSupport.CareerChangeClassification()));
    }

    [TestMethod]
    public void OrientationChangesCacheKey()
    {
        var builder = NewBuilder();
        var original = TestSupport.DestinyRequest();
        var changed = original with
        {
            Cards =
            [
                original.Cards[0],
                original.Cards[1] with { Orientation = Orientation.REVERSED },
                original.Cards[2]
            ]
        };

        Assert.AreNotEqual(
            builder.BuildRedisKey(original, TestSupport.CareerChangeClassification()),
            builder.BuildRedisKey(changed, TestSupport.CareerChangeClassification()));
    }

    [TestMethod]
    public void LocaleChangesCacheKey()
    {
        var builder = NewBuilder();
        var thai = TestSupport.DestinyRequest(locale: "th");
        var english = TestSupport.DestinyRequest(locale: "en");

        Assert.AreNotEqual(
            builder.BuildRedisKey(thai, TestSupport.CareerChangeClassification()),
            builder.BuildRedisKey(english, TestSupport.CareerChangeClassification()));
    }

    [TestMethod]
    public void PromptAndInterpretationVersionsChangeCacheKey()
    {
        var request = TestSupport.DestinyRequest();
        var classification = TestSupport.CareerChangeClassification();
        var first = NewBuilder("PROMPT_V1", "INTERPRETATION_V1");
        var promptChanged = NewBuilder("PROMPT_V2", "INTERPRETATION_V1");
        var interpretationChanged = NewBuilder("PROMPT_V1", "INTERPRETATION_V2");

        Assert.AreNotEqual(first.BuildRedisKey(request, classification), promptChanged.BuildRedisKey(request, classification));
        Assert.AreNotEqual(first.BuildRedisKey(request, classification), interpretationChanged.BuildRedisKey(request, classification));
    }

    [TestMethod]
    public void RawQuestionDoesNotChangeSharedCacheKey()
    {
        var builder = NewBuilder();
        var first = TestSupport.DestinyRequest("Should I change my job?");
        var second = TestSupport.DestinyRequest("Should I leave my company?");

        Assert.AreEqual(
            builder.BuildRedisKey(first, TestSupport.CareerChangeClassification()),
            builder.BuildRedisKey(second, TestSupport.CareerChangeClassification()));
    }

    [TestMethod]
    public void StandardAndDeepReadingsUseDifferentCacheKeys()
    {
        var builder = NewBuilder();
        var standard = TestSupport.DestinyRequest(readingMode: ReadingMode.STANDARD);
        var deep = standard with { ReadingMode = ReadingMode.DEEP };

        Assert.AreNotEqual(
            builder.BuildRedisKey(standard, TestSupport.CareerChangeClassification()),
            builder.BuildRedisKey(deep, TestSupport.CareerChangeClassification()));
    }

    private static CacheKeyBuilder NewBuilder(
        string promptVersion = "PROMPT_V1",
        string interpretationVersion = "INTERPRETATION_V1") =>
        new(Options.Create(new TarotCacheOptions
        {
            PromptVersion = promptVersion,
            InterpretationVersion = interpretationVersion
        }));
}

[TestClass]
public sealed class QuestionClassifierTests
{
    [TestMethod]
    public void ChangeJobQuestionIsCacheEligible()
    {
        var classifier = TestSupport.NewClassifier();
        var result = classifier.Classify("Should I change my job?", "en");

        Assert.AreEqual(TarotDomain.CAREER, result.Domain);
        Assert.AreEqual("CAREER_CHANGE_JOB", result.Intent);
        Assert.AreEqual(PersonalizationLevel.LOW, result.Personalization);
        Assert.IsTrue(classifier.CanUseSharedCache(result));
    }

    [TestMethod]
    public void LowConfidenceQuestionSkipsSharedCache()
    {
        var classifier = TestSupport.NewClassifier();
        var result = classifier.Classify("what is around this situation", "en");

        Assert.AreEqual(TarotIntents.PersonalCustom, result.Intent);
        Assert.IsFalse(classifier.CanUseSharedCache(result));
    }

    [TestMethod]
    public void HighlyPersonalQuestionSkipsSharedCache()
    {
        var classifier = TestSupport.NewClassifier();
        var result = classifier.Classify(
            "I have worked with my brother for 8 years and we are considering selling our company because he wants to move overseas. What should I do?",
            "en");

        Assert.AreEqual(PersonalizationLevel.HIGH, result.Personalization);
        Assert.IsFalse(classifier.CanUseSharedCache(result));
    }

    [TestMethod]
    public void ExplicitTopicUsesStableGeneralIntent()
    {
        var classifier = TestSupport.NewClassifier();
        var result = classifier.Classify("TOPIC: LOVE\nREQUEST: Love guidance", "en");

        Assert.AreEqual(TarotDomain.LOVE, result.Domain);
        Assert.AreEqual("LOVE_GENERAL", result.Intent);
        Assert.IsTrue(classifier.CanUseSharedCache(result));
    }
}
