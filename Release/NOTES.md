# DeskPages 0.8.40 Alpha 1 — bounded commands and lighter snapshot updates

Windows 11 x64 engineering preview with measured component repairs. This is not a claim that all responsiveness issues are resolved or that physical input-to-transition latency is below 50ms.

## Changes

- Reserve command queue capacity atomically so concurrent submissions cannot exceed its three-command limit. Release reservations on dequeue or enqueue failure.
- Reuse background file metadata for unchanged desktop items, refresh notified paths, and fully rescan after membership changes or watcher recovery. Remove metadata for deleted catalog entries and preserve selective page invalidation.
- Remove the redundant old-manifest write during target visibility preparation. Source membership and the transition journal remain durable; the verified target still commits at the existing final commit point.

## Measured component results

- Queue: 1,000 rounds with 16 simultaneous senders exceeded capacity 158 times before and zero times after. With two BelowNormal CPU workers and 64MiB of test memory: 238 violations before and zero after. No queue latency speedup is claimed; measured P99 increased in the initial paired trials.
- Snapshot catalog: 100 notifications against 1,000 temporary files reduced metadata probes from 100,000 to 100. Mean background update time changed from 82.14ms to 5.16ms, P95 from 111.54ms to 7.87ms, and P99 from 121.55ms to 9.53ms. Three reverse-order repeat trials confirmed the reduction. These are catalog completion times, not navigation timings.
- Isolated target preparation, 100 calls: redundant manifest writes changed from 100 to zero, mean from 107.01ms to 86.11ms and P99 from 310.98ms to 223.10ms. These are preparation timings, not whole-navigation timings.

## Validation and remaining scope

Native builds and targeted queue, catalog, missed-notification, posted-message dispatch, animation sequence, visibility, drag recovery/drop and child-process crash-recovery tests passed locally. The local broad run failed the existing global taskbar shortcut-registration fixture; its logic is unchanged. Other following suites passed separately. Publication requires the repository's GitHub Windows regression/stress and Microsoft Defender scan gates.

Physical 100/1,000 desktop navigation loops, hours-long endurance, fully stressed RAM/CPU, comprehensive resource-leak measurements and the 50ms input-to-transition objective remain unverified. The controlled load test is not full resource exhaustion. The Alpha installer is not Authenticode-signed.
