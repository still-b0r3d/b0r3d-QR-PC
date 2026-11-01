# Changelog

## 0.3 (not released yet)

The third stage: the full QR designer, as qr.b0r3d.org has it.

- The same codes as the website, module for module: its way of splitting text into number,
  capital-letter and byte parts, and its choice of mask (checked against 42 codes the
  website's own encoder made). With the UTF-8 marker, the website has zint make the code,
  so b0r3d QR follows zint's rules then (checked against 58 codes zint made for the website).
- The Make tab's settings fold out in sections like the website's: Data, Style, Logo, Frame,
  and Size and print.
- The website's shapes for the dots, corner squares and corner dots; colours; a background
  or none; rounded corners; the presets Plain, Rounded, Dots, b0r3d Cup and Rukus (their
  logos byte for byte), and Randomize.
- A logo from a file, the clipboard or a drop, with the website's room for it, its size and
  space, and L or M raised to Q. Photos saved sideways are turned upright, as browsers show
  them. An SVG logo gets its room and goes into SVG files, but isn't drawn into pictures yet.
- Frames with a caption on any side, the website's three frame presets, colours, a
  background picture, border, corners, padding and any font on the PC. Framed pictures come
  out at the website's sizes, the caption measured the way its preview wraps it.
- A fixed size (QR version), the UTF-8 marker (ECI 26), the quiet zone, and the website's
  error-correction suggestion.
- The scan check, warnings for contrast, light on dark, see-through backgrounds and the quiet
  zone, the website's link checks with one-click fixes, and What's in this code.
- Files: PNG, JPG, SVG, PDF and text (half blocks, full blocks or ASCII, each as plain text or
  Markdown), at the website's sizes or a print size in mm or inches at 150, 300 or 600 DPI,
  with the module size and scanning distance; copy as a picture, SVG or text. A picture too
  big to draw (over 100 million pixels) is refused with the reason.
- Design files: the website's qr-code-config.json, saved and opened (or dropped on the
  window), both ways, emptied fields included. The last design is kept between runs, never
  its text; the first run after 0.2 starts from 0.2's level, quiet zone and picture size.
- Numbers typed with a point mean the same in any language. Access keys for the main
  switches, and screen readers hear the scan check and the checks on the text.

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
