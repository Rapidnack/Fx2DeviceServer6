using CyUSB;
using MonoLibUsb;
using MonoLibUsb.Profile;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Fx2DeviceServer
{
    public class DACDevice : Fx2Device
    {
        private ushort dataPortNo = 0;
        private CyBulkEndPoint endpoint2 = null;

        public DACDevice(CyUSBDevice usbDevice, MonoUsbProfile usbProfile, EDeviceType deviceType)
            : base(usbDevice, usbProfile, deviceType)
        {
			if (deviceType == EDeviceType.DAC)
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
                endpoint2 = USBDevice.EndPointOf(0x02) as CyBulkEndPoint;
            }

            var ct = Cts.Token;
            Task.Run(() =>
            {
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
						if (ControlPortNo > 0 && controlClients.Count == 0)
						{
							Thread.Sleep(100);
							continue;
						}

						TcpClient client = null;
                        try
                        {
							string remoteAddr;
							if (ControlPortNo == 0)
							{
								remoteAddr = "127.0.0.1";
							}
							else
							{
								try
								{
									remoteAddr = ((IPEndPoint)controlClients[0].Client.RemoteEndPoint).Address.ToString();
								}
								catch
								{
									continue;
								}
							}
							client = new TcpClient(remoteAddr, dataPortNo);
                        }
                        catch (SocketException ex)
                        {
                            if (ex.ErrorCode == 10061)
                            {
                                Thread.Sleep(1000);
                                continue;
                            }

                            throw ex;
                        }
                        Console.WriteLine($"{dataPortNo}: accepted",
                            DeviceType,
                            ((IPEndPoint)client.Client.RemoteEndPoint).Address,
                            ((IPEndPoint)client.Client.RemoteEndPoint).Port);

                        NetworkStream ns = client.GetStream();
                        try
                        {
                            int maxPacketSize;
                            if (USBDevice != null)
                            {
                                maxPacketSize = endpoint2.MaxPktSize;
                            }
                            else
                            {
                                maxPacketSize = MonoUsbApi.GetMaxPacketSize(USBProfile.ProfileHandle, 0x02);
                            }
                            byte[] inData = new byte[64 * 1024];
                            byte[] outData = new byte[maxPacketSize - 16]; // Some PCs cannot send 1024 bytes
							int outDataPos = 0;

                            while (!ct.IsCancellationRequested && !(ControlPortNo > 0 && controlClients.Count == 0))
                            {
                                int resSize = ns.Read(inData, 0, inData.Length);
                                if (resSize == 0)
                                    break;

                                int inDataLen = resSize;
                                int inDataPos = 0;
                                while (!ct.IsCancellationRequested && inDataPos < inDataLen)
                                {
                                    while (outDataPos < outData.Length && inDataPos < inDataLen)
                                    {
                                        outData[outDataPos++] = inData[inDataPos++];
                                    }

                                    if (outDataPos == outData.Length)
                                    {
                                        int xferLen = outData.Length;
                                        bool ret = false;
                                        if (USBDevice != null)
                                        {
                                            ret = endpoint2.XferData(ref outData, ref xferLen);
                                        }
                                        else
                                        {
                                            ret = MonoUsbApi.BulkTransfer(MonoDeviceHandle, 0x02, outData, outData.Length, out xferLen, TIMEOUT) == 0;
                                        }
                                        if (ret == false || xferLen == 0)
                                            break;
                                        if (xferLen != outData.Length)
                                        {
                                            Console.WriteLine($"{dataPortNo}: the response size {xferLen} not equal to the requested size {outData.Length}");
                                        }
                                        outDataPos = 0;
                                    }
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // nothing to do
                        }
                        finally
                        {
                            ns.Close();
                            client.Close();
                            Console.WriteLine($"{dataPortNo}: closed");
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
            }, ct);
        }

        public override string ToString()
        {
			if (DeviceType == EDeviceType.DAC_C)
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
