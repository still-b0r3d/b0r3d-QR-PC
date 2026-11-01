using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using B0r3dQR.Design;

namespace B0r3dQR
{
    // The website's "Data to encode" window: a template's form, opened from the Data section, that
    // writes the code's text. What's typed in each form is kept while b0r3d QR runs, as the website
    // keeps it, and the text already in the Data box fills the form when it's that template.
    public partial class TemplateWindow : Window
    {
        sealed class FieldUi
        {
            public TemplateField Field;
            public TextBlock Label, Note, Problem, Placeholder;
            public Control Input;          // a TextBox, PasswordBox, ComboBox or CheckBox
        }

        readonly Dictionary<TemplateKind, Dictionary<string, string>> forms;
        readonly List<FieldUi> fields = new List<FieldUi>();
        readonly Dictionary<string, string> problems = new Dictionary<string, string>();   // after Save: field and message
        TemplateKind kind;
        bool submitted, showing;

        // the text to use, once Save has written it; null while there's none
        public string Result { get; private set; }

        internal TemplateKind Kind => kind;
        internal Dictionary<string, string> Values => forms[kind];

        internal TemplateWindow(TemplateKind start, string data, Dictionary<TemplateKind, Dictionary<string, string>> forms)
        {
            InitializeComponent();
            this.forms = forms;
            foreach (TemplateKind k in TemplateForms.Types)
                if (!forms.ContainsKey(k)) forms[k] = TemplateForms.Defaults(k);
            // the website fills the form from the data only when the data is that template
            if (!string.IsNullOrEmpty(data) && Templates.Detect(data, out Dictionary<string, string> read) == start)
                forms[start] = TemplateForms.FromText(start, read);
            RequiredNote.Text = TemplateForms.RequiredNote;
            showing = true;
            TypeBox.ItemsSource = TemplateForms.Types.Select(TemplateForms.Label).ToList();
            showing = false;
            ShowForm(start);
            SourceInitialized += (s, e) => Theme.TitleBar(this);
        }

        // the window over `owner`, and the text it wrote (null when it was cancelled)
        internal static string Ask(Window owner, TemplateKind start, string data, Dictionary<TemplateKind, Dictionary<string, string>> forms)
        {
            var window = new TemplateWindow(start, data, forms) { Owner = owner };
            return window.ShowDialog() == true ? window.Result : null;
        }

        void ShowForm(TemplateKind k)
        {
            kind = k;
            showing = true;
            TypeBox.SelectedIndex = Array.IndexOf(TemplateForms.Types, k);
            Build();
            showing = false;
            Refresh();
        }

        void OnType(object sender, SelectionChangedEventArgs e)
        {
            if (showing || TypeBox.SelectedIndex < 0) return;
            submitted = false;   // a new template starts with no messages, as on the website
            problems.Clear();
            ShowForm(TemplateForms.Types[TypeBox.SelectedIndex]);
        }

        void OnExample(object sender, RoutedEventArgs e)
        {
            // the fields filled in lose their messages, as they would being typed in
            foreach (KeyValuePair<string, string> pair in TemplateForms.Example(kind, DateTime.Now))
            {
                bool changed = !Values.TryGetValue(pair.Key, out string was) || was != pair.Value;
                Values[pair.Key] = pair.Value;
                if (changed) Unflag(pair.Key, pair.Value);
            }
            ShowForm(kind);
        }

        // ---------- the form ----------

        void Build()
        {
            FieldsPanel.Children.Clear();
            fields.Clear();
            string intro = TemplateForms.Intro(kind);
            if (intro != null) FieldsPanel.Children.Add(new TextBlock { Text = intro, Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 0, 0, 2) });
            Grid grid = null;
            int row = -1, column = 0;
            bool rowFull = true;   // the next field starts a row of its own
            if (kind == TemplateKind.VCard)
            {
                // a contact's fields two to a row, as on the website's wider screens
                grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                FieldsPanel.Children.Add(grid);
            }
            foreach (TemplateField f in TemplateForms.Fields(kind))
            {
                FieldUi ui = Make(f);
                fields.Add(ui);
                var box = new StackPanel();
                if (ui.Label != null) box.Children.Add(ui.Label);
                box.Children.Add(ui.Placeholder == null ? (UIElement)ui.Input : WithPlaceholder(ui));
                box.Children.Add(ui.Note);
                box.Children.Add(ui.Problem);
                if (grid == null)
                {
                    FieldsPanel.Children.Add(box);
                    continue;
                }
                if (f.Wide || rowFull)
                {
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    row++;
                    column = 0;
                }
                else column = 2;
                Grid.SetRow(box, row);
                Grid.SetColumn(box, column);
                if (f.Wide) Grid.SetColumnSpan(box, 3);
                grid.Children.Add(box);
                rowFull = f.Wide || column == 2;
            }
        }

