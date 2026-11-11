using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SquadEditor.Core.Layout;
using SquadEditor.Core.Memory;

namespace SquadEditor.Core.Demo;

public sealed class DemoGame
{
	private const int PlayerCount = 24;

	private const int ClubCount = 6;

	private const int AttributeCount = 36;

	private const int PositionCount = 14;

	private static readonly string[] Nations = new string[7] { "Turkey", "England", "Spain", "Brazil", "France", "Argentina", "Germany" };

	private static readonly Lazy<DemoGame> LazyInstance = new Lazy<DemoGame>(() => new DemoGame());

	public static DemoGame Instance => LazyInstance.Value;

	public GameLayout Layout { get; }

	public IReadOnlyDictionary<string, ulong> Pins { get; }

	private DemoGame()
	{
		Layout = LoadLayout();
		Random random = new Random(27);
		nint[] array = new nint[6];
		for (int i = 0; i < 6; i++)
		{
			array[i] = CreateClub(i, random);
		}
		nint[] array2 = new nint[24];
		for (int j = 0; j < 24; j++)
		{
			array2[j] = CreatePlayer(j, array[j % 6], random);
		}
		Pins = new Dictionary<string, ulong>
		{
			["PlayerTable"] = (ulong)CreateTable(array2),
			["ClubTable"] = (ulong)CreateTable(array)
		};
	}

	public GameSession CreateSession(IMemory memory)
	{
		GameSession gameSession = new GameSession(memory, Layout);
		foreach (var (name, match) in Pins)
		{
			gameSession.PinAnchor(name, match);
		}
		return gameSession;
	}

	private static GameLayout LoadLayout()
	{
		using Stream stream = typeof(DemoGame).Assembly.GetManifestResourceStream("SquadEditor.Core.Demo.demo.json") ?? throw new InvalidOperationException("Demo layout resource is missing.");
		using StreamReader streamReader = new StreamReader(stream);
		return GameLayout.Parse(streamReader.ReadToEnd());
	}

	private static nint CreateTable(nint[] items)
	{
		nint num = Alloc(16);
		nint num2 = Alloc(items.Length * 8);
		Marshal.WriteInt32(num, 0, items.Length);
		Marshal.WriteIntPtr(num, 8, num2);
		for (int i = 0; i < items.Length; i++)
		{
			Marshal.WriteIntPtr(num2, i * 8, items[i]);
		}
		return num;
	}

