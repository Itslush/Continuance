using Continuance.CLI;
using Continuance.Models;
using Continuance.Roblox.Services;
using Newtonsoft.Json;

namespace Continuance.Core
{
    public class BackgroundKeepAliveService(AccountManager accountManager, AuthenticationService authService)
    {
        private readonly AccountManager _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        private readonly AuthenticationService _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        private CancellationTokenSource? _cts;
        private Task? _serviceTask;
        private const string AccountsFilePath = "ContinuanceAccounts.json";

        public void Start()
        {
            if (_serviceTask != null && !_serviceTask.IsCompleted) return;

            _cts = new CancellationTokenSource();
            _serviceTask = Task.Run(() => RunLoopAsync(_cts.Token));
            Logger.LogInfo($"Background Keep-Alive Service started (Interval: {AppConfig.BackgroundRefreshIntervalMinutes} min).");
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                if (_serviceTask != null)
                {
                    try { await _serviceTask; } catch (OperationCanceledException) { }
                }
                _cts.Dispose();
                _cts = null;
            }
        }

        private async Task RunLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    int delayMinutes = AppConfig.BackgroundRefreshIntervalMinutes;
                    for (int i = 0; i < delayMinutes * 60; i++)
                    {
                        if (token.IsCancellationRequested) return;
                        await Task.Delay(1000, token);
                    }

                    if (token.IsCancellationRequested) return;

                    await PerformKeepAliveCycle(token);
                }
                catch (Exception ex)
                {
                    Logger.LogDefault($"[Background Service Error] {ex.Message}");
                }
            }
        }

        private async Task PerformKeepAliveCycle(CancellationToken token)
        {
            var accounts = _accountManager.GetAllAccounts();
            var validAccounts = accounts.Where(a => a.IsValid).ToList();

            if (validAccounts.Count == 0) return;

            bool saveNeeded = false;

            foreach (var acc in validAccounts)
            {
                if (token.IsCancellationRequested) return;

                var (isValid, _, _) = await AuthenticationService.ValidateCookieAsync(acc.Cookie, suppressOutput: true);

                if (isValid)
                {
                    string oldToken = acc.XcsrfToken;
                    bool refreshed = await AuthenticationService.RefreshXCSRFTokenIfNeededAsync(acc);

                    if (refreshed && acc.XcsrfToken != oldToken)
                    {
                        saveNeeded = true;
                    }
                }
                else
                {
                    acc.IsValid = false;
                    acc.XcsrfToken = "";
                    Logger.LogError($"[Background Monitor] Alert: Session for {acc.Username} expired/invalidated.");
                    saveNeeded = true;
                }

                await Task.Delay(1000, token);
            }

            if (saveNeeded)
            {
                await SaveAccountsToDiskAsync();
            }
        }

        private async Task SaveAccountsToDiskAsync()
        {
            try
            {
                var accounts = _accountManager.GetAllAccounts();
                string json = JsonConvert.SerializeObject(accounts, Formatting.Indented);
                await File.WriteAllTextAsync(AccountsFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Background Monitor] Failed to auto-save accounts: {ex.Message}");
            }
        }
    }
}