namespace DeFi.Services;

public interface IBalanceService
{
    Task<decimal> GetBalanceInEtherAsync(string address);
}
