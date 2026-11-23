// Makes the game's sound play on iPhone even when the silent switch is on,
// and lets it play together with a WeChat/Telegram call or other apps' audio.
#import <AVFoundation/AVFoundation.h>

extern "C" void _OurTrip_SetPlaybackAudio()
{
    AVAudioSession* session = [AVAudioSession sharedInstance];
    NSError* error = nil;
    [session setCategory:AVAudioSessionCategoryPlayback
             withOptions:AVAudioSessionCategoryOptionMixWithOthers
                   error:&error];
    if (error) NSLog(@"[OurTrip] setCategory failed: %@", error);
    [session setActive:YES error:&error];
    if (error) NSLog(@"[OurTrip] setActive failed: %@", error);
}
