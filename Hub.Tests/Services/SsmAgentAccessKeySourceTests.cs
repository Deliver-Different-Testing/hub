using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Hub.Services;
using NSubstitute;

namespace Hub.Tests.Services;

// Phase 1.3: the pod IAM policy allows ssm:GetParametersByPath (and PutParameter) only, so
// the keys are read with GetParametersByPath, decrypted, paginated.
public class SsmAgentAccessKeySourceTests
{
    private const string Prefix = "/Hub/urgent/AgentAccess";

    [Fact]
    public async Task Reads_by_path_with_decryption_and_maps_parameters_by_leaf_name()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();
        ssm.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse
            {
                Parameters =
                [
                    new Parameter { Name = $"{Prefix}/SigningKey", Value = "sign" },
                    new Parameter { Name = $"{Prefix}/EncryptionKey", Value = "enc" }
                ]
            });

        var result = await new SsmAgentAccessKeySource(ssm).ReadAsync(Prefix, TestContext.Current.CancellationToken);

        Assert.Equal("sign", result["SigningKey"]);
        Assert.Equal("enc", result["EncryptionKey"]);
        await ssm.Received(1).GetParametersByPathAsync(
            Arg.Is<GetParametersByPathRequest>(r => r.Path == Prefix && r.WithDecryption == true && r.Recursive == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Follows_NextToken_until_the_last_page()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();
        ssm.GetParametersByPathAsync(Arg.Is<GetParametersByPathRequest>(r => r.NextToken == null), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse
            {
                NextToken = "page2",
                Parameters = [new Parameter { Name = $"{Prefix}/SigningKey", Value = "sign" }]
            });
        ssm.GetParametersByPathAsync(Arg.Is<GetParametersByPathRequest>(r => r.NextToken == "page2"), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse
            {
                Parameters = [new Parameter { Name = $"{Prefix}/EncryptionKey", Value = "enc" }]
            });

        var result = await new SsmAgentAccessKeySource(ssm).ReadAsync(Prefix, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task A_trailing_slash_on_the_prefix_is_ignored()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();
        ssm.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse { Parameters = [] });

        await new SsmAgentAccessKeySource(ssm).ReadAsync(Prefix + "/", TestContext.Current.CancellationToken);

        await ssm.Received(1).GetParametersByPathAsync(
            Arg.Is<GetParametersByPathRequest>(r => r.Path == Prefix), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ssm_failures_propagate_so_the_caller_can_fail_closed()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();
        ssm.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetParametersByPathResponse>(_ => throw new AmazonSimpleSystemsManagementException("denied"));

        await Assert.ThrowsAsync<AmazonSimpleSystemsManagementException>(
            () => new SsmAgentAccessKeySource(ssm).ReadAsync(Prefix, TestContext.Current.CancellationToken));
    }
}
