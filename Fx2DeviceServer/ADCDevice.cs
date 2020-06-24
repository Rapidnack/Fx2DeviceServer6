using CyUSB;
using MonoLibUsb;
using MonoLibUsb.Profile;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Fx2DeviceServer
{
    public class ADCDevice : Fx2Device
    {
        private ushort dataPortNo = 0;
        private CyBulkEndPoint endpoint2 = null;

        public ADCDevice(CyUSBDevice usbDevice, MonoUsbProfile usbProfile, EDeviceType deviceType)
            : base(usbDevice, usbProfile, deviceType)
        {
			if (deviceType == EDeviceType.ADC)
			{
				byte[] response = ReceiveVendorResponse((byte)EVendorRequests.DeviceParam, 2);
				dataPortNo = (ushort)(response[0] + (response[1] << 8));
			}
			else
			{
				byte[] response = ReceiveVendorResponse((byte)EVendorRequests.DeviceParam, 4);
				dataPortNo = (ushort)(response[0] + (response[1] << 8));
				ControlPortNo = (ushort)(response[2] + (response[3] << 8));
			}

			Console.WriteLine($"+ {this}");

            if (USBDevice != null)
            {
                endpoint2 = USBDevice.EndPointOf(0x82) as CyBulkEndPoint;
            }

            var ct = Cts.Token;
            Task.Run(() =>
            {
				while (!ct.IsCancellationRequested)
				{
					if (ControlPortNo > 0 && controlClients.Count == 0)
					{
						Thread.Sleep(100);
						continue;
					}

					UdpClient udp = new UdpClient();
					try
					{
						int maxPacketSize;
						if (USBDevice != null)
						{
							maxPacketSize = endpoint2.MaxPktSize;
						}
						else
						{
							maxPacketSize = MonoUsbApi.GetMaxPacketSize(USBProfile.ProfileHandle, 0x82);
						}
						byte[] inData = new byte[maxPacketSize];
						byte[] outData = null;
						int outDataPos = 0;

						while (!ct.IsCancellationRequested && !(ControlPortNo > 0 && controlClients.Count == 0))
						{
							int xferLen = inData.Length;
							bool ret = false;
							if (USBDevice != null)
							{
								ret = endpoint2.XferData(ref inData, ref xferLen);
							}
							else
							{
								ret = MonoUsbApi.BulkTransfer(MonoDeviceHandle, 0x82, inData, inData.Length, out xferLen, TIMEOUT) == 0;
							}
							if (ret == false)
								break;

							int inDataPos = 0;
							while (!ct.IsCancellationRequested && inDataPos < xferLen)
							{
								if (outData == null)
								{
									outData = new byte[1472];
								}
								while (outDataPos < outData.Length && inDataPos < xferLen)
								{
									outData[outDataPos++] = inData[inDataPos++];
								}

								if (outDataPos == outData.Length)
								{
									List<string> remoteAddrList = new List<string>();
									foreach (var client in controlClients.ToArray())
									{
										string remoteAddr;
										try
										{
											remoteAddr = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
										}
										catch
										{
											continue;
										}

										if (remoteAddrList.Contains(remoteAddr) == false)
										{
											remoteAddrList.Add(remoteAddr);
											udp.Send(outData, outData.Length, remoteAddr, dataPortNo);
										}
									}

									if (ControlPortNo == 0)
									{
										udp.Send(outData, outData.Length, "127.0.0.1", dataPortNo);
									}

									outData = null;
									outDataPos = 0;
								}
							}
						}
					}
					catch (OperationCanceledException)
					{
						// nothing to do
					}
					catch (Exception ex)
					{
						Console.WriteLine($"{dataPortNo}: {ex.Message}");
					}
					finally
					{
						udp.Close();
					}

					Thread.Sleep(1000);
				}
			}, ct);
        }

        public override string ToString()
        {
			if (DeviceType == EDeviceType.ADC_C)
			{
				return $"{DeviceType} {dataPortNo} {ControlPortNo}";
			}
			else
			{
				return $"{DeviceType} {dataPortNo}";
			}
        }
    }
}
