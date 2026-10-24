#pragma once
inline void ForegroundServiceThread(){
 SetThreadPriority(GetCurrentThread(),THREAD_PRIORITY_ABOVE_NORMAL);
 MEMORY_PRIORITY_INFORMATION memory{MEMORY_PRIORITY_NORMAL};SetThreadInformation(GetCurrentThread(),ThreadMemoryPriority,&memory,sizeof(memory));
 THREAD_POWER_THROTTLING_STATE power{};power.Version=THREAD_POWER_THROTTLING_CURRENT_VERSION;power.ControlMask=THREAD_POWER_THROTTLING_EXECUTION_SPEED;
 SetThreadInformation(GetCurrentThread(),ThreadPowerThrottling,&power,sizeof(power));
}
