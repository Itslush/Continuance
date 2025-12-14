using Continuance.Models;

namespace Continuance.Core
{
    public class AccountManager
    {
        private readonly List<Account> _accounts = [];
        private readonly List<int> _selectedAccountIndices = [];
        private readonly Dictionary<long, (VerificationStatus Status, string Details)> _verificationResults = [];

        private readonly object _lock = new();

        public IReadOnlyList<Account> GetAllAccounts()
        {
            lock (_lock)
            {
                return _accounts.AsReadOnly();
            }
        }

        public List<int> GetSelectedAccountIndices()
        {
            lock (_lock)
            {
                return [.. _selectedAccountIndices];
            }
        }

        internal List<int> GetSelectedAccountIndicesList()
        {
            return _selectedAccountIndices;
        }

        public List<Account> GetSelectedAccounts()
        {
            lock (_lock)
            {
                return [.. _selectedAccountIndices
                    .Where(index => index >= 0 && index < _accounts.Count)
                    .Select(index => _accounts[index])];
            }
        }

        internal void AddAccount(Account newAccount)
        {
            lock (_lock)
            {
                if (!_accounts.Any(a => a.Cookie == newAccount.Cookie))
                {
                    _accounts.Add(newAccount);
                }
            }
        }

        internal void AddAccounts(IEnumerable<Account> newAccounts)
        {
            lock (_lock)
            {
                _accounts.AddRange(newAccounts);
            }
        }

        internal void ClearAccounts()
        {
            lock (_lock)
            {
                _accounts.Clear();
                _selectedAccountIndices.Clear();
                _verificationResults.Clear();
            }
        }

        internal void SortAccountsByUsername()
        {
            lock (_lock)
            {
                var selectedUserIds = GetSelectedAccounts().Select(a => a.UserId).ToHashSet();

                _accounts.Sort((a, b) => string.Compare(a.Username, b.Username, StringComparison.OrdinalIgnoreCase));

                _selectedAccountIndices.Clear();
                for (int i = 0; i < _accounts.Count; i++)
                {
                    if (selectedUserIds.Contains(_accounts[i].UserId))
                    {
                        _selectedAccountIndices.Add(i);
                    }
                }
                SortAndDeduplicateSelection();
            }
        }

        internal void SortAndDeduplicateSelection()
        {
            lock (_lock)
            {
                var distinctSorted = _selectedAccountIndices.Distinct().OrderBy(i => i).ToList();
                _selectedAccountIndices.Clear();
                _selectedAccountIndices.AddRange(distinctSorted);
            }
        }

        public VerificationStatus GetVerificationStatus(long userId)
        {
            lock (_lock)
            {
                if (_verificationResults.TryGetValue(userId, out var result))
                {
                    return result.Status;
                }
                return VerificationStatus.NotChecked;
            }
        }

        public void SetVerificationStatus(long userId, VerificationStatus status, string details)
        {
            lock (_lock)
            {
                _verificationResults[userId] = (status, details ?? string.Empty);
            }
        }

        public void ClearVerificationResults()
        {
            lock (_lock)
            {
                _verificationResults.Clear();
            }
        }

        public int GetVerificationResultsCount()
        {
            lock (_lock)
            {
                return _verificationResults.Count;
            }
        }
    }
}