using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using B0r3dQR.Design;

namespace B0r3dQR.Controls
{
    // A colour: a swatch that opens Windows' colour dialog, and its #rrggbb to type (any CSS colour
    // is taken, and written as #rrggbb, the way the website's pickers write colours; one that's
    // partly see-through as #rrggbbaa, and "transparent" as itself).
    public sealed class ColorBox : UserControl
    {
        readonly Border chip;
        readonly TextBox hex;
        string value = "#000000";

        public event EventHandler ColorChanged;

        public ColorBox()
        {
            chip = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
            chip.SetResourceReference(Border.BorderBrushProperty, "FieldLine");
            var swatch = new Button { Content = chip, MinWidth = 0, Width = 34, Height = 30, Padding = new Thickness(0), ToolTip = "Pick a colour" };
            swatch.Click += (s, e) => Pick();
            hex = new TextBox { Width = 84, Height = 30, Margin = new Thickness(6, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
            hex.LostKeyboardFocus += (s, e) => Commit();
            hex.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                Commit();
                hex.SelectAll();
                e.Handled = true;
            };
            var name = new Binding { Path = new PropertyPath(AutomationProperties.NameProperty), Source = this };
            hex.SetBinding(AutomationProperties.NameProperty, name);
            swatch.SetBinding(AutomationProperties.NameProperty, new Binding { Path = new PropertyPath(AutomationProperties.NameProperty), Source = this, StringFormat = "Pick: {0}" });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(hex);
            Content = row;
            Focusable = false;
            Show();
        }

        // the colour as the design holds it
        public string Value
        {
            get => value;
            set
            {
                this.value = value ?? "";
                Show();
            }
        }

        public TextBox TextField => hex;

        void Show()
        {
            hex.Text = value;
            Color? c = Css.Colour(value);
            chip.Background = new SolidColorBrush(c ?? Colors.Transparent);
            hex.ToolTip = c == null ? "Not a colour: it draws black, as on the website" : null;
        }

        // takes what's typed, or puts the colour back if it isn't one
        public void Commit()
        {
            string typed = hex.Text.Trim();
            Color? c = Css.Colour(typed);
            if (c == null || typed == value)
            {
                hex.Text = value;
                return;
            }
            if (Css.IsTransparent(typed)) Set("transparent");
            else if (c.Value.A < 255) Set(Css.ToHex(c.Value) + c.Value.A.ToString("x2"));
            else Set(typed.StartsWith("#", StringComparison.Ordinal) && typed.Length == 7 ? typed.ToLowerInvariant() : Css.ToHex(c.Value));
        }

        void Pick()
        {
            Commit();
            Color? picked = ColorDialog.Pick(Window.GetWindow(this), Css.Colour(value) ?? Colors.Black);
            if (picked.HasValue) Set(Css.ToHex(picked.Value));
        }

        void Set(string colour)
        {
            if (colour == value)
            {
                Show();
                return;
            }
            value = colour;
            Show();
            ColorChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // Windows' own colour dialog, with its 16 custom colours kept while the program runs.
    static class ColorDialog
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Choose
        {
            public int Size;
            public IntPtr Owner, Instance;
            public int Result;
            public IntPtr Custom;
            public int Flags;
            public IntPtr Data, Hook, Template;
        }

        [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
        static extern bool ChooseColor(ref Choose choose);

        const int RgbInit = 0x1, FullOpen = 0x2, AnyColor = 0x100;
        static IntPtr custom;

        public static Color? Pick(Window owner, Color start)
        {
            if (custom == IntPtr.Zero)
            {
                custom = Marshal.AllocHGlobal(16 * 4);
                for (int i = 0; i < 16; i++) Marshal.WriteInt32(custom, i * 4, 0xFFFFFF);
            }
            var choose = new Choose
            {
                Size = Marshal.SizeOf(typeof(Choose)),
                Owner = owner == null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle,
                Result = start.R | start.G << 8 | start.B << 16,
                Custom = custom,
                Flags = RgbInit | FullOpen | AnyColor,
            };
            if (!ChooseColor(ref choose)) return null;
            int c = choose.Result;
            return Color.FromRgb((byte)c, (byte)(c >> 8), (byte)(c >> 16));
        }
    }
}
