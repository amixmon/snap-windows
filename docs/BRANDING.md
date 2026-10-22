# Snap branding

The production logo is `src/Snap/Assets/Snap-logo.png`, a transparent high-resolution PNG. The Windows icon is `src/Snap/Assets/Snap-app.ico`, with 16, 20, 24, 32, 40, 48, 64, 128 and 256 pixel entries. The app executable, taskbar/tray, installed shortcuts, setup and uninstaller use the same mark.

The logo was generated with the built-in ImageGen tool. The original artwork is retained in the project; no external asset URL is needed at runtime. `tools/Prepare-BrandAssets.ps1` resizes the original into Windows icon entries and places it in the installer artwork. It does not redraw the mark. Run that script again if the source artwork changes, then rebuild the app and installer.

Final generation prompt:

> Use case: logo-brand. Asset type: production Windows application icon and simple logo for Snap, a minimal native screenshot app. Create ONE finished icon, centered, front facing, square composition, with genuinely transparent canvas outside the icon. Design: a warm coral-orange rounded square tile with a single exceptionally simple white screenshot selection/crop mark inside: four generous separated rounded L-shaped corner brackets, plus a small centered solid white capture dot. The icon should feel modern, calm, precise, friendly, and immediately readable at Windows taskbar sizes 16 to 32 pixels. Flat vector-like edges, uniform bold strokes, careful symmetry, generous negative space. Use the existing Snap palette: vivid warm orange/coral and white. Keep the tile large with only a narrow transparent margin, about 6 percent. No text, no lettering, no mockup, no scene, no device, no extra icons, no watermark, no photographic texture, no 3D bevel, no perspective, no cast shadow outside the tile, no decorative elements. Deliver a clean high resolution transparent PNG app icon.

The earlier `Snap.ico` and its original generator are retained as legacy project assets; the application now references `Snap-app.ico`.
