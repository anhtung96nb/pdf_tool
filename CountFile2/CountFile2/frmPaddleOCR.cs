using System;
using System.IO;

namespace CountFile2
{
    public sealed class frmPaddleOCR : frmVietOCR
    {
        public frmPaddleOCR() : base(true) { }
    }

    internal static class PaddleOcrDefaults
    {
        public static string FindPython()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, ".paddleocr-venv", "Scripts", "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "python";
        }
    }
}
