namespace NjuTrayApp.UI;

public static class InputDialog
{
    public static string? Show(string title, string label, string initialValue = "")
    {
        using var form = new Form();
        using var okButton = new Button();
        using var cancelButton = new Button();
        using var textBox = new TextBox();
        using var promptLabel = new Label();

        form.Text = title;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.ClientSize = new Size(420, 120);

        promptLabel.Text = label;
        promptLabel.AutoSize = true;
        promptLabel.Left = 10;
        promptLabel.Top = 10;

        textBox.Left = 10;
        textBox.Top = 35;
        textBox.Width = 390;
        textBox.Text = initialValue;

        okButton.Text = "OK";
        okButton.Left = 245;
        okButton.Top = 75;
        okButton.Width = 75;
        okButton.DialogResult = DialogResult.OK;

        cancelButton.Text = "Anuluj";
        cancelButton.Left = 325;
        cancelButton.Top = 75;
        cancelButton.Width = 75;
        cancelButton.DialogResult = DialogResult.Cancel;

        form.Controls.Add(promptLabel);
        form.Controls.Add(textBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
    }
}
