import { useState } from 'react';
import { ethers } from 'ethers';

const API_BASE_URL = 'http://localhost:5000';

function App() {
  const [account, setAccount] = useState(null);
  const [history, setHistory] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  const connectWallet = async () => {
    if (!window.ethereum) {
      alert('Будь ласка, встановіть розширення MetaMask!');
      return;
    }

    try {
      const provider = new ethers.BrowserProvider(window.ethereum);
      const accounts = await provider.send('eth_requestAccounts', []);
      const userAddress = accounts[0];
      setAccount(userAddress);
      await fetchHistory(userAddress);
    } catch (err) {
      console.error('Помилка підключення гаманця:', err);
      setError('Не вдалося підключити гаманець.');
    }
  };

  const fetchHistory = async (walletAddress) => {
    setLoading(true);
    setError(null);

    try {
      const response = await fetch(`${API_BASE_URL}/api/swaps?trader=${walletAddress}`);

      if (!response.ok) {
        throw new Error(`API повернув статус ${response.status}`);
      }

      const data = await response.json();
      setHistory(data);
    } catch (err) {
      console.error('Помилка завантаження історії:', err);
      setError('Не вдалося завантажити історію обмінів із бекенду.');
    } finally {
      setLoading(false);
    }
  };

  return (
      <div style={{ padding: '20px', fontFamily: 'Arial' }}>
        <h1>DeFi Пул — Історія обмінів (React SPA)</h1>

        {!account ? (
            <button onClick={connectWallet} style={{ padding: '10px' }}>
              Підключити MetaMask
            </button>
        ) : (
            <p>
              <strong>Підключено:</strong> {account}
            </p>
        )}

        {error && <p style={{ color: 'crimson' }}>{error}</p>}
        {loading && <p>Завантаження...</p>}

        <h2>Історія обмінів (з БД)</h2>
        <table border="1" cellPadding="10" style={{ borderCollapse: 'collapse', width: '100%' }}>
          <thead>
          <tr>
            <th>Блок</th>
            <th>Хеш транзакції</th>
            <th>Віддав (Amount In)</th>
            <th>Отримав (Amount Out)</th>
            <th>Комісія</th>
          </tr>
          </thead>
          <tbody>
          {history.length > 0 ? (
              history.map((swap) => (
                  <tr key={`${swap.transactionHash}-${swap.blockNumber}`}>
                    <td>{swap.blockNumber}</td>
                    <td>{swap.transactionHash}</td>
                    <td>{swap.amountIn}</td>
                    <td>{swap.amountOut}</td>
                    <td>{(swap.feeBps / 100).toFixed(2)}%</td>
                  </tr>
              ))
          ) : (
              <tr>
                <td colSpan="5">Історія порожня</td>
              </tr>
          )}
          </tbody>
        </table>
      </div>
  );
}

export default App;