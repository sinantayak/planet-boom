using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

// UGS foundation. This service owns initialization/authentication; gameplay
// persistence waits on its ReadyTask in PlayerDataPersistenceManager.
public sealed class UnityGamingServicesManager : MonoBehaviour
{
    public enum UgsState
    {
        NotStarted,
        InitializingServices,
        Authenticating,
        Ready,
        Failed,
        SignedOut,
        SessionExpired
    }

    public static UnityGamingServicesManager Instance { get; private set; }

    public static event Action<UgsState> StateChanged;
    public static event Action<string> AuthenticationReady;
    public static event Action<Exception> InitializationFailed;

    [Header("Runtime Status (Play Mode)")]
    [SerializeField] private UgsState currentState = UgsState.NotStarted;
    [SerializeField] private string authenticatedPlayerId = string.Empty;

    public UgsState State => currentState;
    public bool IsReady => State == UgsState.Ready;
    public bool IsAuthenticated =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;
    public string PlayerId => IsAuthenticated ? authenticatedPlayerId : string.Empty;
    public Exception LastError { get; private set; }
    public bool HadCachedSessionAtStartup { get; private set; }
    public ICloudSaveService CloudSave { get; private set; }
    public bool IsCloudSaveAvailable => IsReady && CloudSave != null;
    public Task ReadyTask => readyTaskSource.Task;

    private TaskCompletionSource<bool> readyTaskSource = NewReadyTaskSource();
    private Task initializationTask;
    private bool authenticationEventsBound;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootstrapBeforeFirstScene()
    {
        if (Instance != null)
            return;

        var serviceObject = new GameObject("UnityGamingServicesManager");
        DontDestroyOnLoad(serviceObject);
        serviceObject.AddComponent<UnityGamingServicesManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        _ = InitializeAndAuthenticateAsync();
    }

    private void OnDestroy()
    {
        UnbindAuthenticationEvents();
        if (Instance == this)
            Instance = null;
    }

    public Task InitializeAndAuthenticateAsync()
    {
        if (IsReady)
            return Task.CompletedTask;
        if (initializationTask != null && !initializationTask.IsCompleted)
            return initializationTask;

        if (State == UgsState.Failed || State == UgsState.SignedOut || State == UgsState.SessionExpired)
            readyTaskSource = NewReadyTaskSource();

        initializationTask = RunInitializationAsync();
        return initializationTask;
    }

    // Future loading/retry UI can call this without knowing UGS internals.
    public Task RetryAsync()
    {
        return InitializeAndAuthenticateAsync();
    }

    private async Task RunInitializationAsync()
    {
        LastError = null;
        try
        {
            SetState(UgsState.InitializingServices);
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();

            BindAuthenticationEvents();
            SetState(UgsState.Authenticating);

            IAuthenticationService authentication = AuthenticationService.Instance;
            HadCachedSessionAtStartup = authentication.SessionTokenExists;
            if (!authentication.IsSignedIn)
                await authentication.SignInAnonymouslyAsync();

            // Accessing the instance after Core initialization verifies that
            // Cloud Save is registered and ready for the next persistence phase.
            CloudSave = CloudSaveService.Instance;
            authenticatedPlayerId = authentication.PlayerId;
            SetState(UgsState.Ready);
            readyTaskSource.TrySetResult(true);

            Debug.Log($"UGS ready. Anonymous authentication succeeded. " +
                      $"PlayerId={authentication.PlayerId}, " +
                      $"cachedSession={HadCachedSessionAtStartup}.", this);
            AuthenticationReady?.Invoke(authentication.PlayerId);
        }
        catch (Exception exception)
        {
            CloudSave = null;
            authenticatedPlayerId = string.Empty;
            LastError = exception;
            SetState(UgsState.Failed);
            readyTaskSource.TrySetException(exception);
            Debug.LogError($"UGS initialization/authentication failed: " +
                           $"{exception.GetType().Name}: {exception.Message}", this);
            InitializationFailed?.Invoke(exception);
        }
    }

    private void BindAuthenticationEvents()
    {
        if (authenticationEventsBound)
            return;
        IAuthenticationService authentication = AuthenticationService.Instance;
        authentication.SignedOut += HandleSignedOut;
        authentication.Expired += HandleSessionExpired;
        authenticationEventsBound = true;
    }

    private void UnbindAuthenticationEvents()
    {
        if (!authenticationEventsBound || UnityServices.State != ServicesInitializationState.Initialized)
            return;
        IAuthenticationService authentication = AuthenticationService.Instance;
        authentication.SignedOut -= HandleSignedOut;
        authentication.Expired -= HandleSessionExpired;
        authenticationEventsBound = false;
    }

    private void HandleSignedOut()
    {
        CloudSave = null;
        authenticatedPlayerId = string.Empty;
        SetState(UgsState.SignedOut);
    }

    private void HandleSessionExpired()
    {
        CloudSave = null;
        authenticatedPlayerId = string.Empty;
        SetState(UgsState.SessionExpired);
        Debug.LogWarning("UGS authentication session expired; RetryAsync is available.", this);
    }

    private void SetState(UgsState newState)
    {
        if (currentState == newState)
            return;
        currentState = newState;
        StateChanged?.Invoke(newState);
    }

    private static TaskCompletionSource<bool> NewReadyTaskSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