        FieldUi Make(TemplateField f)
        {
            var ui = new FieldUi { Field = f };
            string value = Values.TryGetValue(f.Name, out string v) ? v ?? "" : "";
            if (f.Kind != FieldKind.Check) ui.Label = new TextBlock { Style = (Style)FindResource("Label") };
            ui.Note = new TextBlock { Style = (Style)FindResource("Hint"), Visibility = Visibility.Collapsed };
            ui.Problem = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
            ui.Problem.SetResourceReference(TextBlock.ForegroundProperty, "Bad");
            AutomationProperties.SetLiveSetting(ui.Problem, AutomationLiveSetting.Polite);
            switch (f.Kind)
            {
                case FieldKind.Choice:
                {
                    var choice = new ComboBox { ItemsSource = f.Choices.Select(c => c.Label).ToList() };
                    choice.SelectedIndex = Math.Max(0, Array.FindIndex(f.Choices, c => c.Value == value));
                    choice.SelectionChanged += (s, e) =>
                    {
                        if (showing || choice.SelectedIndex < 0) return;
                        Changed(f, f.Choices[choice.SelectedIndex].Value);
                    };
                    ui.Input = choice;
                    break;
                }
                case FieldKind.Check:
                {
                    var check = new CheckBox { Content = f.Label, IsChecked = value == "true", Margin = new Thickness(0, 12, 0, 0) };
                    check.Checked += (s, e) => Changed(f, "true");
                    check.Unchecked += (s, e) => Changed(f, "false");
                    ui.Input = check;
                    break;
                }
                case FieldKind.Password:
                {
                    var password = new PasswordBox { Password = value };
                    password.PasswordChanged += (s, e) =>
                    {
                        ShowPlaceholder(ui);
                        if (!showing) Changed(f, TemplateForms.AsTyped(f, password.Password));
                    };
                    ui.Input = password;
                    break;
                }
                default:
                {
                    var text = new TextBox { Text = Shown(f, value) };
                    if (f.MaxLength > 0) text.MaxLength = f.MaxLength;
                    if (f.Kind == FieldKind.Lines)
                    {
                        text.AcceptsReturn = true;
                        text.TextWrapping = TextWrapping.Wrap;
                        text.MinHeight = 64;
                        text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                    }
                    text.TextChanged += (s, e) =>
                    {
                        ShowPlaceholder(ui);
                        if (!showing) Changed(f, Typed(f, text.Text));
                    };
                    ui.Input = text;
                    break;
                }
            }
            string placeholder = f.Kind == FieldKind.Date ? "YYYY-MM-DD" : f.Kind == FieldKind.DateTime ? "YYYY-MM-DD HH:MM" : f.Placeholder;
            if (!string.IsNullOrEmpty(placeholder) && (ui.Input is TextBox || ui.Input is PasswordBox))
            {
                ui.Placeholder = new TextBlock { Text = placeholder, Margin = new Thickness(7, 5, 0, 0), IsHitTestVisible = false };
                ui.Placeholder.SetResourceReference(TextBlock.ForegroundProperty, "Dim");
                ui.Placeholder.Opacity = 0.75;
            }
            AutomationProperties.SetName(ui.Input, f.Label);
            if (f.Hint != null) AutomationProperties.SetHelpText(ui.Input, f.Hint);
            else if (!string.IsNullOrEmpty(placeholder)) AutomationProperties.SetHelpText(ui.Input, placeholder);
            return ui;
        }

        static Grid WithPlaceholder(FieldUi ui)
        {
            var grid = new Grid();
            grid.Children.Add(ui.Input);
            grid.Children.Add(ui.Placeholder);
            ShowPlaceholder(ui);
            return grid;
        }

