using System;

namespace Simitone.Conversations
{
    public enum VirtualLotPhase
    {
        Idle,
        Entering,
        Active,
        Exiting
    }

    // Pure-C# cinematic transition controller for full-screen "Virtual Pocket Lot" work venues
    // (Police Academy Grounds, Beat Patrol Street, Precinct Desk, etc.).
    // Handles smooth fade-from-black entry, translucent arrival location banner, and fade-to-black return
    // back to the player's paused home lot without touching real TS1 neighborhood lot state.
    public sealed class VirtualLotTransition
    {
        public const float DefaultEnterFadeSeconds = 0.42f;
        public const float DefaultBannerHoldSeconds = 1.15f;
        public const float DefaultBannerFadeSeconds = 0.45f;
        public const float DefaultExitFadeSeconds = 0.34f;

        public VirtualLotPhase Phase { get; private set; }
        public string LocationTitle { get; private set; }
        public string LocationSubtitle { get; private set; }
        public bool ExitApplyRewards { get; private set; }

        float phaseElapsed;
        float bannerElapsed;
        float enterFadeSec = DefaultEnterFadeSeconds;
        float bannerHoldSec = DefaultBannerHoldSeconds;
        float bannerFadeSec = DefaultBannerFadeSeconds;
        float exitFadeSec = DefaultExitFadeSeconds;

        // 0 = fully clear scene, 1 = solid black transition veil
        public float BlackVeilAlpha
        {
            get
            {
                if (Phase == VirtualLotPhase.Entering)
                {
                    float u = enterFadeSec > 0.001f ? Clamp01(phaseElapsed / enterFadeSec) : 1f;
                    // Smooth ease-out fade from black into the live virtual lot
                    float inv = 1f - u;
                    return inv * inv;
                }
                if (Phase == VirtualLotPhase.Exiting)
                {
                    float u = exitFadeSec > 0.001f ? Clamp01(phaseElapsed / exitFadeSec) : 1f;
                    return u * u;
                }
                return 0f;
            }
        }

        // 0..1 opacity of the cinematic arrival location card
        public float BannerAlpha
        {
            get
            {
                if (Phase == VirtualLotPhase.Idle || Phase == VirtualLotPhase.Exiting)
                    return 0f;

                float total = enterFadeSec + bannerHoldSec + bannerFadeSec;
                if (bannerElapsed >= total) return 0f;

                if (bannerElapsed < enterFadeSec * 0.7f)
                {
                    float d = Math.Max(0.05f, enterFadeSec * 0.7f);
                    return Clamp01(bannerElapsed / d);
                }

                float fadeStart = enterFadeSec + bannerHoldSec;
                if (bannerElapsed <= fadeStart)
                    return 1f;

                float f = bannerFadeSec > 0.001f ? Clamp01((bannerElapsed - fadeStart) / bannerFadeSec) : 1f;
                return 1f - f;
            }
        }

        public bool IsExiting => Phase == VirtualLotPhase.Exiting;

        public void BeginEnter(
            string locationTitle,
            string locationSubtitle,
            float enterFadeSeconds = DefaultEnterFadeSeconds,
            float bannerHoldSeconds = DefaultBannerHoldSeconds,
            float bannerFadeSeconds = DefaultBannerFadeSeconds)
        {
            LocationTitle = locationTitle ?? "";
            LocationSubtitle = locationSubtitle ?? "";
            enterFadeSec = Math.Max(0.05f, enterFadeSeconds);
            bannerHoldSec = Math.Max(0f, bannerHoldSeconds);
            bannerFadeSec = Math.Max(0.05f, bannerFadeSeconds);
            Phase = VirtualLotPhase.Entering;
            phaseElapsed = 0f;
            bannerElapsed = 0f;
            ExitApplyRewards = false;
        }

        public void BeginExit(bool applyRewards, float exitFadeSeconds = DefaultExitFadeSeconds)
        {
            if (Phase == VirtualLotPhase.Exiting) return;
            ExitApplyRewards = applyRewards;
            exitFadeSec = Math.Max(0.05f, exitFadeSeconds);
            Phase = VirtualLotPhase.Exiting;
            phaseElapsed = 0f;
        }

        public void Reset()
        {
            Phase = VirtualLotPhase.Idle;
            phaseElapsed = 0f;
            bannerElapsed = 0f;
            ExitApplyRewards = false;
        }

        // Advances transition timers by dt. Returns true on the exact tick when Exiting completes.
        public bool Tick(float dt)
        {
            if (dt < 0f) dt = 0f;
            if (Phase == VirtualLotPhase.Idle) return false;

            phaseElapsed += dt;
            if (Phase == VirtualLotPhase.Entering)
            {
                bannerElapsed += dt;
                if (phaseElapsed >= enterFadeSec)
                {
                    Phase = VirtualLotPhase.Active;
                    phaseElapsed = 0f;
                }
                return false;
            }

            if (Phase == VirtualLotPhase.Active)
            {
                bannerElapsed += dt;
                return false;
            }

            if (Phase == VirtualLotPhase.Exiting)
            {
                if (phaseElapsed >= exitFadeSec)
                {
                    Phase = VirtualLotPhase.Idle;
                    return true;
                }
            }

            return false;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
