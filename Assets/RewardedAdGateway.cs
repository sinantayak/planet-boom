using System;

// SDK-neutral rewarded-ad boundary. A future AdMob / LevelPlay adapter
// registers one provider; gameplay UI never talks to an SDK directly.
public interface IRewardedAdProvider
{
    bool IsRewardedAdAvailable { get; }
    void ShowRewardedAd(Action onRewardEarned, Action onClosedWithoutReward);
}

public static class RewardedAdGateway
{
    private static IRewardedAdProvider provider;

    public static void RegisterProvider(IRewardedAdProvider rewardedProvider) =>
        provider = rewardedProvider;

    public static void UnregisterProvider(IRewardedAdProvider rewardedProvider)
    {
        if (ReferenceEquals(provider, rewardedProvider))
            provider = null;
    }

    public static bool IsAvailable(bool allowDevelopmentSimulation)
    {
        if (provider != null && provider.IsRewardedAdAvailable)
            return true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return allowDevelopmentSimulation;
#else
        return false;
#endif
    }

    public static void Show(bool allowDevelopmentSimulation,
        Action onRewardEarned, Action onClosedWithoutReward)
    {
        if (provider != null && provider.IsRewardedAdAvailable)
        {
            bool resolved = false;
            provider.ShowRewardedAd(
                () =>
                {
                    if (resolved) return;
                    resolved = true;
                    onRewardEarned?.Invoke();
                },
                () =>
                {
                    if (resolved) return;
                    resolved = true;
                    onClosedWithoutReward?.Invoke();
                });
            return;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (allowDevelopmentSimulation)
        {
            onRewardEarned?.Invoke();
            return;
        }
#endif
        onClosedWithoutReward?.Invoke();
    }
}
