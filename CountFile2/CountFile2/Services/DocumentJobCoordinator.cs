using System;
using System.Threading;

namespace CountFile2.Services
{
    // Acquired and released on the UI thread. Native work uses the token only.
    public sealed class DocumentJobCoordinator
    {
        public static readonly DocumentJobCoordinator Shared = new DocumentJobCoordinator();
        public string ActiveJob { get; private set; }
        public bool IsBusy { get { return ActiveJob != null; } }
        private CancellationTokenSource cancellation;
        public event EventHandler Changed;
        public bool TryBegin(string name, CancellationTokenSource token)
        {
            if (IsBusy) return false;
            ActiveJob = name; cancellation = token; Notify(); return true;
        }
        public void End(CancellationTokenSource token)
        {
            if (!ReferenceEquals(token, cancellation)) return;
            cancellation = null; ActiveJob = null; Notify();
        }
        public void Cancel() { if (cancellation != null) cancellation.Cancel(); }
        private void Notify() { if (Changed != null) Changed(this, EventArgs.Empty); }
    }
}
