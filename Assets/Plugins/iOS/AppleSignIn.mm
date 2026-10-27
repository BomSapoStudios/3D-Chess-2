// Sign in with Apple for the iPhone build. Asks for no name or email: we only need
// Apple's stable user id, which the game server reads from the identity token.
// Results go to the Unity GameObject named by the caller via UnitySendMessage:
//   OnAppleSignIn(identityToken)  or  OnAppleSignInError(message)

#import <AuthenticationServices/AuthenticationServices.h>
#import <UIKit/UIKit.h>

extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

@interface AppleSignInHelper : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property (nonatomic, copy) NSString* receiver;
@property (nonatomic, copy) NSString* nonce;
@end

static AppleSignInHelper* current;

@implementation AppleSignInHelper

- (void)start
{
    ASAuthorizationAppleIDRequest* request = [[ASAuthorizationAppleIDProvider new] createRequest];
    request.requestedScopes = @[];
    // SHA-256 of a one-time value the app keeps; Apple puts it in the token, so the server can
    // tell this token was asked for by this device and not copied from somewhere else
    if (self.nonce.length > 0) request.nonce = self.nonce;
    ASAuthorizationController* controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[request]];
    controller.delegate = self;
    controller.presentationContextProvider = self;
    [controller performRequests];
}

- (void)finish:(const char*)method message:(NSString*)message
{
    UnitySendMessage(self.receiver.UTF8String, method, (message ?: @"").UTF8String);
    current = nil;
}

- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithAuthorization:(ASAuthorization*)authorization
{
    ASAuthorizationAppleIDCredential* credential = (ASAuthorizationAppleIDCredential*)authorization.credential;
    NSString* token = credential.identityToken ? [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding] : nil;
    if (token.length > 0) [self finish:"OnAppleSignIn" message:token];
    else [self finish:"OnAppleSignInError" message:@"Apple didn't return a sign-in token"];
}

- (void)authorizationController:(ASAuthorizationController*)controller didCompleteWithError:(NSError*)error
{
    NSString* message = error.code == ASAuthorizationErrorCanceled ? @"cancelled"
        : error.code == ASAuthorizationErrorUnknown ? @"Sign in with Apple isn't switched on in this build yet"
        : error.localizedDescription;
    [self finish:"OnAppleSignInError" message:message];
}

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController*)controller
{
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (![scene isKindOfClass:UIWindowScene.class]) continue;
        for (UIWindow* window in ((UIWindowScene*)scene).windows)
            if (window.isKeyWindow) return window;
    }
    return UIApplication.sharedApplication.windows.firstObject;
}

@end

extern "C" void AppleSignIn_Start(const char* receiver, const char* nonce)
{
    current = [AppleSignInHelper new];
    current.receiver = [NSString stringWithUTF8String:receiver];
    current.nonce = nonce ? [NSString stringWithUTF8String:nonce] : nil;
    [current start];
}
