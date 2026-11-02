using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace B0r3dQR.Controls
{
    // A number with up and down buttons (WPF has none of its own): whole numbers, or with up to
    // `Decimals` places. Typing takes effect on Enter or on leaving the box, so a half-typed number
    // never jumps to the limit; the buttons, the arrow keys and the mouse wheel change it at once.
    public partial class NumberBox : UserControl
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(NumberBox),
            new FrameworkPropertyMetadata(0.0, (d, e) => ((NumberBox)d).OnValueChanged(), (d, v) => ((NumberBox)d).Fit((double)v)));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(NumberBox), new PropertyMetadata(0.0, (d, e) => d.CoerceValue(ValueProperty)));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(NumberBox), new PropertyMetadata((double)int.MaxValue, (d, e) => d.CoerceValue(ValueProperty)));

        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(double), typeof(NumberBox), new PropertyMetadata(1.0));

        public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
            nameof(Decimals), typeof(int), typeof(NumberBox), new PropertyMetadata(0, (d, e) => ((NumberBox)d).Field.Text = ((NumberBox)d).Show(((NumberBox)d).Value)));

        // what 0 means when set: the website takes 0 as "the default" in some of its boxes
        public static readonly DependencyProperty FallbackProperty = DependencyProperty.Register(
            nameof(Fallback), typeof(double), typeof(NumberBox), new PropertyMetadata(double.NaN));

        public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
        public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }
        public double Fallback { get => (double)GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }

        public event EventHandler ValueChanged;

        public NumberBox()
        {
            InitializeComponent();
            Field.Text = Show(Value);
            // a screen reader lands on the text box inside, so it takes the box's name
            Field.SetBinding(AutomationProperties.NameProperty, new Binding { Path = new PropertyPath(AutomationProperties.NameProperty), Source = this });
            Field.GotKeyboardFocus += (s, e) => Box.SetResourceReference(Border.BorderBrushProperty, "Accent");
            Field.LostKeyboardFocus += (s, e) => Box.SetResourceReference(Border.BorderBrushProperty, "FieldLine");
        }

        // the text box inside, for a label's target and for the tests
        public TextBox TextField => Field;

        double Fit(double value)
        {
            if (value == 0 && !double.IsNaN(Fallback)) value = Fallback;
            if (double.IsNaN(value)) value = Minimum;
            return Math.Max(Minimum, Math.Min(Maximum, Math.Round(value, Decimals)));
        }

        void OnValueChanged()
        {
            Field.Text = Show(Value);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        string Show(double value) => value.ToString(Decimals == 0 ? "0" : "0." + new string('#', Decimals), CultureInfo.CurrentCulture);

        // takes what's typed (in this PC's way of writing numbers, or with a point), or puts the
        // number back if it isn't one
        public void Commit()
        {
            string typed = Field.Text.Trim();
            if (Parse(typed, out double number))
            {
                double was = Value;
                Value = number;
                if (Value == was) Field.Text = Show(Value);  // clamped back to the same number
            }
            else
            {
                Field.Text = Show(Value);
            }
        }

        // "4.5" is four and a half whatever the PC's language, as on the website; where a comma is the
        // decimal mark, "4,5" and "1.000,5" are read that way too
        internal static bool Parse(string typed, out double number)
        {
            string mark = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            if (mark != "." && typed.Contains(".") && !typed.Contains(mark))
                return double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
            return double.TryParse(typed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out number) ||
                   double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        void Nudge(int steps)
        {
            Commit();
            Value = Value + steps * Step;
        }

        void OnUp(object sender, RoutedEventArgs e) => Nudge(1);

        void OnDown(object sender, RoutedEventArgs e) => Nudge(-1);

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up: Nudge(1); e.Handled = true; break;
                case Key.Down: Nudge(-1); e.Handled = true; break;
                case Key.Enter: Commit(); Field.SelectAll(); e.Handled = true; break;
            }
        }

        void OnLeave(object sender, KeyboardFocusChangedEventArgs e) => Commit();

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Field.IsKeyboardFocusWithin) return;  // a wheel over an unfocused box scrolls the page
            Nudge(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            if (!Field.IsKeyboardFocusWithin) Box.SetResourceReference(Border.BorderBrushProperty, "FieldLineHover");
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Field.IsKeyboardFocusWithin) Box.SetResourceReference(Border.BorderBrushProperty, "FieldLine");
        }
    }
}
