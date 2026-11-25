using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SquadEditor.App.Ui;

/// <summary>
/// Finds logos and faces from the graphics packs FM itself uses (Sports Interactive\...\graphics).
/// Packs map image files to records through config.xml: from="file" to="graphics/pictures/club/1870/logo".
/// </summary>
public sealed class GraphicsIndex
{
	private static readonly Regex Target = new Regex(@"graphics/pictures/(?<kind>[a-z_]+)/(?:r-)?(?<id>\d+)/(?<type>[a-z_ ]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

	private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, IImage?> _images = new ConcurrentDictionary<string, IImage?>();

	public string? Root { get; private set; }

	public int ClubLogos { get; private set; }

	public int Faces { get; private set; }

	/// <summary>Sports Interactive\Football Manager 27\graphics (checks macOS Application Support + Documents).</summary>
	public static string? FindDefaultRoot()
	{
		foreach (string si in PlatformPaths.SportsInteractiveRoots())
		{
			string? found = Directory.GetDirectories(si, "Football Manager*")
				.OrderByDescending(d => d.Contains("27", StringComparison.Ordinal) ? 1 : 0)
				.ThenByDescending(d => d)
				.Select(d => Path.Combine(d, "graphics"))
				.FirstOrDefault(Directory.Exists);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	public static GraphicsIndex Build(string? root)
	{
		var index = new GraphicsIndex { Root = root };
		if (root == null || !Directory.Exists(root))
		{
			return index;
		}
		IEnumerable<string> configs;
		try
		{
			configs = Directory.EnumerateFiles(root, "config.xml", SearchOption.AllDirectories).ToList();
		}
		catch (Exception)
		{
			return index;
		}
		foreach (string config in configs)
		{
			index.ReadConfig(config);
		}
		index.ClubLogos = index._files.Keys.Count(k => k.StartsWith("club:", StringComparison.Ordinal));
		index.Faces = index._files.Keys.Count(k => k.StartsWith("person:", StringComparison.Ordinal));
		return index;
	}

	private void ReadConfig(string config)
	{
		string dir = Path.GetDirectoryName(config)!;
		HashSet<string> present;
		try
		{
			present = new HashSet<string>(Directory.EnumerateFiles(dir).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
		}
		catch (Exception)
		{
			return;
		}
		try
		{
			using XmlReader reader = XmlReader.Create(config, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true });
			while (reader.Read())
			{
				if (reader.NodeType != XmlNodeType.Element || reader.Name != "record")
				{
					continue;
				}
				string? from = reader.GetAttribute("from");
				string? to = reader.GetAttribute("to");
				if (from == null || to == null)
				{
					continue;
				}
				Match m = Target.Match(to);
				if (!m.Success)
				{
					continue;
				}
				string kind = m.Groups["kind"].Value.ToLowerInvariant();
				string type = m.Groups["type"].Value.ToLowerInvariant();
				bool primary = type is "logo" or "portrait" or "flag";
				string key = $"{kind}:{m.Groups["id"].Value}";
				if (!primary && _files.ContainsKey(key))
				{
					continue;
				}
				string? file = from.Contains('/') || from.Contains('\\')
					? Extensions.Select(e => Path.Combine(dir, from.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar) + e)).FirstOrDefault(File.Exists)
					: Extensions.Select(e => from + e).Where(present.Contains).Select(name => Path.Combine(dir, name)).FirstOrDefault();
				if (file != null && (primary || !_files.ContainsKey(key)))
				{
					_files[key] = file;
				}
			}
		}
		catch (Exception)
		{
		}
	}

	public IImage? Club(uint id) => Get("club:" + id, 96);

	public IImage? Person(uint id) => Get("person:" + id, 160);

	private IImage? Get(string key, int size)
	{
		if (!_files.TryGetValue(key, out string? file))
		{
			return null;
		}
		return _images.GetOrAdd(key, _ =>
		{
			try
			{
				using FileStream fs = File.OpenRead(file);
				return Bitmap.DecodeToHeight(fs, size);
			}
			catch (Exception)
			{
				return null;
			}
		});
	}
}
