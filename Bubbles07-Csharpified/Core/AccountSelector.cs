using Continuance.CLI;
using Continuance.Models;
using Spectre.Console;

namespace Continuance.Core
{
    public class AccountSelector(AccountManager accountManager)
    {
        private readonly AccountManager _accountManager = accountManager;

        public void UpdateSelection(List<int> indicesToToggle)
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndicesList();
            int accountCount = _accountManager.GetAllAccounts().Count;
            int toggledOn = 0;
            int toggledOff = 0;

            foreach (int userIndex in indicesToToggle)
            {
                if (userIndex >= 1 && userIndex <= accountCount)
                {
                    int zeroBasedIndex = userIndex - 1;
                    if (selectedIndices.Remove(zeroBasedIndex))
                    {
                        toggledOff++;
                    }
                    else
                    {
                        selectedIndices.Add(zeroBasedIndex);
                        toggledOn++;
                    }
                }
                else
                {
                    Logger.LogError($"Invalid input number: '{userIndex}'. Must be between 1 and {accountCount}. Skipped.");
                }
            }

            if (toggledOn > 0 || toggledOff > 0)
                Logger.LogInfo($"Selection updated: [green]+{toggledOn}[/] selected, [red]-{toggledOff}[/] deselected.");

            _accountManager.SortAndDeduplicateSelection();
        }

        public void SelectAll()
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndicesList();
            selectedIndices.Clear();
            selectedIndices.AddRange(Enumerable.Range(0, _accountManager.GetAllAccounts().Count));
            Logger.LogInfo($"All {_accountManager.GetAllAccounts().Count} accounts selected.");
        }

        public void SelectNone()
        {
            _accountManager.GetSelectedAccountIndicesList().Clear();
            Logger.LogError("Selection Cleared. No accounts selected.");
        }

        public void SelectValid()
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndicesList();
            selectedIndices.Clear();
            selectedIndices.AddRange(_accountManager.GetAllAccounts()
                .Select((a, i) => new { Account = a, Index = i })
                .Where(x => x.Account.IsValid)
                .Select(x => x.Index));
            Logger.LogInfo($"All {selectedIndices.Count} valid accounts selected.");
        }

        public void SelectInvalid()
        {
            var selectedIndices = _accountManager.GetSelectedAccountIndicesList();
            selectedIndices.Clear();
            selectedIndices.AddRange(_accountManager.GetAllAccounts()
                .Select((a, i) => new { Account = a, Index = i })
                .Where(x => !x.Account.IsValid)
                .Select(x => x.Index));
            Logger.LogInfo($"All {selectedIndices.Count} invalid accounts selected.");
        }

        public void SelectFailedVerification()
        {
            if (_accountManager.GetVerificationResultsCount() == 0)
            {
                Logger.LogError("Verification check has not been run recently. Run the Verify action first.");
                return;
            }

            var selectedIndices = _accountManager.GetSelectedAccountIndicesList();
            selectedIndices.Clear();
            var accounts = _accountManager.GetAllAccounts();
            int count = 0;
            for (int i = 0; i < accounts.Count; i++)
            {
                var result = _accountManager.GetVerificationStatus(accounts[i].UserId);
                if (result == VerificationStatus.Failed || result == VerificationStatus.Error)
                {
                    selectedIndices.Add(i);
                    count++;
                }
            }

            if (count > 0)
                Logger.LogInfo($"Selected {count} accounts that failed or had errors in the last verification check.");
            else
                Logger.LogWarning("No accounts failed or had errors in the last verification check.");

            _accountManager.SortAndDeduplicateSelection();
        }
    }
}