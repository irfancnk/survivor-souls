// Light impact haptics for Survivor Souls (KAN-12). Called from SurvivorSouls.Core.Haptics through DllImport("__Internal").
// Unity runs the player loop on the iOS main thread, so UIKit calls here are safe.
// Devices without a Taptic Engine and users with System Haptics off get silence from UIKit, no error.

#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *s_light = nil;

static UIImpactFeedbackGenerator *SSHaptics_Generator(void)
{
    if (s_light == nil)
        s_light = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
    return s_light;
}

extern "C"
{
    void SSHaptics_Prepare(void)
    {
        [SSHaptics_Generator() prepare];
    }

    void SSHaptics_ImpactLight(void)
    {
        UIImpactFeedbackGenerator *generator = SSHaptics_Generator();
        [generator impactOccurred];
        // Keep the Taptic Engine warm for the next kill (prepare lasts a few seconds), so the next tap has no latency.
        [generator prepare];
    }
}
