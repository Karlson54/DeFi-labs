using System.Numerics;
using DeFi.Models;
using DeFi.Models.Contracts;
using Microsoft.Extensions.Options;
using Nethereum.ABI.FunctionEncoding;
using Nethereum.Contracts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.JsonRpc.Client;

namespace DeFi.Services;

public interface IProtocolService
{
    Task<DeployedContract> DeployAsync(BigInteger initialFee, CancellationToken cancellationToken = default);

    Task<BigInteger> GetFeeAsync(string protocolAddress, CancellationToken cancellationToken = default);

    Task<OwnershipTransferResult> TransferOwnershipAsync(string protocolAddress, string newOwner, CancellationToken cancellationToken = default);

    Task<DirectCallAttemptResult> TrySetFeeDirectlyAsync(string protocolAddress, BigInteger newFee, CancellationToken cancellationToken = default);

    Task<byte[]> EncodeSetFeeAsync(string protocolAddress, BigInteger newFee, CancellationToken cancellationToken = default);
}

public sealed class ProtocolService : IProtocolService
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IContractArtifactProvider _artifacts;
    private readonly IContractDeployer _deployer;
    private readonly TimeSpan _timeout;

    public ProtocolService(
        IWeb3Factory web3Factory, IContractArtifactProvider artifacts,
        IContractDeployer deployer, IOptions<Web3Settings> options)
    {
        _web3Factory = web3Factory;
        _artifacts = artifacts;
        _deployer = deployer;
        _timeout = TimeSpan.FromSeconds(options.Value.TransactionTimeoutSeconds);
    }

    public Task<DeployedContract> DeployAsync(BigInteger initialFee, CancellationToken cancellationToken = default) =>
        _deployer.DeployAsync<MockProtocolDeployment>(
            "MockProtocol", m => m.InitialFee = initialFee,
            "Перевірте байткод артефакту MockProtocol.json та баланс на газ.",
            cancellationToken);

    public async Task<BigInteger> GetFeeAsync(string protocolAddress, CancellationToken cancellationToken = default) =>
        await _web3Factory.Client.Eth
            .GetContractQueryHandler<FeeFunction>()
            .QueryAsync<BigInteger>(protocolAddress, new FeeFunction())
            .WaitAsync(_timeout, cancellationToken);

    public async Task<OwnershipTransferResult> TransferOwnershipAsync(
        string protocolAddress, string newOwner, CancellationToken cancellationToken = default)
    {
        var currentOwner = await _web3Factory.Client.Eth
            .GetContractQueryHandler<OwnerFunction>()
            .QueryAsync<string>(protocolAddress, new OwnerFunction())
            .WaitAsync(_timeout, cancellationToken);

        if (string.Equals(currentOwner, newOwner, StringComparison.OrdinalIgnoreCase))
        {
            return new OwnershipTransferResult(newOwner, WasAlreadyTransferred: true, TransactionHash: null);
        }

        var receipt = await _web3Factory.Client.Eth
            .GetContractTransactionHandler<TransferOwnershipFunction>()
            .SendRequestAndWaitForReceiptAsync(protocolAddress, new TransferOwnershipFunction { NewOwner = newOwner })
            .WaitAsync(_timeout, cancellationToken);

        if (receipt.Status?.Value != 1)
        {
            throw new InvalidOperationException(
                "transferOwnership відхилено мережею: права власності може передати лише поточний власник " +
                "(акаунт-розгортач).");
        }

        return new OwnershipTransferResult(newOwner, WasAlreadyTransferred: false, receipt.TransactionHash);
    }

    public async Task<DirectCallAttemptResult> TrySetFeeDirectlyAsync(
        string protocolAddress, BigInteger newFee, CancellationToken cancellationToken = default)
    {
        try
        {
            var receipt = await _web3Factory.Client.Eth
                .GetContractTransactionHandler<SetFeeFunction>()
                .SendRequestAndWaitForReceiptAsync(protocolAddress, new SetFeeFunction { NewFee = newFee })
                .WaitAsync(_timeout, cancellationToken);

            return receipt.Status?.Value != 1
                ? new DirectCallAttemptResult(true, "транзакцію відхилено (status = 0)", receipt.TransactionHash)
                : new DirectCallAttemptResult(false, null, receipt.TransactionHash);
        }
        catch (RpcResponseException ex)
        {
            return new DirectCallAttemptResult(true, RevertReasonParser.Extract(ex.Message), null);
        }
        catch (SmartContractRevertException ex)
        {
            return new DirectCallAttemptResult(true, RevertReasonParser.Extract(ex.Message), null);
        }
        catch (SmartContractCustomErrorRevertException ex)
        {
            return new DirectCallAttemptResult(true, RevertReasonParser.Extract(ex.Message), null);
        }
    }

    public async Task<byte[]> EncodeSetFeeAsync(
        string protocolAddress, BigInteger newFee, CancellationToken cancellationToken = default)
    {
        var artifact = await _artifacts.GetAsync("MockProtocol", cancellationToken);
        var contract = _web3Factory.Client.Eth.GetContract(artifact.Abi, protocolAddress);
        var data = contract.GetFunction("setFee").GetData(newFee);
        return data.HexToByteArray();
    }
}