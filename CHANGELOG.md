# Changelog

## 0.1 (not released yet)

The first stage: the program itself.

- The window: menus, the b0r3d cup, light or dark like Windows (or picked under View →
  Look), and sharp on every screen, whatever its scaling.
- Makes a black-on-white QR code from what you type, as you type, with error correction
  and quiet zone settings, and a check that it reads back exactly.
- Save it as PNG or SVG, copy it as a picture, or drag it into another program or a
  folder. Saved pictures come out at whole pixels per module.
- Settings live in `b0r3dQR.ini` in a `b0r3dQR data` folder next to the exe (or where
  `--data` says); b0r3d QR itself writes nothing in AppData or the registry, keeps saved
  files out of Windows' Recent items and copied codes out of the cloud clipboard, and never
  goes online.
