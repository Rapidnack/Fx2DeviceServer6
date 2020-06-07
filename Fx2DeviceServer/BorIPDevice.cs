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
	public class BorIPDevice : Fx2Device
	{
		private enum ERunningState
		{
			Stop,
			Start,
			Continued
		}


		protected class BorIPClient
		{
			public TcpClient TCPClient { get; set; } = null;

			private string _destAddr = string.Empty;
			public string DestAddr
			{
				get
				{
					if (string.IsNullOrWhiteSpace(_destAddr))
					{
						return ((IPEndPoint)TCPClient.Client.RemoteEndPoint).Address.ToString();
					}
					return _destAddr;
				}
				set
				{
					_destAddr = value;
				}
			}

			public int DestPort { get; set; } = DEFAULT_DESTPORT;

			public bool Header { get; set; } = true;

			public BorIPClient(TcpClient tcpClient)
			{
				TCPClient = tcpClient;
			}
		}


		private const int NUM_SAMPLES = 1024;

		private const int MIN_RATE = 37500; // 37.5k/75k/150k/300k/600k/1.2M
		private const int MIN_RATE_MUL = 0; //   0/   1/   2/   3/   4/   5
		private const int MAX_RATE_MUL = 5;

		private const int MIN_FREQ = 0;
		private const int MAX_FREQ = 475000000; // 475MHz

		private const int MIN_GAIN = 0;
		private const int MAX_GAIN = 16;

		private const uint FPGA_CLOCK = 48000000; // 48MHz
		private const int DEFAULT_DESTPORT = 28888;
		private const int BORIP_SERVERPORT = 28888;

		private ushort dataPortNo = 0; // port number of ADCDevice
		private CyBulkEndPoint endpoint2 = null;

		private List<BorIPClient> borIPClients = new List<BorIPClient>();
		private int sequence;


		private int _rateMul = 3; // 400k
		private int RateMul
		{
			get
			{
				return _rateMul;
			}
			set
			{
				if (value < MIN_RATE_MUL) value = MIN_RATE_MUL;
				if (MAX_RATE_MUL < value) value = MAX_RATE_MUL;
				_rateMul = value;

				if (avalonPacket != null)
				{
					SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 0);
					try
					{
						avalonPacket.WritePacket(0x30, (uint)RateMul);
					}
					finally
					{
						SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 1);
					}
				}
			}
		}

		private int Rate
		{
			get
			{
				return MIN_RATE << RateMul;
			}
			set
			{
				RateMul = (int)Math.Ceiling(Math.Log(value / MIN_RATE) / Math.Log(2));
			}
		}

		private int _freq = 1000000;
		private int Freq
		{
			get
			{
				return _freq;
			}
			set
			{
				if (value < MIN_FREQ) value = MIN_FREQ;
				if (MAX_FREQ < value) value = MAX_FREQ;
				_freq = value;

				if (avalonPacket != null)
				{
					SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 0);
					try
					{
						avalonPacket.WritePacket(0x10, freqToPhaseInc(FPGA_CLOCK, Freq));

						int bank = freqToBank(FPGA_CLOCK, Freq);
						int swapIQ = bank % 2;
						avalonPacket.WritePacket(0x20, (uint)swapIQ);
					}
					finally
					{
						SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 1);
					}
				}
			}
		}

		private int DDC
		{
			get
			{
				return (int)freqToDDC(FPGA_CLOCK, Freq);
			}
		}

		private int _gain = 8;
		private int Gain
		{
			get
			{
				return _gain;
			}
			set
			{
				if (value < MIN_GAIN) value = MIN_GAIN;
				if (MAX_GAIN < value) value = MAX_GAIN;
				_gain = value;

				if (avalonPacket != null)
				{
					SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 0);
					try
					{
						avalonPacket.WritePacket(0x40, (uint)Gain);
					}
					finally
					{
						SendVendorRequest((byte)EVendorRequests.SetSpiCs, null, 1);
					}
				}
			}
		}

		private ERunningState RunningState { get; set; } = ERunningState.Stop;


		public BorIPDevice(CyUSBDevice usbDevice, MonoUsbProfile usbProfile, EDeviceType deviceType)
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


			var ct = Cts.Token;

			Task.Run(() =>
			{
				TcpListener listener = CreateListener(BORIP_SERVERPORT);
				try
				{
					listener.Start();
					var addresses = Dns.GetHostAddresses(Dns.GetHostName())
					.Where(p => p.ToString().Contains('.'));
					Console.WriteLine($"{BORIP_SERVERPORT}: {string.Join(" ", addresses)}");

					CancellationTokenSource tcpCts = null;
					while (!ct.IsCancellationRequested)
					{
						TcpClient client = listener.AcceptTcpClient();
						Console.WriteLine($"{BORIP_SERVERPORT}: accepted");

						if (tcpCts != null)
						{
							tcpCts.Cancel();
						}

						tcpCts = new CancellationTokenSource();
						var tcpCt = tcpCts.Token;
						Task.Run(() =>
						{
							BorIPClient borIPClient = new BorIPClient(client);
							borIPClients.Add(borIPClient);
							try
							{
								using (NetworkStream ns = client.GetStream())
								using (StreamReader sr = new StreamReader(ns, Encoding.ASCII))
								using (StreamWriter sw = new StreamWriter(ns, Encoding.ASCII))
								{
									BorIPWriteLine(sw, "DEVICE -");

									while (!tcpCt.IsCancellationRequested)
									{
										string str = sr.ReadLine();
										if (string.IsNullOrWhiteSpace(str))
											return; // keep alive
										Console.WriteLine($"{BORIP_SERVERPORT}: [in] {str.Trim()}");

										BorIPProcessInput(borIPClient, sw, str);
									}
								}
							}
							catch (Exception)
							{
								// nothing to do
							}
							finally
							{
								borIPClients.Remove(borIPClient);
								Console.WriteLine($"{BORIP_SERVERPORT}: closed");
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
					Console.WriteLine($"{BORIP_SERVERPORT}: {ex.Message}");
				}
				finally
				{
					listener.Stop();
					//Console.WriteLine($"{BORIP_PORTNO}: listener stopped");
				}
			}, ct);


			if (USBDevice != null)
			{
				endpoint2 = USBDevice.EndPointOf(0x82) as CyBulkEndPoint;
			}

			Task.Run(() =>
			{
				while (!ct.IsCancellationRequested)
				{
					if (borIPClients.Count == 0 && (ControlPortNo > 0 && controlClients.Count == 0))
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
						byte[] borIPData = null;
						int borIPDataPos = 0;

						while (!ct.IsCancellationRequested &&
							!(borIPClients.Count == 0 && (ControlPortNo > 0 && controlClients.Count == 0)))
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
								if (borIPData == null)
								{
									borIPData = new byte[4 + 4 * NUM_SAMPLES];
									borIPData[borIPDataPos++] = (byte)((RunningState == ERunningState.Start) ? 0x10 : 0x00);
									RunningState = ERunningState.Continued;
									borIPData[borIPDataPos++] = 0;
									borIPData[borIPDataPos++] = (byte)(sequence & 0xff);
									borIPData[borIPDataPos++] = (byte)((sequence >> 8) & 0xff);
								}

								while (outDataPos < outData.Length && borIPDataPos < borIPData.Length && inDataPos < xferLen)
								{
									byte b = inData[inDataPos++];
									outData[outDataPos++] = b;
									borIPData[borIPDataPos++] = b;
								}

								if (borIPDataPos == borIPData.Length)
								{
									foreach (var client in borIPClients.ToArray())
									{
										if (client.Header)
										{
											string remoteAddr;
											try
											{
												remoteAddr = client.DestAddr;
											}
											catch
											{
												continue;
											}

											udp.Send(borIPData, borIPData.Length, remoteAddr, client.DestPort);
										}
									}

									borIPData = null;
									borIPDataPos = 0;
								}

								if (outDataPos == outData.Length)
								{
									foreach (var client in borIPClients.ToArray())
									{
										if (client.Header == false)
										{
											string remoteAddr;
											try
											{
												remoteAddr = client.DestAddr;
											}
											catch
											{
												continue;
											}

											udp.Send(outData, outData.Length, remoteAddr, client.DestPort);
										}
									}

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

										udp.Send(outData, outData.Length, remoteAddr, dataPortNo);
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
					//catch (Exception ex)
					//{
					//	Console.WriteLine($"BorIP: {ex.Message}");
					//}
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

		protected virtual void BorIPProcessInput(BorIPClient borIPClient, StreamWriter sw, string str)
		{
			if (str.StartsWith("DEVICE -", StringComparison.CurrentCultureIgnoreCase) ||
				str.StartsWith("DEVICE 0", StringComparison.CurrentCultureIgnoreCase))
			{
				string s = $"DEVICE FX2" +
					$"|{MIN_GAIN}.000|{MAX_GAIN}.000|1.000" +
					$"|{MIN_RATE << MAX_RATE_MUL}.000" +
					$"|{NUM_SAMPLES}" +
					$"|RX" +
					$"|00000000" +
					$"|default" +
					$"|default";
				BorIPWriteLine(sw, s);
			}
			else if (str.StartsWith("DEVICE !", StringComparison.CurrentCultureIgnoreCase))
			{
				BorIPWriteLine(sw, "DEVICE -");
			}
			else if (str.StartsWith("RATE", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					string s = str.Split(new char[] { ' ' }, 2)[1];
					Rate = (int)Math.Round(double.Parse(s));
					if (RunningState == ERunningState.Continued)
					{
						sequence = 0;
						RunningState = ERunningState.Start;
					}
					BorIPWriteLine(sw, $"RATE OK {Rate}.000");
				}
				else
				{
					BorIPWriteLine(sw, $"RATE {Rate}.000");
				}
			}
			else if (str.StartsWith("FREQ", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					string s = str.Split(new char[] { ' ' }, 2)[1];
					int value = (int)Math.Round(double.Parse(s));
					Freq = value;
					if (RunningState == ERunningState.Continued)
					{
						sequence = 0;
						RunningState = ERunningState.Start;
					}

					string ret = "OK";
					if (value < MIN_FREQ) ret = "LOW";
					if (MAX_FREQ < value) ret = "HIGH";

					BorIPWriteLine(sw, $"FREQ {ret} 0.000 0.000 {DDC}.000 {DDC}.000");
				}
				else
				{
					BorIPWriteLine(sw, $"FREQ {Freq}.000");
				}
			}
			else if (str.StartsWith("GAIN", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					string s = str.Split(new char[] { ' ' }, 2)[1];
					Gain = (int)Math.Round(double.Parse(s));
					if (RunningState == ERunningState.Continued)
					{
						sequence = 0;
						RunningState = ERunningState.Start;
					}
					BorIPWriteLine(sw, "GAIN OK");
				}
				else
				{
					BorIPWriteLine(sw, $"GAIN {Gain}.000");
				}
			}
			else if (str.StartsWith("ANTENNA", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					BorIPWriteLine(sw, "ANTENNA OK");
				}
				else
				{
					BorIPWriteLine(sw, "ANTENNA RX");
				}
			}
			else if (str.StartsWith("CLOCK_SRC", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					BorIPWriteLine(sw, "CLOCK_SRC OK");
				}
				else
				{
					BorIPWriteLine(sw, "CLOCK_SRC default");
				}
			}
			else if (str.StartsWith("TIME_SRC", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					BorIPWriteLine(sw, "TIME_SRC OK");
				}
				else
				{
					BorIPWriteLine(sw, "TIME_SRC default");
				}
			}
			else if (str.StartsWith("DEST", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					string s = str.Split(new char[] { ' ' }, 2)[1];
					string[] sArray = s.Split(new char[] { ':' }, 2);
					try
					{
						borIPClient.DestAddr = sArray[0];
						if (sArray.Length > 1)
						{
							borIPClient.DestPort = int.Parse(sArray[1]);
						}
						BorIPWriteLine(sw, $"DEST OK {borIPClient.DestAddr}:{borIPClient.DestPort}");
					}
					catch
					{
						BorIPWriteLine(sw, "DEST FAIL Failed to set destination");
					}
				}
				else
				{
					BorIPWriteLine(sw, $"DEST {borIPClient.DestAddr}:{borIPClient.DestPort}");
				}
			}
			else if (str.StartsWith("HEADER", StringComparison.CurrentCultureIgnoreCase))
			{
				if (str.Contains(" "))
				{
					string s = str.Split(new char[] { ' ' }, 2)[1];
					borIPClient.Header = (s == "ON");
					BorIPWriteLine(sw, "HEADER OK");
				}
				else
				{
					string header = borIPClient.Header ? "ON" : "OFF";
					BorIPWriteLine(sw, $"HEADER {header}");
				}
			}
			else if (str.StartsWith("GO", StringComparison.CurrentCultureIgnoreCase))
			{
				sequence = 0;
				RunningState = ERunningState.Start;
				BorIPWriteLine(sw, "GO OK");
			}
			else if (str.StartsWith("STOP", StringComparison.CurrentCultureIgnoreCase))
			{
				RunningState = ERunningState.Stop;
				BorIPWriteLine(sw, "STOP OK");
			}
			else
			{
				BorIPWriteLine(sw, $"{str} UNKNOWN");
			}
		}

		private void BorIPWriteLine(StreamWriter sw, string s)
		{
			sw.WriteLine(s);
			sw.Flush();
			Console.WriteLine($"{BORIP_SERVERPORT}: [out] {s}");
		}

		private int freqToBank(double clk, double freq)
		{ // in Hz
			double clkDiv2 = clk / 2;
			return (int)Math.Floor(freq / clkDiv2);
		}

		private double freqToDDC(double clk, double freq)
		{ // in Hz
			double clkDiv2 = clk / 2;
			int bank = (int)Math.Floor(freq / clkDiv2);
			double lo = freq - clkDiv2 * bank;
			if ((bank % 2) == 1)
				lo = clkDiv2 - lo;

			return lo;
		}

		private uint freqToPhaseInc(double clk, double freq)
		{ // in Hz
			double phaseInc360 = (double)0x80000000UL * 2; // 32 bits full scale

			return (uint)(phaseInc360 * (freqToDDC(clk, freq) / clk));
		}
	}
}
