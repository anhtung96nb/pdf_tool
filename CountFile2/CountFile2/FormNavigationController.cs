using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CountFile2
{
    public sealed class FormNavigationController : IDisposable
    {
        private readonly Panel host;
        private readonly Dictionary<string, Form> forms = new Dictionary<string, Form>();
        public Form Current { get; private set; }
        public FormNavigationController(Panel panel) { host = panel; }
        public Form Navigate(string key, Func<Form> factory)
        {
            Form next;
            if (!forms.TryGetValue(key, out next))
            {
                next = factory();
                if (next is Form1 || next is frmXuLyOCR) next.AutoScrollMinSize = next.ClientSize;
                next.TopLevel = false; next.FormBorderStyle = FormBorderStyle.None;
                next.MinimumSize = System.Drawing.Size.Empty;
                next.Dock = DockStyle.Fill; next.AutoScroll = true;
                host.Controls.Add(next); forms.Add(key, next);
            }
            if (Current != null && Current != next) Current.Hide();
            Current = next; next.Show(); next.BringToFront(); return next;
        }
        public void Dispose()
        {
            foreach (var form in forms.Values) form.Dispose();
            forms.Clear(); Current = null;
        }
    }
}
