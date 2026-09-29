#include <Foundation/Basics.h>

#if PL_ENABLED(PL_PLATFORM_ANDROID)
#  include <cstdlib>
#  define calloc(count, size) static_cast<struct android_app*>(std::calloc((count), (size)))
#  include <android_native_app_glue.c>
#  undef calloc
#endif
