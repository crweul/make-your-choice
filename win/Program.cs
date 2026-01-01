using System;
using System.Windows.Forms;

namespace MakeYourChoice
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetColorMode(SystemColorMode.System);
            Application.Run(new Form1());
        }
    }
}