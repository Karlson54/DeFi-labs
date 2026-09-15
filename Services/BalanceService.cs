using Nethereum.Web3;

namespace Lab1.Services;

public class BalanceService : IBalanceService
{
    private readonly IWeb3 _web3;

    public BalanceService(IWeb3 web3)
    {
        _web3 = web3;
    }

    public async Task<decimal> GetBalanceInEtherAsync(string address)
    {
        var balanceWei = await _web3.Eth.GetBalance.SendRequestAsync(address);

        return Web3.Convert.FromWei(balanceWei.Value);
    }
}
