using System;
using System.Diagnostics;
using System.Threading;

namespace Fx2DeviceServer
{
	class Program
	{
		static void Main(string[] args)
		{
			try
			{
				using (Process p = Process.GetCurrentProcess())
				{
					p.PriorityClass = ProcessPriorityClass.RealTime;
				}
			}
			catch (Exception)
			{
				Console.WriteLine("Usage: sudo mono CuiFx2DeviceServer.exe");
				return;
			}

			using (MonoDeviceServer monoDeviceServer = new MonoDeviceServer())
			{
				while (true)
				{
					Thread.Sleep(1000);
				}
			}
		}
	}
}
