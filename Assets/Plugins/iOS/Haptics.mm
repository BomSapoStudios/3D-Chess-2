// Haptic taps and buzzes (iOS only; see Haptics.cs, which checks the player's Haptics switch)
#import <UIKit/UIKit.h>

extern "C" void _HapticSelection()
{
    dispatch_async(dispatch_get_main_queue(), ^{
        static UISelectionFeedbackGenerator *generator = nil;
        if (generator == nil) generator = [[UISelectionFeedbackGenerator alloc] init];
        [generator selectionChanged];
        [generator prepare];
    });
}

// style: 0 light, 1 medium, 2 heavy
extern "C" void _HapticImpact(int style)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        static UIImpactFeedbackGenerator *generators[3] = { nil, nil, nil };
        int i = style < 0 ? 0 : style > 2 ? 2 : style;
        if (generators[i] == nil)
        {
            UIImpactFeedbackStyle styles[3] = { UIImpactFeedbackStyleLight, UIImpactFeedbackStyleMedium, UIImpactFeedbackStyleHeavy };
            generators[i] = [[UIImpactFeedbackGenerator alloc] initWithStyle:styles[i]];
        }
        [generators[i] impactOccurred];
        [generators[i] prepare];
    });
}

// type: 0 success, 1 warning, 2 error
extern "C" void _HapticNotify(int type)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        static UINotificationFeedbackGenerator *generator = nil;
        if (generator == nil) generator = [[UINotificationFeedbackGenerator alloc] init];
        UINotificationFeedbackType types[3] = { UINotificationFeedbackTypeSuccess, UINotificationFeedbackTypeWarning, UINotificationFeedbackTypeError };
        [generator notificationOccurred:types[type < 0 ? 0 : type > 2 ? 2 : type]];
        [generator prepare];
    });
}
