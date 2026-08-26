using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Contracts;
using TarotDestiny.Api.Domain;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class ResilientClassifierTests
{
    [TestMethod]
    public async Task ExplicitTopicStaysLocalAndAvoidsGrpc()
    {
        var remote = new StubRemoteClassifier(TestSupport.CareerChangeClassification());
        var classifier = Create(remote);

        var result = await classifier.ClassifyAsync(
            "TOPIC: LOVE\nREQUEST: Love guidance",
            "en",
            CancellationToken.None);

        Assert.AreEqual("LOVE_GENERAL", result.Intent);
        Assert.AreEqual(ClassifierSources.CSharpTopic, result.Source);
        Assert.AreEqual(0, remote.CallCount);
    }

    [TestMethod]
    public async Task ValidRemotePredictionIsUsed()
    {
        var remoteResult = new ClassificationResult(
            TarotDomain.CAREER,
            "CAREER_CHANGE_JOB",
            0.96,
            PersonalizationLevel.LOW,
            ClassifierSources.PythonGrpc,
            "tfidf-logreg-seed-v1");
        var remote = new StubRemoteClassifier(remoteResult);
        var classifier = Create(remote);

        var result = await classifier.ClassifyAsync(
            "Should I change my job?",
            "en",
            CancellationToken.None);

        Assert.AreEqual(ClassifierSources.PythonGrpc, result.Source);
        Assert.AreEqual("tfidf-logreg-seed-v1", result.ModelVersion);
        Assert.AreEqual(1, remote.CallCount);
        Assert.IsTrue(classifier.CanUseSharedCache(result));
    }

    [TestMethod]
    public async Task LocalSafetyRulesCanRaiseRemotePersonalization()
    {
        var remote = new StubRemoteClassifier(new ClassificationResult(
            TarotDomain.CAREER,
            "CAREER_BUSINESS",
            0.96,
            PersonalizationLevel.LOW,
            ClassifierSources.PythonGrpc,
            "tfidf-logreg-seed-v1"));
        var classifier = Create(remote);

        var result = await classifier.ClassifyAsync(
            "I have worked with my brother for 8 years and we are considering selling our company because he wants to move overseas. What should I do?",
            "en",
            CancellationToken.None);

        Assert.AreEqual(PersonalizationLevel.HIGH, result.Personalization);
        Assert.IsFalse(classifier.CanUseSharedCache(result));
    }

    [TestMethod]
    public async Task LowRemoteConfidenceBecomesPersonalCustom()
    {
        var remote = new StubRemoteClassifier(new ClassificationResult(
            TarotDomain.GENERAL,
            "GENERAL_DIRECTION",
            0.54,
            PersonalizationLevel.LOW,
            ClassifierSources.PythonGrpc,
            "tfidf-logreg-seed-v1"));
        var classifier = Create(remote);

        var result = await classifier.ClassifyAsync(
            "What is around this unusual situation?",
            "en",
            CancellationToken.None);

        Assert.AreEqual(TarotIntents.PersonalCustom, result.Intent);
        Assert.IsFalse(classifier.CanUseSharedCache(result));
    }

    [TestMethod]
    public async Task RemoteFailureUsesRuleFallbackAndRecordsMetric()
    {
        var remote = new StubRemoteClassifier(new InvalidOperationException("unavailable"));
        var metrics = new TarotMetrics();
        var classifier = Create(remote, metrics);

        var result = await classifier.ClassifyAsync(
            "Should I change my job?",
            "en",
            CancellationToken.None);

        Assert.AreEqual("CAREER_CHANGE_JOB", result.Intent);
        Assert.AreEqual(ClassifierSources.CSharpRuleFallback, result.Source);
        Assert.AreEqual(1, metrics.Snapshot().ClassifierFallbackUsed);
    }

    private static ResilientQuestionClassifier Create(
        IRemoteQuestionClassifier remote,
        TarotMetrics? metrics = null)
    {
        var options = Options.Create(new ClassifierOptions
        {
            UseGrpc = true,
            MinimumCacheConfidence = 0.85,
            DeadlineMilliseconds = 100
        });
        return new ResilientQuestionClassifier(
            TestSupport.NewClassifier(),
            remote,
            options,
            metrics ?? new TarotMetrics(),
            TestSupport.LoggerFactory.CreateLogger<ResilientQuestionClassifier>());
    }

    private sealed class StubRemoteClassifier : IRemoteQuestionClassifier
    {
        private readonly ClassificationResult? _result;
        private readonly Exception? _exception;
        private int _callCount;

        public StubRemoteClassifier(ClassificationResult result) => _result = result;
        public StubRemoteClassifier(Exception exception) => _exception = exception;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<ClassificationResult> ClassifyAsync(
            string question,
            string locale,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return _exception is null
                ? Task.FromResult(_result!)
                : Task.FromException<ClassificationResult>(_exception);
        }
    }
}
