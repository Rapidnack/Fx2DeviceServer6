using MonoLibUsb;
using MonoLibUsb.Profile;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fx2DeviceServer
{
	public class MonoDeviceServer : IDisposable
	{
		private MonoUsbProfileList profileList = null;
		private CancellationTokenSource cts = null;
		private List<Fx2Device> fx2Devices = new List<Fx2Device>();

		// The first time the Session property is used it creates a new session
		// handle instance in '__sessionHandle' and returns it. Subsequent 
		// request simply return '__sessionHandle'.
		private static MonoUsbSessionHandle __sessionHandle;
		public static MonoUsbSessionHandle Session
		{
			get
			{
				if (ReferenceEquals(__sessionHandle, null))
					__sessionHandle = new MonoUsbSessionHandle();
				return __sessionHandle;
			}
		}

		public MonoDeviceServer()
		{
			int numDevices = -1;

			// Initialize the context.
			if (Session.IsInvalid)
				throw new Exception("Failed to initialize context.");

			MonoUsbApi.SetDebug(Session, 0);
			// Create a MonoUsbProfileList instance.
			profileList = new MonoUsbProfileList();

			cts = new CancellationTokenSource();
			var ct = cts.Token;
			Task.Run(async () =>
			{
				try
				{
					while (!ct.IsCancellationRequested)
					{
						// The list is initially empty.
						// Each time refresh is called the list contents are updated. 
						int ret = profileList.Refresh(Session);
						if (ret < 0) throw new Exception("Failed to retrieve device list.");

						if (numDevices != ret)
						{
							numDevices = ret;
							//Console.WriteLine($"{numDevices} device(s) found.");
							MonoSetDevice();
						}

						await Task.Delay(1000, ct);
					}
				}
				finally
				{
					// Since profile list, profiles, and sessions use safe handles the
					// code below is not required but it is considered good programming
					// to explicitly free and close these handle when they are no longer
					// in-use.
					profileList.Close();
					Session.Close();
				}
			}, ct);
		}

		private bool disposed = false;
		public void Dispose()
		{
			Dispose(true);
		}
		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (disposing)
				{
					foreach (var fx2Device in fx2Devices)
					{
						fx2Device.Dispose();
					}

					if (cts != null)
					{
						cts.Cancel();
					}
				}

				disposed = true;
			}
		}

		private void MonoSetDevice()
		{
			// convert MonoUsbProfileList to List<>
			List<MonoUsbProfile> usbProfiles = new List<MonoUsbProfile>();
			foreach (MonoUsbProfile usbProfile in profileList)
			{
				usbProfiles.Add(usbProfile);
			}

			// DeviceRemoved
			foreach (var fx2Device in fx2Devices.ToArray())
			{
				if (!usbProfiles.Contains(fx2Device.USBProfile))
				{
					fx2Device.Dispose();
					fx2Devices.Remove(fx2Device);
				}
			}

			// DeviceAttached
			foreach (MonoUsbProfile usbProfile in profileList)
			{
				if (usbProfile.DeviceDescriptor.VendorID == 0x04b4 && usbProfile.DeviceDescriptor.ProductID == 0x1004)
				{
					if (fx2Devices.Count(p => p.USBProfile == usbProfile) == 0)
					{
						byte[] response;
						using (var monoDeviceHandle = usbProfile.OpenDeviceHandle())
						{
							response = Fx2Device.ReceiveVendorResponse(null, monoDeviceHandle, (byte)Fx2Device.EVendorRequests.DeviceType, 1);
						}
						if (response == null)
						{
							fx2Devices.Add(new Fx2Device(null, usbProfile));
						}
						else
						{
							Fx2Device.EDeviceType deviceType = (Fx2Device.EDeviceType)response[0];
							switch (deviceType)
							{
								case Fx2Device.EDeviceType.DAC:
								case Fx2Device.EDeviceType.DAC_C:
								case Fx2Device.EDeviceType.DAC_SA: fx2Devices.Add(new DACDevice(null, usbProfile, deviceType)); break;

								case Fx2Device.EDeviceType.ADC:
								case Fx2Device.EDeviceType.ADC_C: fx2Devices.Add(new ADCDevice(null, usbProfile, deviceType)); break;

								case Fx2Device.EDeviceType.ADC_SA: fx2Devices.Add(new BorIPDevice(null, usbProfile, deviceType)); break;

								default: fx2Devices.Add(new Fx2Device(null, usbProfile)); break;
							}
						}
					}
				}
			}
		}
	}
}
