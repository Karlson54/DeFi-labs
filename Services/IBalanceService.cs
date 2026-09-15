namespace Lab1.Services;

public interface IBalanceService
{
    Task<decimal> GetBalanceInEtherAsync(string address);
}
