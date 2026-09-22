using System;

public sealed partial class WebFightBootstrap
{
    private static void ResetUltimateChain(FighterState state)
    {
        state.PerfectGuardStreak = state.UltimateComboTicks = state.UltimateStage = 0;
    }

    private static void RegisterPerfectGuard(FighterState state)
    {
        if (state.PerfectGuardStreak == 0 || state.UltimateComboTicks <= 0)
        {
            ResetUltimateChain(state);
            state.UltimateComboTicks = 240;
        }
        state.PerfectGuardStreak = Math.Min(4, state.PerfectGuardStreak + 1);
    }

    private static void TickUltimateTimers(FighterState state)
    {
        if (state.UltimateComboTicks > 0 && --state.UltimateComboTicks == 0) ResetUltimateChain(state);
        state.UltimateReadyTicks = Math.Max(0, state.UltimateReadyTicks - 1);
        state.SpeedBoostTicks = Math.Max(0, state.SpeedBoostTicks - 1);
    }
}
