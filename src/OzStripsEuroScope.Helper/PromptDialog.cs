using System;
using System.Drawing;
using System.Windows.Forms;

namespace OzStripsEuroScope.Helper
{
    internal static class PromptDialog
    {
        public static string? Show(string title, string label, string initialValue)
        {
            using (var form = new Form())
            using (var input = new TextBox())
            using (var ok = new Button())
            using (var cancel = new Button())
            using (var caption = new Label())
            {
                form.Text = title;
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.ClientSize = new Size(360, 120);

                caption.Text = label;
                caption.SetBounds(12, 12, 330, 20);

                input.Text = initialValue;
                input.SetBounds(12, 38, 336, 24);
                input.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

                ok.Text = "OK";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(192, 78, 75, 28);

                cancel.Text = "Cancel";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.SetBounds(273, 78, 75, 28);

                form.Controls.AddRange(new Control[] { caption, input, ok, cancel });
                form.AcceptButton = ok;
                form.CancelButton = cancel;

                return form.ShowDialog() == DialogResult.OK ? input.Text : null;
            }
        }
    }
}
