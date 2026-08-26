using System.Security.Claims;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TarotDestiny.Api.Services;

namespace TarotDestiny.Api.Tests;

[TestClass]
public sealed class DeepReadingAccessTests
{
    [TestMethod]
    public void DevelopmentCanAllowUnentitledDeepReading()
    {
        var policy = NewPolicy(Environments.Development, allowInDevelopment: true);

        var access = policy.Evaluate(new ClaimsPrincipal());

        Assert.IsTrue(access.Enabled);
        Assert.IsTrue(access.Entitled);
    }

    [TestMethod]
    public void ProductionRejectsAnonymousUser()
    {
        var policy = NewPolicy(Environments.Production, allowInDevelopment: true);

        var access = policy.Evaluate(new ClaimsPrincipal());

        Assert.IsTrue(access.Enabled);
        Assert.IsFalse(access.Entitled);
    }

    [TestMethod]
    public void ProductionAcceptsServerIssuedPremiumClaim()
    {
        var policy = NewPolicy(Environments.Production, allowInDevelopment: false);
        var identity = new ClaimsIdentity(
            [new Claim("tarot:deep_reading", "true")],
            authenticationType: "test");

        var access = policy.Evaluate(new ClaimsPrincipal(identity));

        Assert.IsTrue(access.Entitled);
    }

    private static DeepReadingAccessPolicy NewPolicy(
        string environment,
        bool allowInDevelopment) =>
        new(
            Options.Create(new DeepReadingOptions
            {
                Enabled = true,
                AllowUnentitledInDevelopment = allowInDevelopment
            }),
            new TestHostEnvironment { EnvironmentName = environment });

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "TarotDestiny.Api.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
