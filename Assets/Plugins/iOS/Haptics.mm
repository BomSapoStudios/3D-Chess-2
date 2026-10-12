// Light haptic taps for UI changes (iOS only; see Haptics.cs)
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
