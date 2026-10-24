// Game Center for the iPhone build: sign in, post leaderboard scores, report achievements
// and open Apple's Game Center screen. Our server keeps the real ratings; this only copies
// them to Apple. Results go to the Unity GameObject named by the caller via UnitySendMessage:
//   OnGameCenterIdentity(json)  once signed in, with Apple's identity signature for the server
//   OnGameCenterUnavailable(message)  when the player isn't signed in to Game Center

#import <GameKit/GameKit.h>
#import <UIKit/UIKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString* receiver;
static BOOL authenticateStarted;

static UIViewController* TopViewController()
{
    UIWindow* key = nil;
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (![scene isKindOfClass:UIWindowScene.class]) continue;
        for (UIWindow* window in ((UIWindowScene*)scene).windows)
            if (window.isKeyWindow) key = window;
    }
    UIViewController* top = (key ?: UIApplication.sharedApplication.windows.firstObject).rootViewController;
    while (top.presentedViewController) top = top.presentedViewController;
    return top;
}

static void Send(const char* method, NSString* message)
{
    if (receiver.length == 0) return;
    UnitySendMessage(receiver.UTF8String, method, (message ?: @"").UTF8String);
}

static void SendIdentity()
{
    GKLocalPlayer* player = GKLocalPlayer.localPlayer;
    [player fetchItemsForIdentityVerificationSignature:^(NSURL* publicKeyUrl, NSData* signature, NSData* salt, uint64_t timestamp, NSError* error)
    {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (error || !publicKeyUrl || !signature || !salt)
            {
                Send("OnGameCenterUnavailable", error.localizedDescription ?: @"no identity");
                return;
            }
            NSDictionary* body = @{
                @"teamPlayerId": player.teamPlayerID ?: @"",
                @"publicKeyUrl": publicKeyUrl.absoluteString,
                @"signature": [signature base64EncodedStringWithOptions:0],
                @"salt": [salt base64EncodedStringWithOptions:0],
                @"timestamp": @(timestamp),
            };
            NSData* json = [NSJSONSerialization dataWithJSONObject:body options:0 error:nil];
            Send("OnGameCenterIdentity", [[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]);
        });
    }];
}

extern "C" void _GameCenterSignIn(const char* receiverName)
{
    receiver = [NSString stringWithUTF8String:receiverName];
    if (authenticateStarted)
    {
        // Already set up: Apple calls the handler again by itself when the player signs in later
        if (GKLocalPlayer.localPlayer.isAuthenticated) SendIdentity();
        return;
    }
    authenticateStarted = YES;
    GKLocalPlayer.localPlayer.authenticateHandler = ^(UIViewController* viewController, NSError* error)
    {
        if (viewController)
        {
            // Apple's own sign-in sheet. If the player cancels a few times, Game Center stops asking.
            [TopViewController() presentViewController:viewController animated:YES completion:nil];
            return;
        }
        if (GKLocalPlayer.localPlayer.isAuthenticated) SendIdentity();
        else Send("OnGameCenterUnavailable", error.localizedDescription ?: @"not signed in");
    };
}

extern "C" bool _GameCenterIsSignedIn()
{
    return GKLocalPlayer.localPlayer.isAuthenticated;
}

extern "C" void _GameCenterPostScore(const char* leaderboardId, long long score)
{
    if (!GKLocalPlayer.localPlayer.isAuthenticated) return;
    NSString* board = [NSString stringWithUTF8String:leaderboardId];
    [GKLeaderboard submitScore:(NSInteger)score context:0 player:GKLocalPlayer.localPlayer leaderboardIDs:@[board] completionHandler:^(NSError* error)
    {
        if (error) NSLog(@"Game Center: posting to %@ failed: %@", board, error.localizedDescription);
    }];
}

extern "C" void _GameCenterReportAchievement(const char* achievementId)
{
    if (!GKLocalPlayer.localPlayer.isAuthenticated) return;
    GKAchievement* achievement = [[GKAchievement alloc] initWithIdentifier:[NSString stringWithUTF8String:achievementId]];
    achievement.percentComplete = 100;
    achievement.showsCompletionBanner = YES;
    [GKAchievement reportAchievements:@[achievement] withCompletionHandler:^(NSError* error)
    {
        if (error) NSLog(@"Game Center: achievement %@ failed: %@", achievement.identifier, error.localizedDescription);
    }];
}

@interface GameCenterCloser : NSObject <GKGameCenterControllerDelegate>
@end

@implementation GameCenterCloser
- (void)gameCenterViewControllerDidFinish:(GKGameCenterViewController*)controller
{
    [controller dismissViewControllerAnimated:YES completion:nil];
}
@end

static GameCenterCloser* closer;

extern "C" void _GameCenterShow()
{
    if (!GKLocalPlayer.localPlayer.isAuthenticated) return;
    if (!closer) closer = [GameCenterCloser new];
    GKGameCenterViewController* controller = [[GKGameCenterViewController alloc] initWithState:GKGameCenterViewControllerStateLeaderboards];
    controller.gameCenterDelegate = closer;
    [TopViewController() presentViewController:controller animated:YES completion:nil];
}
