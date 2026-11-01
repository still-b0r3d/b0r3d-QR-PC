# Changelog

## 0.2 (not released yet)

The second stage: reading codes.

- A Scan tab beside the Make tab. It reads QR codes, Data Matrix, Aztec, PDF417 and the
  common barcodes, as many as there are in a picture, wherever they are in it, light on
  dark too.
- Scan the screen: every screen freezes; drag a box around a code, click a screen to read
  all of it, or press Enter for every screen. It works across screens with different
  scaling, at their real pixels.
- Open pictures, paste one (Ctrl+V), drop pictures or a folder on the window, or scan a
  whole folder, where every picture gets a line (its codes, "No code found" or "Couldn't
  open") to check a batch. Every page of a TIFF is read; online-only files aren't
  downloaded. Esc stops a scan.
- Each code says what it is (link, Wi-Fi network, contact, event, email, phone number,
  payment, sign-in code, product or book number…) and where it was, with Copy text, Open
  link… (it asks first, and warns about unencrypted, numbered or look-alike addresses) and
  Make this code again for plain QR codes, with the exact text.
- Screen readers hear what each code is, which code a button is for, and when a scan ends.
- Pictures of the screen are only ever kept in memory; opened pictures stay out of Windows'
  Recent items, and copied text (Copy text, or Ctrl+C in any text box) out of the cloud
  clipboard.

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