        static void ShowPlaceholder(FieldUi ui)
        {
            if (ui.Placeholder == null) return;
            bool empty = ui.Input is TextBox t ? t.Text.Length == 0 : ((PasswordBox)ui.Input).Password.Length == 0;
            ui.Placeholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        // how a field's value shows in its box (a time with a space where the T is)
        static string Shown(TemplateField f, string value) => f.Kind == FieldKind.DateTime ? value.Replace('T', ' ') : value;

        // what a field holds once something's typed in it: a number field the number written the
        // JavaScript way, as the website's number fields give it; a date or time in the website's
        // form, or nothing until it's a whole one; other text as a browser's field gives it
        static string Typed(TemplateField f, string text)
        {
            switch (f.Kind)
            {
                case FieldKind.Number: return TemplateForms.TypedNumber(text);
                case FieldKind.Date:
                    return DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime day)
                        ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
                case FieldKind.DateTime:
                    return DateTime.TryParseExact(text.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'H:mm" },
                                                  CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime time)
                        ? time.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture) : "";
                default: return TemplateForms.AsTyped(f, text);
            }
        }

        void Changed(TemplateField f, string value)
        {
            Values[f.Name] = value;
            Unflag(f.Name, value);
            Refresh();
        }

        // A message goes once its field is filled in, as on the website; the IBAN's check (which
        // makes way for the live one) and the amount's go as soon as the field changes.
        void Unflag(string field, string value)
        {
            if (submitted && problems.TryGetValue(field, out string message) &&
                ((value.Length > 0 && TemplateForms.ClearsWhenFilled(kind, field)) || TemplateForms.ClearsWhenChanged(message))) problems.Remove(field);
            if (kind == TemplateKind.Epc && field == "version" && value != "001") problems.Remove("bic");
        }

        // the asterisks, the fields that are off, and the lines under the fields
        void Refresh()
        {
            foreach (FieldUi ui in fields)
            {
                string name = ui.Field.Name;
                bool required = TemplateForms.Required(kind, name, Values);
                if (ui.Label != null)
                {
                    ui.Label.Inlines.Clear();
                    ui.Label.Inlines.Add(new Run(ui.Field.Label));
                    if (required)
                    {
                        var star = new Run(" *");
                        star.SetResourceReference(TextElement.ForegroundProperty, "Bad");
                        ui.Label.Inlines.Add(star);
                    }
                }
                AutomationProperties.SetIsRequiredForForm(ui.Input, required);
                ui.Input.IsEnabled = !TemplateForms.Disabled(kind, name, Values);

                string live = TemplateForms.LiveNote(kind, name, Values);
                problems.TryGetValue(name, out string red);
                if (name == "gtin") red = live ?? red;
                else if (name == "iban" && red == null) red = live;
                string dim = name == "encryption" ? live : name == "gtin" && red != null ? null : ui.Field.Hint;
                ui.Problem.Text = red ?? "";
                ui.Problem.Visibility = red != null ? Visibility.Visible : Visibility.Collapsed;
                ui.Note.Text = dim ?? "";
                ui.Note.Visibility = dim != null ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ---------- Save ----------

        void OnSave(object sender, RoutedEventArgs e) => Save();

        // checks the form; with nothing missing, writes the text and closes
        internal bool Save()
        {
            submitted = true;
            problems.Clear();
            foreach (KeyValuePair<string, string> p in TemplateForms.Problems(kind, Values)) problems[p.Key] = p.Value;
            Refresh();
            FieldUi first = fields.FirstOrDefault(f => problems.ContainsKey(f.Field.Name));
            if (first != null)
            {
                first.Input.Focus();
                if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                    UIElementAutomationPeer.CreatePeerForElement(first.Problem)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                return false;
            }
            Result = Templates.Make(kind, Values);
            try
            {
                DialogResult = true;
            }
            catch (InvalidOperationException)
            {
                Close();   // not shown as a dialog (the tests)
            }
            return true;
        }

        // for the tests: a field's box, by the field's name
        internal Control Input(string name) => fields.FirstOrDefault(f => f.Field.Name == name)?.Input;

        internal string ProblemText(string name) => fields.FirstOrDefault(f => f.Field.Name == name)?.Problem.Text ?? "";

        internal bool HasAsterisk(string name) => fields.FirstOrDefault(f => f.Field.Name == name)?.Label?.Inlines.Count == 2;
    }
}
