using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Hub.Features.AgentAccess.Keys;
using NSubstitute;

namespace Hub.Tests.Features.AgentAccess.Keys;

// Phase 1.3: the pod IAM policy allows ssm:GetParametersByPath (and PutParameter) only, so
// the keys are read with GetParametersByPath, decrypted, paginated, and created with PutParameter.
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

        Assert.Equal("sign", result["SigningKey"].Value);
        Assert.Equal("enc", result["EncryptionKey"].Value);
        await ssm.Received(1).GetParametersByPathAsync(
            Arg.Is<GetParametersByPathRequest>(r => r.Path == Prefix && r.WithDecryption == true && r.Recursive == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Created_keys_under_the_prefix_are_read_by_their_name_below_it()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();
        ssm.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse
            {
                Parameters =
                [
                    new Parameter { Name = $"{Prefix}/SigningKey", Value = "legacy" },
                    new Parameter { Name = $"{Prefix}/signing/20261105010203-0a1b2c3d", Value = "sign" },
                    new Parameter { Name = $"{Prefix}/encryption/20261105010203-4e5f6a7b", Value = "enc" }
                ]
            });

        var result = await new SsmAgentAccessKeySource(ssm).ReadAsync(Prefix, TestContext.Current.CancellationToken);

        Assert.Equal("legacy", result["SigningKey"].Value);
        Assert.Equal("sign", result["signing/20261105010203-0a1b2c3d"].Value);
        Assert.Equal("enc", result["encryption/20261105010203-4e5f6a7b"].Value);
    }

    [Fact]
    public async Task Carries_each_parameters_last_modified_date()
    {
        var written = new DateTime(2026, 11, 5, 1, 2, 3, DateTimeKind.Utc);
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();
        ssm.GetParametersByPathAsync(Arg.Any<GetParametersByPathRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetParametersByPathResponse
            {
                Parameters = [new Parameter { Name = $"{Prefix}/signing/20261105010203-0a1b2c3d", Value = "sign", LastModifiedDate = written }]
            });

        var result = await new SsmAgentAccessKeySource(ssm).ReadAsync(Prefix, TestContext.Current.CancellationToken);

        Assert.Equal(written, result["signing/20261105010203-0a1b2c3d"].LastModified);
    }

    [Fact]
    public async Task Creates_a_secure_string_that_never_overwrites()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();

        await new SsmAgentAccessKeySource(ssm).CreateAsync(
            Prefix + "/", "signing/20261105010203-0a1b2c3d", "pem", TestContext.Current.CancellationToken);

        await ssm.Received(1).PutParameterAsync(
            Arg.Is<PutParameterRequest>(r =>
                r.Name == $"{Prefix}/signing/20261105010203-0a1b2c3d"
                && r.Value == "pem"
                && r.Type == ParameterType.SecureString
                && r.Overwrite == false
                && r.KeyId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Uses_the_configured_kms_key_when_there_is_one()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();

        await new SsmAgentAccessKeySource(ssm, "alias/hub-agent-access").CreateAsync(
            Prefix, "encryption/20261105010203-0a1b2c3d", "pem", TestContext.Current.CancellationToken);

        await ssm.Received(1).PutParameterAsync(
            Arg.Is<PutParameterRequest>(r => r.KeyId == "alias/hub-agent-access"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_parameter_that_already_exists_propagates_so_the_caller_can_re_read()
    {
        var ssm = Substitute.For<IAmazonSimpleSystemsManagement>();
        ssm.PutParameterAsync(Arg.Any<PutParameterRequest>(), Arg.Any<CancellationToken>())
            .Returns<PutParameterResponse>(_ => throw new ParameterAlreadyExistsException("exists"));

        await Assert.ThrowsAsync<ParameterAlreadyExistsException>(
            () => new SsmAgentAccessKeySource(ssm).CreateAsync(Prefix, "signing/x", "pem", TestContext.Current.CancellationToken));
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
