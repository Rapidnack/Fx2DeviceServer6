using CyUSB;
using MonoLibUsb;
using MonoLibUsb.Profile;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fx2DeviceServer
{
    public class Fx2Device : IDisposable
    {
        public enum EDeviceType
        {
            Unknown = 0,
            DAC = 1,
            ADC = 2,
			DAC_C = 3, // + control port
			ADC_C = 4, // + control port
			DAC_SA = 5, // slave fifo + avalon packet
			ADC_SA = 6, // slave fifo + avalon packet
		}

		public enum EVendorRequests
		{
			DeviceType = 0xc0,
			DeviceParam = 0xc1,
			SetSampleRate = 0xc2,
			SetSpiCs = 0xc3,
		}

		private static Dictionary<ushort, TcpListener> listenerDict = new Dictionary<ushort, TcpListener>();
		protected List<TcpClient> controlClients = new List<TcpClient>();
		protected const int TIMEOUT = 3000;
		protected IAvalonPacket avalonPacket = null;

		protected EDeviceType DeviceType { get; private set; } = EDeviceType.Unknown;

		private ushort _controlPortNo = 0;
		protected ushort ControlPortNo
		{
			get
			{
				return _controlPortNo;
			}
			set
			{
				_controlPortNo = value;

				if (0 < ControlPortNo)
				{
					var ct = Cts.Token;
					Task.Run(() =>
					{
						TcpListener listener = CreateListener(ControlPortNo);
						try
						{
							listener.Start();
							var addresses = Dns.GetHostAddresses(Dns.GetHostName())
							.Where(p => p.ToString().Contains('.'));								
							Console.WriteLine($"{ControlPortNo}: {string.Join(" ", addresses)}");

							CancellationTokenSource tcpCts = null;
							while (!ct.IsCancellationRequested)
							{
								TcpClient controlClient = listener.AcceptTcpClient();
								Console.WriteLine($"{ControlPortNo}: accepted");

								if (tcpCts != null)
								{
									tcpCts.Cancel();
								}

								tcpCts = new CancellationTokenSource();
								var tcpCt = tcpCts.Token;
								Task.Run(() =>
								{
									controlClients.Add(controlClient);
									try
									{
										using (NetworkStream ns = controlClient.GetStream())
										using (StreamReader sr = new StreamReader(ns, Encoding.ASCII))
										using (StreamWriter sw = new StreamWriter(ns, Encoding.ASCII))
										{
											while (!tcpCt.IsCancellationRequested)
											{
												string str = sr.ReadLine();
												if (string.IsNullOrWhiteSpace(str))
													return; // keep alive
												Console.WriteLine($"{ControlPortNo}: [in] {str.Trim()}");

												ProcessInput(sw, str);
											}
										}
									}
									catch (Exception)
									{
										// nothing to do
									}
									finally
									{
										controlClients.Remove(controlClient);
										Console.WriteLine($"{ControlPortNo}: closed");
									}
								}, tcpCt);
							}
						}
						catch (SocketException ex) when (ex.ErrorCode == 10004)
						{
							// nothing to do
						}
						catch (OperationCanceledException)
						{
							// nothing to do
						}
						catch (Exception ex)
						{
							Console.WriteLine($"{ControlPortNo}: {ex.Message}");
						}
						finally
						{
							listener.Stop();
							//Console.WriteLine($"{ControlPortNo}: listener stopped");
						}
					}, ct);
				}
			}
		}

		protected CancellationTokenSource Cts { get; } = new CancellationTokenSource();

		protected MonoUsbDeviceHandle MonoDeviceHandle { get; private set; } = null;

        public CyUSBDevice USBDevice { get; private set; } = null;

        private MonoUsbProfile _usbProfile = null;
        public MonoUsbProfile USBProfile
        {
            get
            {
                return _usbProfile;
            }
            set
            {
                _usbProfile = value;
                MonoDeviceHandle = _usbProfile.OpenDeviceHandle();
            }
        }

        public Fx2Device(CyUSBDevice usbDevice, MonoUsbProfile usbProfile, EDeviceType deviceType = EDeviceType.Unknown)
        {
            if (usbDevice != null)
            {
                USBDevice = usbDevice;
            }
            if (usbProfile != null)
            {
                USBProfile = usbProfile;
            }
            DeviceType = deviceType;

            if (deviceType == EDeviceType.Unknown)
            {
                Console.WriteLine($"+ {this}");
            }

			if (deviceType == EDeviceType.DAC_SA || deviceType == EDeviceType.ADC_SA)
			{
				if (usbDevice != null)
				{
					CyBulkEndPoint outEndpoint = usbDevice.EndPointOf(0x01) as CyBulkEndPoint;
					CyBulkEndPoint inEndpoint = usbDevice.EndPointOf(0x81) as CyBulkEndPoint;
					avalonPacket = new AvalonPacket(outEndpoint, inEndpoint);
				}
				else
				{
					avalonPacket = new MonoAvalonPacket(MonoDeviceHandle);
				}
			}
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
                    if (Cts != null)
                    {
                        Cts.Cancel();
                    }
                    Console.WriteLine($"- {this}");
                }

                disposed = true;
            }
        }

        public override string ToString()
        {
            return $"{DeviceType}";
        }

        public static byte[] ReceiveVendorResponse(CyUSBDevice usbDevice, MonoUsbDeviceHandle monoDeviceHandle, byte reqCode, int length, ushort value = 0, ushort index = 0)
        {
            if (usbDevice != null)
            {
                CyControlEndPoint ctrlEpt = usbDevice.ControlEndPt;
                ctrlEpt.TimeOut = TIMEOUT;
                ctrlEpt.Direction = CyConst.DIR_FROM_DEVICE;
                ctrlEpt.ReqType = CyConst.REQ_VENDOR;
                ctrlEpt.Target = CyConst.TGT_DEVICE;
                ctrlEpt.ReqCode = reqCode;
                ctrlEpt.Value = value;
                ctrlEpt.Index = index;

                int bytes = length;
                byte[] buffer = new byte[bytes];
                ctrlEpt.XferData(ref buffer, ref bytes);
                if (bytes == buffer.Length)
                {
                    return buffer;
                }
            }
            else
            {
                short bytes = (short)length;
                byte[] data = new byte[bytes];
                byte requestType = CyConst.DIR_FROM_DEVICE + CyConst.REQ_VENDOR + CyConst.TGT_DEVICE;
                int ret = MonoUsbApi.ControlTransfer(monoDeviceHandle, requestType, reqCode, (short)value, (short)index, data, bytes, TIMEOUT);
                if (ret == data.Length)
                {
                    return data;
                }
            }

            return null;
        }

        public static bool SendVendorRequest(CyUSBDevice usbDevice, MonoUsbDeviceHandle monoDeviceHandle, byte reqCode, byte[] data, ushort value = 0, ushort index = 0)
        {
            if (data == null)
            {
                data = new byte[0];
            }

            if (usbDevice != null)
            {
                CyControlEndPoint ctrlEpt = usbDevice.ControlEndPt;
                ctrlEpt.TimeOut = TIMEOUT;
                ctrlEpt.Direction = CyConst.DIR_TO_DEVICE;
                ctrlEpt.ReqType = CyConst.REQ_VENDOR;
                ctrlEpt.Target = CyConst.TGT_DEVICE;
                ctrlEpt.ReqCode = reqCode;
                ctrlEpt.Value = value;
                ctrlEpt.Index = index;

                int bytes = data.Length;
                ctrlEpt.XferData(ref data, ref bytes);
                return bytes == data.Length;
            }
            else
            {
                short bytes = (short)data.Length;
                byte requestType = CyConst.DIR_TO_DEVICE + CyConst.REQ_VENDOR + CyConst.TGT_DEVICE;
                int ret = MonoUsbApi.ControlTransfer(monoDeviceHandle, requestType, reqCode, (short)value, (short)index, data, bytes, TIMEOUT);
                return ret == data.Length;
            }
        }

        protected byte[] ReceiveVendorResponse(byte reqCode, int length, ushort value = 0, ushort index = 0)
        {
            return ReceiveVendorResponse(USBDevice, MonoDeviceHandle, reqCode, length, value, index);
        }

        protected bool SendVendorRequest(byte reqCode, byte[] data, ushort value = 0, ushort index = 0)
        {
            return SendVendorRequest(USBDevice, MonoDeviceHandle, reqCode, data, value, index);
        }

		protected virtual void ProcessInput(StreamWriter sw, string s)
		{
			switch (DeviceType)
			{
				case EDeviceType.DAC_C:
				case EDeviceType.ADC_C:
					if (s.StartsWith("*Rate:"))
					{
						string param = s.Split(':')[1];

						uint rate = Convert.ToUInt32(param);

						byte[] response = ReceiveVendorResponse((byte)EVendorRequests.SetSampleRate, 4,
							(ushort)(rate & 0xffff), (ushort)((rate >> 16) & 0xffff));

						rate = (uint)(response[0] + (response[1] << 8) + (response[2] << 16) + (response[3] << 24));
						WriteLine(sw, rate.ToString());
					}
					break;

				case EDeviceType.DAC_SA:
				case EDeviceType.ADC_SA:
					if (s.StartsWith("*W32:"))
					{
						string param = s.Split(':')[1];

						SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 0);
						try
						{
							string[] sarray = param.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
							UInt32 addr = Convert.ToUInt32(sarray[0], sarray[0].StartsWith("0x") ? 16 : 10);
							UInt32 data = Convert.ToUInt32(sarray[1], sarray[1].StartsWith("0x") ? 16 : 10);
							avalonPacket.WritePacket(addr, data);
						}
						finally
						{
							SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 1);
						}
					}
					else if (s.StartsWith("*R32:"))
					{
						string param = s.Split(':')[1];

						SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 0);
						try
						{
							UInt32 addr = Convert.ToUInt32(param, param.StartsWith("0x") ? 16 : 10);
							UInt32 data = avalonPacket.ReadPacket(addr);
							if (param.StartsWith("0x"))
							{
								WriteLine(sw, "0x" + data.ToString("x"));
							}
							else
							{
								WriteLine(sw, data.ToString());
							}
						}
						finally
						{
							SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 1);
						}
					}
					break;
			}
		}

		protected void WriteLine(StreamWriter sw, string s)
		{
			sw.WriteLine(s);
			sw.Flush();
			Console.WriteLine($"{ControlPortNo}: [out] {s}");
		}

		protected static TcpListener CreateListener(ushort port)
		{
			if (listenerDict.ContainsKey(port))
			{
				TcpListener oldListener = listenerDict[port];
				oldListener.Stop();
				listenerDict.Remove(port);
			}
			TcpListener newListener = new TcpListener(IPAddress.Any, port);
			listenerDict.Add(port, newListener);

			return newListener;
		}
	}
}
