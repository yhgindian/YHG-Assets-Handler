#pragma once
#if defined(_MSC_VER)
#define EXPORT extern "C" __declspec(dllexport)
#elif defined(__GNUC__)
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

typedef void(__stdcall* ProgressCallback)(float progress, const char* status);