using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace B0r3dQR.Controls
{
    // A whole number with up and down buttons (WPF has none of its own). Typing takes effect on
    // Enter or on leaving the box, so a half-typed number never jumps to the limit; the buttons, the
    // arrow keys and the mouse wheel change it at once.
    public partial class NumberBox : UserControl
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(NumberBox),
            new FrameworkPropertyMetadata(0, (d, e) => ((NumberBox)d).OnValueChanged(), (d, v) => ((NumberBox)d).Fit((int)v)));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(int), typeof(NumberBox), new PropertyMetadata(0, (d, e) => d.CoerceValue(ValueProperty)));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(int), typeof(NumberBox), new PropertyMetadata(int.MaxValue, (d, e) => d.CoerceValue(ValueProperty)));

        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(int), typeof(NumberBox), new PropertyMetadata(1));

        public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
        public int Minimum { get => (int)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
        public int Maximum { get => (int)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
        public int Step { get => (int)GetValue(StepProperty); set => SetValue(StepProperty, value); }

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

        int Fit(int value) => Math.Max(Minimum, Math.Min(Maximum, value));

        void OnValueChanged()
        {
            Field.Text = Show(Value);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        static string Show(int value) => value.ToString(CultureInfo.CurrentCulture);

        // takes what's typed, or puts the number back if it isn't one
        public void Commit()
        {
            string typed = Field.Text.Trim();
            if (int.TryParse(typed, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out int number))
            {
                int was = Value;
                Value = number;
                if (Value == was) Field.Text = Show(Value);  // clamped back to the same number
            }
            else
            {
                Field.Text = Show(Value);
            }
        }

        void Nudge(int steps)
        {
            Commit();
            long next = (long)Value + (long)steps * Step;
            Value = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, next));
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
