using CyUSB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Fx2DeviceServer
{
	public class DeviceServer : IDisposable
	{
		private USBDeviceList usbDeviceList = null;
		private List<Fx2Device> fx2Devices = new List<Fx2Device>();

		public DeviceServer()
		{
			usbDeviceList = new USBDeviceList(CyConst.DEVICES_CYUSB);
			usbDeviceList.DeviceAttached += (s, evt) => SetDevice();
			usbDeviceList.DeviceRemoved += (s, evt) => SetDevice();

			SetDevice();
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

					if (usbDeviceList != null)
					{
						usbDeviceList.Dispose();
					}
				}

				disposed = true;
			}
		}

		private void SetDevice()
		{
			// convert USBDeviceList to List<>
			List<CyUSBDevice> usbDevices = new List<CyUSBDevice>();
			foreach (CyUSBDevice usbDevice in usbDeviceList)
			{
				usbDevices.Add(usbDevice);
			}

			// DeviceRemoved
			foreach (var fx2Device in fx2Devices.ToArray())
			{
				if (!usbDevices.Contains(fx2Device.USBDevice))
				{
					fx2Device.Dispose();
					fx2Devices.Remove(fx2Device);
				}
			}

			// DeviceAttached
			foreach (CyUSBDevice usbDevice in usbDeviceList)
			{
				if (usbDevice.VendorID == 0x04b4 && usbDevice.ProductID == 0x1004)
				{
					if (fx2Devices.Count(p => p.USBDevice == usbDevice) == 0)
					{
						byte[] response = Fx2Device.ReceiveVendorResponse(usbDevice, null, (byte)Fx2Device.EVendorRequests.DeviceType, 1);
						if (response == null)
						{
							fx2Devices.Add(new Fx2Device(usbDevice, null));
						}
						else
						{
							Fx2Device.EDeviceType deviceType = (Fx2Device.EDeviceType)response[0];
							switch (deviceType)
							{
								case Fx2Device.EDeviceType.DAC:
								case Fx2Device.EDeviceType.DAC_C:
								case Fx2Device.EDeviceType.DAC_SA: fx2Devices.Add(new DACDevice(usbDevice, null, deviceType)); break;

								case Fx2Device.EDeviceType.ADC:
								case Fx2Device.EDeviceType.ADC_C: fx2Devices.Add(new ADCDevice(usbDevice, null, deviceType)); break;

								case Fx2Device.EDeviceType.ADC_SA: fx2Devices.Add(new BorIPDevice(usbDevice, null, deviceType)); break;

								default: fx2Devices.Add(new Fx2Device(usbDevice, null)); break;
							}
						}
					}
				}
			}
		}
	}
}