	private static nint CreatePlayer(int index, nint club, Random random)
	{
		nint num = Alloc(96);
		nint num2 = Alloc(36);
		short num3 = (short)random.Next(60, 170);
		short val = (short)Math.Min(200, num3 + random.Next(0, 50));
		Marshal.WriteIntPtr(num, 0, Marshal.StringToHGlobalUni($"Demo Player {index + 1}"));
		Marshal.WriteInt32(num, 8, 10000 + index);
		Marshal.WriteByte(num, 12, (byte)random.Next(17, 35));
		Marshal.WriteByte(num, 13, (byte)(index + 1));
		Marshal.WriteInt16(num, 14, num3);
		Marshal.WriteInt16(num, 16, val);
		Marshal.WriteInt32(num, 20, random.Next(5, 120) * 1000);
		Marshal.WriteInt64(num, 24, (long)random.Next(1, 400) * 250000L);
		Marshal.WriteIntPtr(num, 32, num2);
		Marshal.WriteByte(num, 41, (byte)random.Next(6, 21));
		Marshal.WriteByte(num, 42, (byte)((index % 5 == 0) ? 1u : ((index % 7 == 0) ? 2u : 0u)));
		Marshal.WriteByte(num, 43, (byte)random.Next(168, 196));
		Marshal.WriteByte(num, 44, (byte)random.Next(64, 92));
		Marshal.WriteInt64(num, 48, (long)random.Next(0, 4) * 25000000L);
		Marshal.WriteInt16(num, 56, (short)random.Next(2027, 2032));
		Marshal.WriteIntPtr(num, 72, club);
		Marshal.WriteInt16(num, 80, (short)random.Next(1500, 9000));
		Marshal.WriteByte(num, 82, (byte)Math.Clamp((170 - num3) / 18, 0, 6));
		Marshal.WriteByte(num, 83, (byte)random.Next(0, 7));
		Marshal.WriteByte(num, 84, (byte)random.Next(55, 101));
		Marshal.WriteIntPtr(num, 88, Marshal.StringToHGlobalUni(Nations[index % Nations.Length]));
		bool flag = ((index == 2 || index == 10) ? true : false);
		int num4 = (flag ? (21 - index) : 0);
		Marshal.WriteInt16(num, 46, (short)num4);
		Marshal.WriteByte(num, 45, (byte)((index == 6) ? 2u : 0u));
		Marshal.WriteByte(num, 40, (byte)((num4 > 0) ? random.Next(45, 70) : random.Next(80, 101)));
		int num5 = 3 + num3 / 14;
		for (int i = 0; i < 36; i++)
		{
			Marshal.WriteByte(num2, i, (byte)Math.Clamp(num5 + random.Next(-5, 6), 1, 20));
		}
		int num6 = index % 14;
		for (int j = 0; j < 14; j++)
		{
			Marshal.WriteByte(num, 58 + j, (byte)(Math.Abs(j - num6) switch
			{
				0 => 20u, 
				1 => (uint)random.Next(12, 17), 
				2 => (uint)random.Next(6, 11), 
				_ => (uint)random.Next(1, 5), 
			}));
		}
		return num;
	}

	private static nint CreateClub(int index, Random random)
	{
		nint num = Alloc(80);
		Marshal.WriteIntPtr(num, 0, Marshal.StringToHGlobalUni($"Demo Club {(char)(ushort)(65 + index)}"));
		Marshal.WriteInt64(num, 8, (long)random.Next(-20, 300) * 1000000L);
		Marshal.WriteInt64(num, 16, (long)random.Next(0, 80) * 1000000L);
		Marshal.WriteInt64(num, 24, (long)random.Next(100, 2000) * 1000L);
		Marshal.WriteInt16(num, 32, (short)random.Next(2000, 9500));
		Marshal.WriteByte(num, 34, (byte)random.Next(6, 19));
		Marshal.WriteByte(num, 35, (byte)random.Next(6, 19));
		Marshal.WriteByte(num, 36, (byte)random.Next(6, 19));
		Marshal.WriteByte(num, 37, (byte)random.Next(6, 19));
		Marshal.WriteByte(num, 38, (byte)random.Next(6, 19));
		Marshal.WriteByte(num, 39, (index == 4) ? ((byte)1) : ((byte)0));
		Marshal.WriteInt32(num, 40, random.Next(8, 80) * 1000);
		Marshal.WriteInt32(num, 44, random.Next(15, 90));
		Marshal.WriteInt64(num, 48, (long)random.Next(0, 150) * 1000000L);
		Marshal.WriteInt64(num, 56, (long)random.Next(2, 60) * 1000000L);
		Marshal.WriteInt32(num, 64, random.Next(300, 1500));
		Marshal.WriteByte(num, 68, (byte)(index % 4));
		Marshal.WriteByte(num, 69, (byte)random.Next(60, 100));
		Marshal.WriteByte(num, 70, (byte)random.Next(35, 95));
		Marshal.WriteIntPtr(num, 72, Marshal.StringToHGlobalUni($"Demo Stadium {(char)(ushort)(65 + index)}"));
		return num;
	}

	private static nint Alloc(int size)
	{
		nint num = Marshal.AllocHGlobal(size);
		for (int i = 0; i < size; i++)
		{
			Marshal.WriteByte(num, i, 0);
		}
		return num;
	}
}
