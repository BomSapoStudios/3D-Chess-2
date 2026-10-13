// Push notifications (see PushNotifications.cs). Asks for permission, gets the device
// token from iOS, and remembers which game a tapped notification was about. The C# side
// polls for the token and the tapped game, so nothing is lost if they arrive before
// Unity is running.
//
// Unity's app controller only implements the remote notification callbacks when its
// Mobile Notifications package is in use, so they're added to it here at load time.

#import <UIKit/UIKit.h>
#import <UserNotifications/UserNotifications.h>
#import <objc/runtime.h>
#import "UnityAppController.h"

static NSString* pushToken = nil;
static NSString* tappedRoom = nil;
static NSString* authStatus = @"unknown";

static char* CopyString(NSString* s)
{
    const char* utf8 = (s ?: @"").UTF8String;
    char* copy = (char*)malloc(strlen(utf8) + 1);
    strcpy(copy, utf8);
    return copy;
}

static void RememberTap(NSDictionary* userInfo)
{
    // Remembered as "kind|roomCode" (the room is empty for friend requests)
    id kind = userInfo[@"kind"], room = userInfo[@"roomCode"];
    if (![kind isKindOfClass: [NSString class]]) return;
    tappedRoom = [NSString stringWithFormat: @"%@|%@", kind, [room isKindOfClass: [NSString class]] ? room : @""];
}

static void RefreshStatus(void (^then)(UNAuthorizationStatus))
{
    [[UNUserNotificationCenter currentNotificationCenter] getNotificationSettingsWithCompletionHandler:^(UNNotificationSettings* settings) {
        switch (settings.authorizationStatus)
        {
            case UNAuthorizationStatusAuthorized: authStatus = @"authorized"; break;
            case UNAuthorizationStatusProvisional: authStatus = @"authorized"; break;
            case UNAuthorizationStatusEphemeral: authStatus = @"authorized"; break;
            case UNAuthorizationStatusDenied: authStatus = @"denied"; break;
            default: authStatus = @"notDetermined"; break;
        }
        if (then) then(settings.authorizationStatus);
    }];
}

static void RegisterWithApple()
{
    dispatch_async(dispatch_get_main_queue(), ^{ [[UIApplication sharedApplication] registerForRemoteNotifications]; });
}

@interface ChessPush : NSObject <UNUserNotificationCenterDelegate>
@end

@implementation ChessPush

static ChessPush* shared = nil;

static void DidRegister(id self, SEL _cmd, UIApplication* app, NSData* token)
{
    const unsigned char* bytes = (const unsigned char*)token.bytes;
    NSMutableString* hex = [NSMutableString stringWithCapacity: token.length * 2];
    for (NSUInteger i = 0; i < token.length; i++) [hex appendFormat: @"%02x", bytes[i]];
    pushToken = hex;
}

static void DidFailToRegister(id self, SEL _cmd, UIApplication* app, NSError* error)
{
    NSLog(@"[Push] Couldn't register for notifications: %@", error.localizedDescription);
}

+ (void)load
{
    Class controller = [UnityAppController class];
    class_addMethod(controller, @selector(application:didRegisterForRemoteNotificationsWithDeviceToken:), (IMP)DidRegister, "v@:@@");
    class_addMethod(controller, @selector(application:didFailToRegisterForRemoteNotificationsWithError:), (IMP)DidFailToRegister, "v@:@@");

    // The delegate has to be in place before launch finishes to hear about a tap that opened the app
    [[NSNotificationCenter defaultCenter] addObserverForName: UIApplicationDidFinishLaunchingNotification object: nil queue: nil usingBlock:^(NSNotification* note) {
        shared = [[ChessPush alloc] init];
        [UNUserNotificationCenter currentNotificationCenter].delegate = shared;
        NSDictionary* remote = note.userInfo[UIApplicationLaunchOptionsRemoteNotificationKey];
        if (remote) RememberTap(remote);
        // Tokens can change, so register on every launch once the player has said yes
        RefreshStatus(^(UNAuthorizationStatus status) {
            if (status == UNAuthorizationStatusAuthorized || status == UNAuthorizationStatusProvisional) RegisterWithApple();
        });
    }];
}

// A notification while the app is open: show it as a banner anyway (it's about another game or a friend)
- (void)userNotificationCenter:(UNUserNotificationCenter*)center willPresentNotification:(UNNotification*)notification withCompletionHandler:(void (^)(UNNotificationPresentationOptions))completionHandler
{
    completionHandler(UNNotificationPresentationOptionBanner | UNNotificationPresentationOptionList | UNNotificationPresentationOptionSound);
}

- (void)userNotificationCenter:(UNUserNotificationCenter*)center didReceiveNotificationResponse:(UNNotificationResponse*)response withCompletionHandler:(void (^)(void))completionHandler
{
    RememberTap(response.notification.request.content.userInfo);
    completionHandler();
}

@end

// Development builds are signed for Apple's sandbox push servers; App Store and TestFlight
// builds (which carry no embedded provisioning profile, or a production one) use production.
static bool IsSandbox()
{
    NSString* path = [[NSBundle mainBundle] pathForResource: @"embedded" ofType: @"mobileprovision"];
    if (!path) return false;
    NSString* profile = [[NSString alloc] initWithData: [NSData dataWithContentsOfFile: path] encoding: NSISOLatin1StringEncoding];
    NSRange key = [profile rangeOfString: @"<key>aps-environment</key>"];
    if (key.location == NSNotFound) return false;
    NSString* rest = [profile substringFromIndex: key.location + key.length];
    NSRange end = [rest rangeOfString: @"</string>"];
    return end.location != NSNotFound && [[rest substringToIndex: end.location] containsString: @"development"];
}

extern "C"
{
    /// Ask the player for permission (only shows iOS's prompt the first time); registers if they agree
    void _PushRequest()
    {
        [[UNUserNotificationCenter currentNotificationCenter] requestAuthorizationWithOptions: (UNAuthorizationOptionAlert | UNAuthorizationOptionSound | UNAuthorizationOptionBadge)
                                                                            completionHandler:^(BOOL granted, NSError* error) {
            RefreshStatus(nil);
            if (granted) RegisterWithApple();
        }];
    }

    /// Re-check permission (the player may have changed it in Settings); result shows up in _PushStatus
    void _PushRefreshStatus() { RefreshStatus(nil); }

    /// authorized | denied | notDetermined | unknown
    char* _PushStatus() { return CopyString(authStatus); }

    /// The device token as hex, once iOS has given it out; empty until then
    char* _PushToken() { return CopyString(pushToken); }

    bool _PushIsSandbox() { return IsSandbox(); }

    /// The tapped notification as "kind|roomCode", or null if nothing was tapped. Clears it.
    char* _PushTakeTappedRoom()
    {
        if (tappedRoom == nil) return NULL;
        char* result = CopyString(tappedRoom);
        tappedRoom = nil;
        return result;
    }

    /// Open this app's page in the Settings app, so the player can turn notifications back on
    void _PushOpenSettings()
    {
        dispatch_async(dispatch_get_main_queue(), ^{
            NSURL* url = [NSURL URLWithString: UIApplicationOpenNotificationSettingsURLString];
            [[UIApplication sharedApplication] openURL: url options: @{} completionHandler: nil];
        });
    }
}
