// Saves an image file to the iPhone's Photos app.
#import <UIKit/UIKit.h>

extern "C" void _OurTrip_SaveToPhotos(const char* path)
{
    if (path == NULL) return;
    NSString* file = [NSString stringWithUTF8String:path];
    UIImage* image = [UIImage imageWithContentsOfFile:file];
    if (image != nil)
        UIImageWriteToSavedPhotosAlbum(image, nil, nil, nil);
    else
        NSLog(@"[OurTrip] Could not load photo at %@", file);
}
