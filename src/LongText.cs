using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using B0r3dQR.Qr;

namespace B0r3dQR
{
    // No text from outside the program ever reaches WPF's text layout with enough marks on one letter to
    // end the whole program (Unicode.Capped says what that means and why). Captions are drawn through it
    // already; this does the same, by the same rule, for every text box (whatever is typed, pasted,
    // dropped or set in it), for the text a scanned code's card is bound to, and (Cap) for every line
    // and note that shows a file's name, a message or anything else from outside.
    static class LongText
    {
        const int MostMarks = CaptionFont.MostMarks;

        // the caption's rule, used everywhere a text box or block shows outside text (the same string back,
        // nothing allocated, when no letter has too many marks)
        internal static string Cap(string text) => Unicode.Capped(text, MostMarks);

        // Shown by the Make tab where a text box IS the data (the data box, the barcode data box): the
        // text put in it had marks left out, so the code holds less than the text it came from. "Make
        // this code again" on a scanned code with such text then makes a different code from the one
        // scanned, which this makes clear.
        public static readonly string DataNote =
            "Some letters had more than " + MostMarks + " marks on them; the extra marks were left out, so this code holds less than the full text.";

        // the same under a template's field (TemplateWindow), whose text becomes the code's
        public static readonly string FieldNote =
            "Some letters had more than " + MostMarks + " marks on them; the extra marks were left out, so the code made from this holds less than the full text.";

        // A text box's letters that had marks left out, as each one's index in its text, kept up to date as the
        // text changes: the notes above show while there are any, and go once those letters are deleted or the
        // text is replaced, not when something is typed elsewhere in it.
        static readonly DependencyProperty CutsProperty =
            DependencyProperty.RegisterAttached("Cuts", typeof(List<int>), typeof(LongText), new PropertyMetadata(null));

        // set while what's going into a box (a paste or a drop) had marks left out on the way
        static readonly DependencyProperty CutComingProperty =
            DependencyProperty.RegisterAttached("CutComing", typeof(bool), typeof(LongText), new PropertyMetadata(false));

        // whether the box's text had marks left out on the way in, and still holds those letters
        public static bool GetTrimmed(DependencyObject box) => box.GetValue(CutsProperty) is List<int> cuts && cuts.Count > 0;

        // the box the guard is changing itself, whose change it lets through
        static TextBox trimming;

        // Every text box in the program caps the marks on each letter, before the box is ever laid out:
        // this is set once, for all of them (App's constructor). A paste or a drop is capped before it goes
        // in, so it's one step to undo and the caret ends up after it, as ever; anything else (what's typed,
        // text set from code) is capped as the text changes, before layout too, which joins the step it came
        // in with and puts the caret where it ended.
        public static void Guard()
        {
            EventManager.RegisterClassHandler(typeof(TextBox), DataObject.PastingEvent, new DataObjectPastingEventHandler(OnPasting));
            EventManager.RegisterClassHandler(typeof(TextBox), TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnTextChanged));
        }

