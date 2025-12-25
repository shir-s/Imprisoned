# Time-Based Trail Aging System

## What This Does

Your trail will now **dynamically change from green to gray over time**!

- Fresh paint = Your original green/poison color
- Old paint = Fades to gray
- The change happens **smoothly and continuously** in real-time

## How It Works

```
BEFORE (your current system):
Player moves → Paint color to _PaintTex → Shader shows paint

AFTER (new system):
Player moves → Paint color to _PaintTex
            → Paint TIME to _PaintTimeTex (when this pixel was painted)
            
Shader reads both textures:
- _PaintTex: What color to show
- _PaintTimeTex: When it was painted
- Calculates: age = currentTime - paintTime
- Result: Fresh = green, Old = gray
```

## Files to Replace/Add

### Shaders (4 files):

| File | Purpose |
|------|---------|
| `PaintBrushBlit.shader` | Unchanged (or minor update) |
| `TimeBrushBlit.shader` | **NEW** - Writes time when painting |
| `TimePolygonFill.shader` | **NEW** - Writes time when filling areas |
| `PaintSurfaceWithAging.shader` | **NEW** - Displays trail with aging |

### Scripts (2 files):

| File | Changes |
|------|---------|
| `SimplePaintSurface.cs` | Added `_PaintTimeRT` texture + time tracking |
| `RenderTextureTrailPainter.cs` | Paints to both color AND time textures |

## Setup Steps

### Step 1: Import Files

Copy shaders to: `Assets/Shaders/`
- TimeBrushBlit.shader
- TimePolygonFill.shader  
- PaintSurfaceWithAging.shader

Replace scripts in: `Assets/Scripts/Painting/`
- SimplePaintSurface.cs
- RenderTextureTrailPainter.cs

### Step 2: Create Materials

1. **TimeBrush_Mat**
   - Create → Material
   - Shader: `Custom/TimeBrushBlit`

2. **TimePolygonFill_Mat**
   - Create → Material
   - Shader: `Custom/TimePolygonFill`

3. **Update your surface material**
   - Change shader to: `Custom/PaintSurfaceWithAging`
   - Configure colors:
     - Fresh Color: Your green (0.2, 0.8, 0.3)
     - Old Color: Gray (0.5, 0.5, 0.5)
     - Max Age: 10 (seconds until fully gray)

### Step 3: Configure RenderTextureTrailPainter

On your player's `RenderTextureTrailPainter` component:

1. Assign **Time Brush Blit Material** → TimeBrush_Mat
2. Assign **Time Polygon Fill Material** → TimePolygonFill_Mat

### Step 4: Configure SimplePaintSurface

On your ground/floor's `SimplePaintSurface` component:

1. Check **Enable Time Aging** ✓
2. Set **Max Age Seconds** (e.g., 10 = fully gray after 10 seconds)

## Integrating with Your PoisonShader (Shader Graph)

If you want to keep your fancy poison shader graph but add aging:

### Add these nodes to your Shader Graph:

1. **Add Property**: `_PaintTimeTex` (Texture2D)
2. **Add Property**: `_CurrentTime` (Float)
3. **Add Property**: `_MaxAge` (Float, default 0.3)

4. **Add nodes**:
```
Sample Texture 2D (_PaintTimeTex) → R channel = paintTime
Subtract: _CurrentTime - paintTime = age
Divide: age / _MaxAge = normalizedAge
Saturate: clamp to 0-1

Lerp: 
  A = Your fresh poison color
  B = Gray color (0.5, 0.5, 0.5)
  T = normalizedAge
  Out = Final color with aging!
```

### Visual node layout:
```
[_PaintTimeTex] → [Sample Texture 2D] → (R) → [Subtract] → [Divide] → [Saturate] → [Lerp T]
                                              ↑             ↑                        ↑    ↑
[_CurrentTime] ────────────────────────────────             |         [Fresh Color] ─┘    |
[_MaxAge] ─────────────────────────────────────────────────┘         [Gray Color] ───────┘
```

## Testing

1. Play the game
2. Move your character to paint trail
3. Wait 5-10 seconds
4. **The old trail should be grayer than fresh trail!**

## Troubleshooting

### "Trail doesn't change color"
- Check that `Enable Time Aging` is checked on SimplePaintSurface
- Make sure TimeBrush_Mat is assigned on RenderTextureTrailPainter
- Verify surface material uses `PaintSurfaceWithAging` shader

### "Trail turns gray instantly"
- Increase `Max Age Seconds` (try 30 or 60)
- Check `_MaxAge` property in shader

### "Nothing shows at all"
- Check that `_PaintTimeTex` is being assigned to material
- Look for errors in console

## Parameters to Tune

| Parameter | Effect |
|-----------|--------|
| Max Age Seconds | How long until fully gray (longer = slower fade) |
| Fresh Color | Color of new paint |
| Old Color | Color paint fades to (gray) |
