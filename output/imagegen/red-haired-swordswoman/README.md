# 붉은 머리 검사 — Idle

- `idle-64x64.gif`: 64 × 64, 8 frames, 100 ms per frame, infinite loop.
- `idle-sheet-512x64.png`: transparent PNG, 8 frames left to right; each frame 64 × 64.
- `frames/`: individual transparent PNG frames.
- `preview-4x.gif`: nearest-neighbor enlarged preview, 256 × 256.
- Unity import suggestion: Sprite (2D and UI), Multiple, Grid by Cell Size 64 × 64, Point filter, Compression None, animation 10 FPS.

Artwork generated with the built-in image_gen tool. Sharp used for mechanical frame extraction, nearest-neighbor sizing, sprite sheet and GIF packaging.

## Generation prompt

Use case: stylized-concept. Asset type: game pixel-art animation sprite sheet. Create one new original character, inspired by the attached conversation reference's compact Japanese pixel-art sword fighter proportions and idle stance, but clearly a DIFFERENT character: short copper-red bob hair, teal short jacket, dark fitted trousers, brown boots, short sword held low. 3/4 view facing right. Exactly EIGHT sequential frames of one subtle seamless breathing idle animation. Feet stay anchored, shoulders rise and fall subtly, hair tips and jacket hem sway, sword follows hand consistently. One identical character throughout. Production layout is a 4-column by 2-row sprite sheet, row-major animation order, exactly equal SQUARE cells, no gutters, no padding outside the grid. Each cell represents a 64 by 64 pixel logical canvas; draw like genuine 64px low-resolution pixel art enlarged with nearest-neighbor square pixels, with restrained 20-30 color palette, crisp stepped outlines, no antialiasing, no gradients, no text or panel borders. Character full body about 48 logical pixels tall and centered at identical position in each cell, soles at logical y=58 and center x=32. All character details fit the cell. Idle phase in degrees across frames: 0,45,90,135,180,225,270,315. Actual transparent background, no ground shadow, no scenery, no checkerboard. Output an exact 2:1 aspect image (ideally 1024x512), 4 columns by 2 rows; all 8 equally spaced complete characters must be visible. The reference image is for style only, not an edit target.
