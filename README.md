# b0r3d QR for Windows

**b0r3d QR** makes static QR codes on your PC, and reads codes off pictures and the screen.
Whatever you type goes straight into the pattern, so a code never expires, there's no
account, and nothing you type or scan is sent anywhere. It's the Windows program of
[qr.b0r3d.org](https://qr.b0r3d.org), made by [b0r3d.org](https://b0r3d.org).

It's one small exe that runs on Windows 10 and 11 with nothing to install, and it never
goes online.

**Being built.** This is version 0.2, the second of seven stages: a plain QR code made as you
type, with a check that it reads back, PNG and SVG files, the clipboard and drag and drop;
and reading codes from pictures, folders and the screen. The full designer, data
templates, other barcodes, batch export and printing come next.

## Use it

Put `b0r3dQR.exe` in a folder of its own and double-click it. Type or paste a link or any
text, and the code appears on the right with a line saying whether it reads back. Then:

- **Save PNG…** or **Save SVG…** (File menu, Ctrl+S or Ctrl+Shift+S),
- **Copy picture** (Edit menu, Ctrl+Shift+C), or
- drag the code into another program, or into a folder as a PNG file.

**Error correction** sets how much of the code can be damaged and still read; **Quiet zone**
is the white space around it (4 modules is the standard); **Picture size** is how big saved
pictures come out, at whole pixels per module so every module is sharp.

**View → Look** follows Windows' light or dark setting, or stays light or dark.

## Read codes

The **Scan** tab reads QR codes, Data Matrix, Aztec, PDF417 and the common barcodes (EAN and
UPC product numbers, Code 128, Code 39, Code 93, ITF, Codabar and GS1 DataBar), as many as
there are in a picture:

- **Scan the screen** (Scan menu, Ctrl+Shift+R): b0r3d QR steps aside and every screen
  freezes. Drag a box around a code, click a screen to read all of it, or press Enter to
  read every screen; Esc cancels.
- **Open picture…** (Ctrl+O), Ctrl+V with a picture copied (or **Paste picture**,
  Ctrl+Shift+V), or drop pictures anywhere on the window: PNG, JPEG, BMP, GIF and TIFF
  (every page), and WebP or HEIC where Windows can open them. For a PDF, open it and scan
  the screen.
- **Scan a folder…** (Ctrl+Shift+O) reads every picture in a folder (not the folders inside
  it) and gives each one a line, so a batch can be checked: its codes, or "No code found",
  or "Couldn't open" and why. Pictures that are only online (OneDrive and the like) are
  left alone rather than downloaded.

Esc or **Cancel** stops a scan. Each code found shows what it is (a link, a Wi-Fi network, a
contact, a product number…), its type and where it was, with its whole text in a box under
that. The line under its name leaves out Wi-Fi passwords and sign-in secrets. **Copy text**
copies the text, **Open link…** asks first and says if anything about the link looks off
(no encryption, a numbered address, letters from other alphabets that look like another
site's), and **Make this code again** puts a plain QR code's exact text on the Make tab
with the same error correction.

## Settings

b0r3d QR keeps its settings in `b0r3dQR.ini`, in a `b0r3dQR data` folder next to the exe.
It writes nothing anywhere else itself: not in AppData, not in the registry. To keep them
somewhere else, start it with:

```
b0r3dQR.exe --data "D:\Somewhere\b0r3dQR settings"
```

That folder can hold other things too: b0r3d QR only ever touches its own `b0r3dQR.ini` and
`b0r3dQR drag` folder there.

What you type isn't kept, and neither is anything you scan: pictures of the screen and the
codes found only ever live in memory. If the exe's folder can't be written to, it says so
as soon as it starts and keeps working without saving settings. Run straight out of a zip,
an email attachment or a download folder inside AppData, it keeps nothing there either.

Windows keeps a few records of its own about any program you run: the Open and Save
dialogs remember the last folder you used, and Explorer notes which programs you've
started. b0r3d QR asks Windows not to add the files you open or save to your Recent items,
and a copied code or text never goes to Windows' cloud clipboard.

## Build it yourself

Building needs the [.NET 10 SDK](https://dotnet.microsoft.com/download); running the result
needs only Windows. Double-click `build.cmd`, or run `build.cmd test` to build and run the
tests too. The program is `bin\b0r3dQR.exe`.

`bin\tests\b0r3dQRTests.exe --shots <folder>` draws the windows in light and dark into PNG
files.

## License

MIT + Commons Clause: free to use, change and share, but not to sell. See
[LICENSE](LICENSE). b0r3d QR includes [ZXing.Net](https://github.com/micjahn/ZXing.Net)
(Apache-2.0); see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