        // a paste or a drop (both come this way): what it would put over the limit is left out of what goes in,
        // the letters around where it goes counted (a mark pasted on a letter at the limit is left out too)
        static void OnPasting(object sender, DataObjectPastingEventArgs e)
        {
            var box = (TextBox)sender;
            if (e.CommandCancelled || e.FormatToApply == null) return;
            string pasted;
            try
            {
                pasted = e.DataObject.GetData(e.FormatToApply) as string;
            }
            catch (Exception x) when (x is ExternalException || x is OutOfMemoryException || x is ArgumentException)
            {
                return;   // another program's data that won't come out: nothing goes in
            }
            if (pasted == null) return;
            string kept = Unicode.Insertable(box.Text, box.SelectionStart, box.SelectionStart + box.SelectionLength, pasted, MostMarks);
            if (ReferenceEquals(kept, pasted)) return;
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, kept);
            e.DataObject = data;
            e.FormatToApply = DataFormats.UnicodeText;
            if (kept.Length > 0 || box.SelectionLength > 0)
            {
                box.SetValue(CutComingProperty, true);
                return;
            }
            // nothing goes in and nothing changes: the letter it would have gone on is the one with marks left out
            var cuts = box.GetValue(CutsProperty) is List<int> had ? new List<int>(had) : new List<int>();
            string text = box.Text;
            Unicode.Around(text, box.SelectionStart, box.SelectionStart, out int start, out int end);
            Unicode.AtTheLimit(text, start, end, MostMarks, cuts);
            SetCuts(box, cuts);
        }

        static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var box = (TextBox)sender;
            if (ReferenceEquals(box, trimming)) return;   // the guard's own change, below
            var changes = new List<Change>(e.Changes.Count);
            foreach (TextChange change in e.Changes) changes.Add(new Change(change.Offset, change.AddedLength, change.RemovedLength));
            bool coming = (bool)box.GetValue(CutComingProperty);
            if (coming) box.ClearValue(CutComingProperty);
            List<int> cuts = box.GetValue(CutsProperty) as List<int>;
            // text that had marks left out on the way in, or came back by Undo or Redo: its letters at the limit are cut short
            string capped = Check(box.Text, changes, ref cuts, coming || e.UndoAction == UndoAction.Undo || e.UndoAction == UndoAction.Redo, out int caret);
            SetCuts(box, cuts);
            if (capped == null) return;
            // SetCurrentValue, never the Text property, so a one-way binding (a scanned code's card) stays. In a box with an
            // undo history it joins the change that brought the text, so undoing that undoes both: nothing ever goes back to
            // the text with too many marks.
            trimming = box;
            try
            {
                box.SetCurrentValue(TextBox.TextProperty, capped);
            }
            finally
            {
                trimming = null;
            }
            box.CaretIndex = caret;
        }

        // one change to a text, as TextChange has it: where (in the changed text), how much went in and how much went
        internal struct Change
        {
            public readonly int Offset, Added, Removed;

            public Change(int offset, int added, int removed)
            {
                Offset = offset;
                Added = added;
                Removed = removed;
            }
        }

        // What the guard makes of a text after changes to it (in order): null when every letter is within the limit,
        // else the text capped, with `caret` where the last change ended. Only the letters around each change can have
        // gone over the limit (the rest of the text was within it), so only those are looked at, and the cost doesn't
        // grow with the text. `cuts`, the letters cut short before, come back where they are now, with those the cap
        // cut short, and with `noteCuts` the letters at the limit around the changes.
        internal static string Check(string text, List<Change> changes, ref List<int> cuts, bool noteCuts, out int caret)
        {
            caret = 0;
            if (changes.Count > 1) changes.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            cuts = Moved(cuts, changes);
            var runs = new List<(int Start, int End)>(changes.Count);
            bool over = false;
            foreach (Change change in changes)
            {
                Unicode.Around(text, change.Offset, change.Offset + change.Added, out int start, out int end);
                if (runs.Count > 0 && start <= runs[runs.Count - 1].End) runs[runs.Count - 1] = (runs[runs.Count - 1].Start, Math.Max(end, runs[runs.Count - 1].End));
                else runs.Add((start, end));
            }
            foreach (var (start, end) in runs) over |= Unicode.OverTheLimit(text, start, end, MostMarks);
            if (!over)
            {
                if (noteCuts)
                {
                    cuts = cuts ?? new List<int>();
                    foreach (var (start, end) in runs) Unicode.AtTheLimit(text, start, end, MostMarks, cuts);
                }
                return null;
            }

            // each run over the limit capped, and what's after it moved by what was left out of it
            var capped = new StringBuilder(text.Length);
            var trims = new List<(int Start, int End, int Kept)>(runs.Count);
            int copied = 0;
            foreach (var (start, end) in runs)
            {
                if (!Unicode.OverTheLimit(text, start, end, MostMarks)) continue;
                string part = Unicode.Capped(text.Substring(start, end - start), MostMarks);
                capped.Append(text, copied, start - copied).Append(part);
                trims.Add((start, end, part.Length));
                copied = end;
            }
            capped.Append(text, copied, text.Length - copied);
            int Through(int at)
            {
                int moved = at;
                foreach (var (start, end, kept) in trims)
                {
                    if (at >= end) moved -= end - start - kept;
                    else if (at > start) moved -= at - start - Unicode.Capped(text.Substring(start, at - start), MostMarks).Length;
                }
                return moved;
            }
            Change last = changes[changes.Count - 1];
            caret = Through(last.Offset + last.Added);
            // the letters cut short before, but for those in what's capped now; then those it cut short
            var noted = new List<int>();
            if (cuts != null)
                foreach (int cut in cuts)
                    if (!trims.Exists(t => cut >= t.Start && cut < t.End)) noted.Add(Through(cut));
            string result = capped.ToString();
            int shift = 0;
            foreach (var (start, end, kept) in trims)
            {
                Unicode.AtTheLimit(result, start + shift, start + shift + kept, MostMarks, noted);
                shift += kept - (end - start);
            }
            cuts = noted;
            return result;
        }

        // the cut letters (indexes in the text before the changes) where they are after them, without those whose
        // letter went; null with none
        static List<int> Moved(List<int> cuts, List<Change> changes)
        {
            if (cuts == null || cuts.Count == 0) return null;
            var moved = new List<int>(cuts.Count);
            foreach (int cut in cuts)
            {
                int shift = 0;
                bool gone = false;
                foreach (Change change in changes)
                {
                    int was = change.Offset - shift;   // where it began in the text before
                    if (was + change.Removed <= cut)
                    {
                        shift += change.Added - change.Removed;
                        continue;
                    }
                    gone = was <= cut;
                    break;
                }
                if (!gone) moved.Add(cut + shift);
            }
            return moved;
        }

        static void SetCuts(TextBox box, List<int> cuts)
        {
            if (cuts == null || cuts.Count == 0)
            {
                if (box.GetValue(CutsProperty) != null) box.ClearValue(CutsProperty);
                return;
            }
            cuts.Sort();
            for (int i = cuts.Count - 1; i > 0; i--)
                if (cuts[i] == cuts[i - 1]) cuts.RemoveAt(i);
            box.SetValue(CutsProperty, cuts);
        }

        // The box's letters at the limit, noted as cut short: for text that had marks left out before it reached
        // the box (a template's field, whose text then goes into the data box)
        public static void MarkCut(TextBox box)
        {
            var cuts = box.GetValue(CutsProperty) is List<int> had ? new List<int>(had) : new List<int>();
            string text = box.Text;
            Unicode.AtTheLimit(text, 0, text.Length, MostMarks, cuts);
            SetCuts(box, cuts);
        }
    }

    // A text block or run bound to text from outside the program (a scanned code's card is bound in XAML)
    // shows it with the marks on each letter capped, the same as a caption.
    sealed class CapMarks : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is string text ? LongText.Cap(text) : value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value;
    }
}
