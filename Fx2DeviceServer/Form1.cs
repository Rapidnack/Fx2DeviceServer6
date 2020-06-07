using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace Fx2DeviceServer
{
	public partial class Form1 : Form
	{
		DeviceServer deviceServer;
		MonoDeviceServer monoDeviceServer;

		public Form1()
		{
			InitializeComponent();
		}

		private void Form1_Load(object sender, EventArgs e)
		{
			FileVersionInfo ver = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
			this.Text = $"FX2 Device Server {ver.ProductMajorPart}.{ver.ProductMinorPart}.{ver.ProductPrivatePart}";

			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				using (Process p = Process.GetCurrentProcess())
				{
					p.PriorityClass = ProcessPriorityClass.RealTime;
				}

				deviceServer = new DeviceServer();
			}
			else if (Environment.OSVersion.Platform == PlatformID.Unix)
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
					Console.WriteLine("Usage: sudo mono Fx2DeviceServer.exe");
					return;
				}

				monoDeviceServer = new MonoDeviceServer();
			}
		}

		private void Form1_FormClosing(object sender, FormClosingEventArgs e)
		{
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				deviceServer.Dispose();
			}
			else if (Environment.OSVersion.Platform == PlatformID.Unix)
			{
				monoDeviceServer.Dispose();
			}
		}
	}
}
