# Changelog

What changed in each version of b0r3d QR for Windows, the newest first. The versions before 1.0
are its stages, built one after the other; 0.8 is the first one released.

## 1.3 (released 8 November 2026)

Version 1.3 for parity with qr.b0r3d.org v1.3 (skipping 0.9 to 1.2 to match the website's
version number): four new features (Scan from anywhere, Scan PDFs, Vector PDF, Avery label
sheets with printer nudge), plus website parity and bug fixes.

- **Scan from anywhere** (Scan menu and Scan tab): while b0r3d QR is running, a cup icon in
  the taskbar corner and **Ctrl+Shift+Q** start Scan the screen without opening the main
  window first. **Hide in the tray** hides the main window to the tray icon; pressing Esc
  during a screen scan leaves it hidden there, and a code found opens the Scan tab. Right-click
  the tray icon for Scan the screen, Scan with the camera, Paste picture, Show b0r3d QR and
  Exit. Nothing starts with Windows, and nothing is written outside the data folder.
- **Scan PDFs**: **Open picture or PDF…** (Ctrl+O), dropping a PDF on the window, pasting a
  copied PDF file (Ctrl+V), or **Scan a folder…** reads every page of a PDF (up to 50 pages,
  at 200 DPI) through Windows' built-in PDF reader (`Windows.Data.Pdf`), with the page number
  on each code found (`file.pdf (page 2)`). Password-protected PDFs say so. Nothing extra is
  installed and b0r3d QR still never goes online.
- **Vector PDF**: PDF exports (File → Save as PDF… and Batch export) now draw QR codes,
  barcodes, frames, SVG logos (linear and radial gradients included) and captions (their
  letters as outlines, colour emoji as their layers) as vector shapes, so they stay sharp at
  any zoom or print size. Raster logos and frame backgrounds are kept without loss, with a
  soft mask for see-through parts. Every saved PDF is read back through Windows' PDF reader
  and scanned.
- **Label sheets**: **Print…** and Batch export's **Print them…** print onto standard Avery
  label sheets (**5160**, **5167**, **5163**, **5164** and **22806** on US Letter; **L7160**,
  **L7163** and **L7165** on A4), one code on every label with a 1.5 mm safe inset and no cut
  marks, plus a **Nudge** setting (**Right** and **Down**, up to 10 mm) for printers that feed
  sheets slightly off. The sheet and nudge are kept in `b0r3dQR.ini`.
