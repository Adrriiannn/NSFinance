using NSFinance.Api.Modules.Banking.Services.MerchantIntelligence;

namespace NSFinance.Api.Tests.Unit;

public sealed class DomainTriggerPolicyServiceTests
{
    private readonly DomainTriggerPolicyService _service = new();

    [Theory]
    [InlineData(900, DomainTriggerMode.D0)]
    [InlineData(910, DomainTriggerMode.D0)]
    [InlineData(920, DomainTriggerMode.D0)]
    [InlineData(140, DomainTriggerMode.D1)]
    [InlineData(150, DomainTriggerMode.D1)]
    [InlineData(280, DomainTriggerMode.D1)]
    [InlineData(130, DomainTriggerMode.D2)]
    [InlineData(220, DomainTriggerMode.D2)]
    [InlineData(240, DomainTriggerMode.D3)]
    [InlineData(290, DomainTriggerMode.D3)]
    [InlineData(300, DomainTriggerMode.D3)]
    public void Evaluate_KnownDomain_MapsToExpectedMode(int domainId, DomainTriggerMode expected)
    {
        var result = _service.Evaluate([domainId], normalizedDescriptor: "descriptor");

        Assert.Equal(expected, result.TriggerMode);
        Assert.Contains(domainId, result.DomainCandidates);
        Assert.False(result.UsedInferredCandidates);
    }

    [Fact]
    public void Evaluate_NoDomainCandidates_InferTransferLikeDescriptor_AsD0()
    {
        var result = _service.Evaluate([], "internal transfer savings pocket");

        Assert.Equal(DomainTriggerMode.D0, result.TriggerMode);
        Assert.True(result.UsedInferredCandidates);
    }

    [Fact]
    public void Evaluate_NoDomainCandidates_InferGiftDescriptor_AsD3()
    {
        var result = _service.Evaluate([], "charity donation world aid");

        Assert.Equal(DomainTriggerMode.D3, result.TriggerMode);
        Assert.True(result.UsedInferredCandidates);
    }

    [Fact]
    public void Evaluate_NoDomainCandidates_DefaultConsumerDescriptor_AsD2()
    {
        var result = _service.Evaluate([], "coffee shop downtown");

        Assert.Equal(DomainTriggerMode.D2, result.TriggerMode);
        Assert.True(result.UsedInferredCandidates);
    }
}

