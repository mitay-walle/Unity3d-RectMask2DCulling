## RectMask2DCulling
Custom RectMask2D, that allow to disable Culling / Softness
## Problem
Built-in RectMask2D has overhead, that grow linearly with count of active Child UI.Graphic, main reason for this - Culling. When all this UI.Graphic is always visible (![Pooled ScrollRectList](https://github.com/disas69/Unity-Pooled-Scroll-List) or ![Optimized ScrollView Adapter](https://assetstore.unity.com/packages/tools/gui/optimized-scrollview-adapter-68436) used, for example) - Culling is useless

And you can't disable it

## Solution
Inherite from RectMask2D and add flags to disable Culling
## Performance
- Profiled at Honor 10X Lite
- 50 child TextMeshPro
- 400 child other various UI.Graphic (UI.Image, UI.RawImage, UI.Text)

- RectMask2D
![](https://github.com/mitay-walle/Unity3d-RectMask2DCulling/blob/main/RectMask2D_profiling.jpg)

- RectMask2DCulling , Culling and Softness disabled
![](https://github.com/mitay-walle/Unity3d-RectMask2DCulling/blob/main/RectMask2DCulling_profiling.jpg)

- RectMask2DCulling use reflection to get private properties of parent RectMask2D class, and it add performance overhead
![](https://github.com/mitay-walle/Unity3d-RectMask2DCulling/blob/main/RectMask2DCulling_performance_scheme.jpg)
