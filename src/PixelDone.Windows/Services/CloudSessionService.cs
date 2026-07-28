using PixelDone.Core;
using PixelDone.Infrastructure;

namespace PixelDone.Windows.Services;

public sealed record CloudState(
    bool IsSignedIn,
    string? Account,
    string Message,
    SyncSummary? Summary = null,
    IReadOnlyList<SyncConflict>? Conflicts = null);

public sealed class CloudSessionService : IDisposable
{
    private readonly SupabaseClient _client;
    private readonly SyncEngine _syncEngine;
    private readonly WindowsCredentialStore _credentialStore;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private CancellationTokenSource? _realtimeCancellation;
    private AuthSession? _session;
    private bool _disposed;

    public event EventHandler<CloudState>? StateChanged;

    public CloudState State { get; private set; } =
        new(false, null, "CLOUD READY · SIGN IN OR CREATE AN ACCOUNT");

    public CloudSessionService(
        SupabaseClient client,
        SyncEngine syncEngine,
        WindowsCredentialStore credentialStore)
    {
        _client = client;
        _syncEngine = syncEngine;
        _credentialStore = credentialStore;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _credentialStore.LoadAsync();
        if (saved is null)
        {
            Publish(State);
            return;
        }

        try
        {
            var session = await _client.RefreshIfNeededAsync(
                saved,
                cancellationToken: cancellationToken);
            await AcceptSessionAsync(session);
            await SyncNowAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await _credentialStore.ClearAsync();
            _session = null;
            Publish(new CloudState(false, null, $"CLOUD SESSION ERROR · {exception.Message}"));
        }
    }

    public async Task SignInAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        Publish(new CloudState(false, null, "SIGNING IN…"));
        var session = await _client.SignInAsync(email, password, cancellationToken);
        await AcceptSessionAsync(session);
        await SyncNowAsync(cancellationToken);
    }

    public async Task SignUpAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        Publish(new CloudState(false, null, "CREATING ACCOUNT…"));
        var session = await _client.SignUpAsync(email, password, cancellationToken);
        await AcceptSessionAsync(session);
        await SyncNowAsync(cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var session = _session;
        _session = null;
        _realtimeCancellation?.Cancel();
        if (session is not null)
        {
            await _client.SignOutAsync(session, cancellationToken);
        }

        await _credentialStore.ClearAsync();
        Publish(new CloudState(false, null, "SIGNED OUT · LOCAL WORKSPACE REMAINS AVAILABLE"));
    }

    public async Task ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Sign in before changing the password.");
        }

        await _client.ChangePasswordAsync(
            _session,
            currentPassword,
            newPassword,
            cancellationToken);
        Publish(new CloudState(true, Account(_session), "PASSWORD CHANGED"));
    }

    public void RequestSync()
    {
        if (_session is not null)
        {
            _ = SyncNowBestEffortAsync();
        }
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            return;
        }

        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var session = await _client.RefreshIfNeededAsync(
                _session,
                cancellationToken: cancellationToken);
            await _credentialStore.SaveAsync(session);
            _session = session;
            Publish(new CloudState(true, Account(session), "SYNCING…"));
            var summary = await _syncEngine.SyncAsync(session, cancellationToken);
            var conflicts = await _syncEngine.ListConflictsAsync(cancellationToken);
            Publish(
                new CloudState(
                    true,
                    Account(session),
                    $"SYNCED · PULLED {summary.PulledRecords} · " +
                    $"PUSHED {summary.PushedRecords} · CURSOR {summary.ServerVersion}",
                    summary,
                    conflicts));
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task ResolveConflictAsync(
        string conflictId,
        ConflictChoice choice,
        CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Sign in before resolving cloud conflicts.");
        }

        await _syncEngine.ResolveConflictAsync(
            _session,
            conflictId,
            choice,
            cancellationToken);
        await SyncNowAsync(cancellationToken);
    }

    private async Task AcceptSessionAsync(AuthSession session)
    {
        await _credentialStore.SaveAsync(session);
        _session = session;
        Publish(new CloudState(true, Account(session), $"SIGNED IN · {Account(session)}"));
        StartRealtime();
    }

    private void StartRealtime()
    {
        _realtimeCancellation?.Cancel();
        _realtimeCancellation?.Dispose();
        _realtimeCancellation = new CancellationTokenSource();
        var token = _realtimeCancellation.Token;
        _ = Task.Run(
            async () =>
            {
                while (!token.IsCancellationRequested && _session is { } session)
                {
                    try
                    {
                        await _client.ListenForInvalidationsAsync(
                            session,
                            async () => await SyncNowBestEffortAsync(),
                            token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception exception)
                    {
                        Publish(
                            new CloudState(
                                true,
                                Account(session),
                                $"REALTIME · {exception.Message}"));
                        await Task.Delay(TimeSpan.FromSeconds(10), token);
                    }
                }
            },
            token);
    }

    private async Task SyncNowBestEffortAsync()
    {
        try
        {
            await SyncNowAsync();
        }
        catch (Exception exception)
        {
            Publish(
                new CloudState(
                    _session is not null,
                    _session is null ? null : Account(_session),
                    $"CLOUD ERROR · {exception.Message}"));
        }
    }

    private void Publish(CloudState state)
    {
        State = state;
        if (_uiContext is null)
        {
            StateChanged?.Invoke(this, state);
            return;
        }

        _uiContext.Post(_ => StateChanged?.Invoke(this, state), null);
    }

    private static string Account(AuthSession session) =>
        session.Email ?? session.UserId;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _realtimeCancellation?.Cancel();
        _realtimeCancellation?.Dispose();
        _syncGate.Dispose();
    }
}
