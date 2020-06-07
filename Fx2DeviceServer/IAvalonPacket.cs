using System;

namespace Fx2DeviceServer
{
	public interface IAvalonPacket
	{
		void PrintBytes(string title, byte[] bytes);
		byte[] WritePacket(uint addr, uint data);
		byte[] WritePacket(uint addr, uint[] array, int start, int length, bool isIncremental = false);
		uint ReadPacket(uint addr);
		int ReadPacket(uint addr, uint[] array, int start, int length, bool isIncremental = false);
	}
}
