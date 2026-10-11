using DeFi.Models;

namespace DeFi.Services;

public sealed class DeploymentSession
{
    private readonly IWeb3Factory _web3Factory;
    private readonly IDeploymentStateStore _stateStore;
    private bool _cascade;

    public DeploymentSession(IWeb3Factory web3Factory, IDeploymentStateStore stateStore, DeploymentState state)
    {
        _web3Factory = web3Factory;
        _stateStore = stateStore;
        State = state;
    }

    public DeploymentState State { get; private set; }

    public async Task<ContractRef> EnsureAsync(
        string key, string name, Func<Task<DeployedContract>> deploy, CancellationToken cancellationToken = default)
    {
        var known = _cascade ? null : State.Get(key);

        if (!string.IsNullOrWhiteSpace(known) && await HasContractCodeAsync(known))
        {
            return new ContractRef(name, known, WasAlreadyDeployed: true, TransactionHash: null);
        }

        var deployed = await deploy();

        _cascade = true;
        State = State.With(key, deployed.Address);
        await _stateStore.SaveAsync(State, cancellationToken);

        return new ContractRef(name, deployed.Address, WasAlreadyDeployed: false, deployed.TransactionHash);
    }

    private async Task<bool> HasContractCodeAsync(string address)
    {
        var code = await _web3Factory.Client.Eth.GetCode.SendRequestAsync(address);
        return !string.IsNullOrEmpty(code) && code != "0x";
    }
}