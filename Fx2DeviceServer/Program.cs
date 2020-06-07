using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Fx2DeviceServer
{
    static class Program
    {
		private static Mutex mutex = new Mutex(false, Application.ProductName);

		/// <summary>
		/// アプリケーションのメイン エントリ ポイントです。
		/// </summary>
		[STAThread]
        static void Main()
        {
			if (mutex.WaitOne(0, false) == false)
				return;

			Application.ThreadException += (s, e) =>
            {
                Console.WriteLine(
                    "ThreadException: {0}, {1}\r\n{2}\r\n", e.Exception.TargetSite, e.Exception.Message, e.Exception.StackTrace);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null)
                {
                    Console.WriteLine(
                        "UnhandledException: {0}, {1}\r\n{2}\r\n", ex.TargetSite, ex.Message, ex.StackTrace);
                }
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}