- **Parity with the new qr.b0r3d.org and fixes**:
  - The colour contrast warning rounds the ratio down (`Low contrast (3.9:1)` for red on
    white), matching the website.
  - Saving a JPG with a partly see-through background puts white paper under it, for QR codes
    and barcodes alike.
  - A design file whose logo has control characters before its scheme is refused.
  - One-click text fixes (`Trim…`) and saving a data template keep Windows line breaks (`CR LF`)
    when the text already uses them; after a one-click fix, the focus returns to the Data box;
    choosing the template already showing in the list opens its form.
  - Recent codes: a code whose design can't be read says `That code can't be opened: …`; a
    recent barcode whose entry leaves out some settings keeps the settings currently on the
    Make tab, as on the website; a Data Matrix Wi-Fi code with a password is titled by network
    name (`WiFi: …`) and shown with a padlock instead of its picture, like a QR code's.
  - `mailto:`, `sms:` and quoted-printable vCard 2.1 text decode broken UTF-8 with one
    replacement character (``) per broken byte sequence, as browsers do.
  - Unticking **Include quiet zones** leaves Data Matrix and PDF417 margins alone (the box is
    hidden for them), and **What's in this code** says which codes have quiet zones.
  - Screen readers hear a barcode's error once per mistake rather than on every character count
    change; barcode text under the bars is drawn fully opaque.
  - Batch export plans and names large CSVs in linear time, and cuts file names over 255
    characters down so Windows can save them.
  - Case-blind regular expressions use the invariant culture, so Turkish Windows matches `i`
    and `I` as other Windows languages do.
- The self-test also draws a framed code in Pacifico to a vector PDF and reads it back through
  Windows' PDF reader (`pdf=ok`), lays out an Avery label sheet, and loads the tray icon.

## 0.8 (released 6 November 2026)

The last of what qr.b0r3d.org does: SVG logos, its caption fonts and the camera.

- SVG logos and SVG frame pictures are drawn into every picture: the preview, PNG, JPG, PDF,
  copying, dragging, printing, batch export and Recent codes. b0r3d QR reads SVG itself and
  draws it as the website's browser does (checked against 210 pictures Chromium drew the
  website's way); an SVG's patterns, masks, filters and markers are left out. An SVG logo can
  be pasted too. One that can't be drawn, or is too big or too deeply nested to draw safely
  (more than 2,000 characters of text, say), is refused with the reason.
- Captions in the website's 18 fonts, built into the exe, in the preview and every file, so a
  caption looks the same on any PC and its lines break where the website's preview breaks them
  (checked against 1,083 widths and 445 layouts measured in Chromium). The generic monospace is
  Consolas now, as in Chrome and Edge on Windows. The fonts make the exe about 4 MB.
- Emoji in captions, and in an SVG logo's or frame picture's text, are in colour in the
  preview and every picture (PNG, JPG, PDF, copying, dragging, printing, batch export and
  Recent codes), from Windows' Segoe UI Emoji as on the website; an SVG file keeps the caption
  as text, as the website's does. An emoji takes the room it takes on the website, so a
  caption's lines break where its preview breaks them.
- A letter carrying more than 100 accents or other marks (in a scanned code, a paste, a design
  file, a file or camera name: anywhere b0r3d QR shows text) is shown with its first 100:
  Windows' text layout ends a program at about 700. What counts as a mark follows Unicode 17,
  newer than the Unicode .NET knows. Copy text still copies a scanned code's whole text. A
  paste with more goes in without the extra ones, as one step to undo, and the Make tab (or a
  template's field) says so for as long as the code holds less.
- Scan with the camera (Scan tab, Scan menu, Ctrl+Shift+A): your webcam's picture with the
  website's aiming frame, read a few times a second at the website's size. The first code found
  goes to the Scan tab and the camera turns off at once. A list of cameras when there's more
  than one, and a message when there's no camera, it's in use, or Windows doesn't allow it
  (with where to allow it). Closing the window or choosing another camera turns the
  camera off at once, even on a busy PC. Nothing the camera sees is saved.
- Batch export: a row whose file fails to be made is listed with why in the summary, and the
  other rows are still made; a zip that's stopped or fails never leaves its temporary file.
- The Windows DLLs b0r3d QR calls are only ever loaded from System32, never from the folder
  the exe is in.
- New wording for the data templates' help lines, placeholders, examples (Use example) and
  missing-field messages, the camera's messages (which now say where to allow the camera on
  Windows 10 too) and the note on a logo's error correction; the website words them its own
  way. What each template checks and writes is the same.
- Written anew: Batch export's CSV reader, its rows and its file names for QR codes and text;
  the data templates' writing, and telling which template a text is; and the layout of a
  logo's room, a frame and its caption. Everything comes out as before: checked against 45 of
  the website's own CSV readings and 118 of its template cases, and against the previous
  build on millions of random CSV files, texts and template fields, and thousands of designs.
- A caption with Windows line breaks (from a design file saved elsewhere, or a batch CSV
  saved that way) is laid out, drawn and sized as on qr.b0r3d.org: its SVG keeps each break's
  CR as the website's does, so a line that only just fits wraps a word sooner there too;
  pictures and printing draw that CR as a space, as browsers draw the website's SVG; and the
  caption box shows each line break once. Checked against 114 of the website's SVG files
  drawn by Chromium and 210 more of its preview's layouts.
- Capitals and small letters as JavaScript in a browser makes them, wherever b0r3d QR does
  what the website does: a payment code's BIC, IBAN and purpose (ß becomes SS, a ligature
  such as ﬁ becomes FI), the IBAN check, the names in Wi-Fi, contact and event codes, a batch
  file's header and a link's tracking parameters (a Kelvin sign counts as a k there), and
  Code 39's and ISBN's capitals. Checked against Chromium for every character.
- Saving over a file on a USB stick that can't swap files: the old file is moved out of the
  way, the new one put in its place and the old one deleted, so a nearly full stick can't lose
  either, and if the new one can't go in, the old one goes back (it used to be copied over). A
  batch's zip goes in the same way now; it used to fail there.
- Printing: with Windows' print service stopped, Print says what's wrong instead of failing.
- Batch export: a row named like a Windows device (CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to
  LPT9) gets _ on its file's name (CON_.png), into a folder or a zip: Windows 10 can't make a
  file called CON.png.
- THIRD-PARTY-NOTICES.txt has the Unicode License V3 now, for the character tables b0r3d QR
  makes from Unicode's data, and HarfBuzz's notice, for the way a caption takes its letters and
  accents from a font, which follows HarfBuzz's.

## 0.7 (not released yet)

The seventh stage: getting ready for the first release.

- Help → Check for a new version opens b0r3d QR's Releases page in your browser. b0r3d QR still
  never goes online itself: the page only opens when you click.
- Print… sits under the code with Save PNG…, Save SVG… and Copy picture, as File → Print…
  (Ctrl+P) does.
- Recent codes: with one code in the list, Clear all asks "Delete the 1 code", not "Delete all
  1 codes".
- The README says how to get b0r3d QR and past Windows' SmartScreen warning (More info, then
  Run anyway: it isn't code-signed), with pictures of every part of it, drawn offscreen.
- The third-party notices (in THIRD-PARTY-NOTICES.txt and Help → About) name more of the code
  b0r3d QR follows: Barcode4J (behind ZXing's PDF417 and Data Matrix writers), zint's Data
  Matrix and Reed-Solomon authors, Project Nayuki (behind zint's choice of QR modes), GS1's
  Barcode Syntax Dictionary (the GS1 field lengths), and qrcode-generator (whose mask scoring
  b0r3d QR's QR codes follow, as the website's do).
- Ctrl+C in any text box keeps the text out of Windows' cloud clipboard, in every window: the
  data templates' forms (an IBAN, a contact's details) let it through before.

## 0.6 (not released yet)

The sixth stage: batch export and printing.

- Batch export (File → Batch export…, Ctrl+B, or drop a CSV on the Make tab): a file for every
  row of a CSV, for QR codes in the Make tab's design or for the barcode type showing in its
  settings. The CSV is read as the website reads it (its columns, contacts files written as
  vCards, captions, file names and fonts, commas, semicolons or tabs, UTF-8 or Windows-1252),
  and the files are named as the website names them (checked against 35 of the website's own
  CSV readings). Rows that can't be made are listed, with why, before anything is written; each
  row can be looked at in turn, its code drawn as its file will be. PNG, JPG, SVG, PDF or text
  (QR codes), straight into a folder or into a zip; files already in the folder are asked
  about once (number the new ones, replace them or skip them). Every file is read back once
  it's written and scanned, and a summary says what was made and anything that wasn't. Examples
  to start from (links, contacts) can be saved from the window. A frame shows each row's
  caption when the file has captions, as on the website.
- Printing (File → Print…, Ctrl+P): the code at its exact size, the print size set on the Make
  tab (or its picture's size without one), alone in the middle of the page or as a sheet of
  copies, with a caption under each code and cut marks at the corners; a batch prints on sheets
  with each code's file name under it, or one a page. A preview of each page, then Windows'
  Print dialog for the printer and its paper. The codes are drawn as shapes, so they print as
  sharp as the printer can; a code bigger than the paper is printed smaller to fit, and the
  window says so.

## 0.5 (not released yet)

The fifth stage: the website's 13 other barcodes, as qr.b0r3d.org makes them.

- Code type, above the settings: QR code, or Data Matrix, GS1 DataMatrix, Aztec, PDF417,
  EAN-13, EAN-8, UPC-A, UPC-E, ISBN, Code 128, GS1-128, ITF-14 and Code 39, in the website's
  groups, each with its hint and example.
- The website has zint 2.16.0 make these, so b0r3d QR follows zint's rules, written again in
  C#: the same bars and modules, the same text under the bars, quiet zones and ITF-14 box, and
  the same SVG, character for character (checked against 624 barcodes the website made, and
  252 2D codes module for module). Check digits are added or checked, GS1 data in round
  brackets is checked as zint checks it, and what can't be made gets the website's message.
- Barcode settings: the text under the bars, bar height, quiet zones, a rectangular Data
  Matrix, colours or a see-through background, pixels per module, and a print size with whole
  pixels for every bar. The scan check, warnings, What's in this code, PNG, JPG, SVG and PDF,
  copying, dragging and Recent codes all work for barcodes. Each type keeps its own text while
  the program runs; the barcode settings are kept for next time.
- Scan tab: Make this code again works for the barcodes it can make too, as the same type.
  GS1 data (GS1-128, GS1 DataMatrix, GS1 QR codes) is shown in round brackets, and GS1-128's
  fields no longer run together.

## 0.4 (not released yet)

The fourth stage: data templates and recent codes, as qr.b0r3d.org has them.

- Data templates: text, URL, Wi-Fi, contact card (vCard 2.1, 3.0 or 4.0), email, phone, SMS,
  location, event, EPC payment (SEPA, GiroCode) and product (GS1 Digital Link), each a form
  that writes the code's text the website's way, byte for byte (checked against 116 cases run
  through the website's own code). The list under Data shows which template the text is;
  choosing one, or Edit…, opens its form filled from the text already there, so a code made
  elsewhere opens without losing anything the form shows. Messages for missing fields, live
  checks of IBANs and product numbers, and an example for each form (Use example). What's
  typed in each form is kept while the program runs. Where the website's Save could write a
  wrong code or none over the text (an amount like 1.234,56, a coordinate that isn't a number,
  a name or BIC of spaces alone), the form asks instead.
- Recent codes: the last 20 codes saved, copied or dragged out (the same text again replaces
  its old entry), with a picture of each drawn from its design, none for a Wi-Fi network with
  its password. Open brings a code back, design and text; Delete takes one out; Clear all asks
  again first. A switch turns it off (it's on, as on the website), and a note says where the
  first code went, with Don't keep codes. They're kept in `b0r3dQR recent.json` in the data
  folder, nowhere else; a file from a newer b0r3d QR, or one another program has open, is left
  alone.

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
- The version and the build are shown apart: v0.3 · Build: abc1234.

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
